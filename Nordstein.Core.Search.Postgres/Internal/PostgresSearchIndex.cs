using System.Data.Common;
using System.Globalization;
using System.Text;
using System.Text.Json;

namespace Nordstein.Core.Search.Postgres.Internal;

/// <summary>
/// One index on PostgreSQL. Search is one statement: each leg filters on the facets inside its own
/// index scan (HNSW for the vector leg, the tsvector GIN for the keyword leg), ranks its best
/// candidates, fusion sums <c>1 / (k + rank)</c>, and only the fused top is joined back for its
/// text.
/// </summary>
/// <remarks>
/// <para>
/// The vector leg runs with pgvector's iterative scan, so a filtered HNSW walk keeps going until it
/// has enough passing candidates instead of returning fewer. There is no trigram leg: similarity
/// over prose rechecks a large part of the table on every query.
/// </para>
/// <para>
/// Rows travel as one JSON parameter expanded by <c>jsonb_to_recordset</c>, so a batch is one
/// statement and needs no driver-specific bulk API. Stateless; safe for concurrent callers as far
/// as the underlying session is.
/// </para>
/// </remarks>
internal sealed class PostgresSearchIndex : ISearchIndex
{
    private const int MaxListLimit = 1000;
    private const int UpsertBatchSize = 200;
    private const int EmbeddingBatchSize = 50;

    private readonly SearchSession session;
    private readonly IndexObjects objects;

    public PostgresSearchIndex(SearchIndexDefinition definition, SearchSession session)
    {
        ArgumentNullException.ThrowIfNull(definition);
        Definition = definition;
        this.session = session;
        objects = new IndexObjects(definition);
    }

    public SearchIndexDefinition Definition { get; }

    public async Task UpsertAsync(IReadOnlyCollection<SearchEntry> entries, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entries);
        SearchEntry[] rows = [.. entries];
        if (rows.Any(entry => entry is null))
        {
            throw new ArgumentNullException(nameof(entries), "An entry is null.");
        }

        if (rows.Select(entry => entry.Id).Distinct().Count() != rows.Length)
        {
            throw new ArgumentException("Two entries share an id.", nameof(entries));
        }

        if (rows.Length == 0)
        {
            return;
        }

        // A changed text loses its vector: the SET expressions read the stored row, so the CASE
        // compares the old text with the new one.
        string sql =
            $"""
            INSERT INTO {objects.Table} (id, owner_id, text, facets)
            SELECT r.id, r.owner_id, r.text, r.facets
            FROM jsonb_to_recordset(@rows::jsonb) AS r(id uuid, owner_id uuid, text text, facets text[])
            ON CONFLICT (id) DO UPDATE SET
                owner_id = EXCLUDED.owner_id,
                facets = EXCLUDED.facets,
                embedding = CASE WHEN {objects.Table}.text = EXCLUDED.text THEN {objects.Table}.embedding ELSE NULL END,
                text = EXCLUDED.text
            """;
        await session.RunAsync(
            async commands =>
            {
                foreach (SearchEntry[] batch in rows.Chunk(UpsertBatchSize))
                {
                    string json = JsonSerializer.Serialize(batch.Select(entry => new
                    {
                        id = entry.Id,
                        owner_id = entry.OwnerId,
                        text = entry.Text,
                        facets = entry.Facets.Order(StringComparer.Ordinal),
                    }));
                    await commands.ExecuteAsync(sql, cancellationToken, ("rows", json)).ConfigureAwait(false);
                }

                return true;
            },
            cancellationToken).ConfigureAwait(false);
    }

    public Task<IReadOnlyList<SearchEntry>> ListWithoutEmbeddingAsync(int limit, Guid? afterId = null, CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(limit, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(limit, MaxListLimit);

        // Guid.Empty sorts before every uuid, so "no start" needs no second statement.
        return session.RunAsync(
            commands => commands.QueryAsync(
                $"SELECT id, owner_id, text, facets FROM {objects.Table} WHERE embedding IS NULL AND id > @after ORDER BY id LIMIT @limit",
                reader => new SearchEntry(reader.GetGuid(0), reader.GetGuid(1), reader.GetString(2), reader.GetFieldValue<string[]>(3)),
                cancellationToken,
                ("after", afterId ?? Guid.Empty),
                ("limit", limit)),
            cancellationToken);
    }

    public Task<long> CountWithoutEmbeddingAsync(CancellationToken cancellationToken = default)
        => session.RunAsync(
            async commands => (await commands.QueryAsync(
                $"SELECT count(*) FROM {objects.Table} WHERE embedding IS NULL",
                reader => reader.GetInt64(0),
                cancellationToken).ConfigureAwait(false))[0],
            cancellationToken);

    public async Task<int> SetEmbeddingsAsync(IReadOnlyCollection<SearchEmbedding> embeddings, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(embeddings);
        SearchEmbedding[] rows = [.. embeddings];
        if (rows.Any(embedding => embedding is null))
        {
            throw new ArgumentNullException(nameof(embeddings), "An embedding is null.");
        }

        if (rows.Select(embedding => embedding.EntryId).Distinct().Count() != rows.Length)
        {
            throw new ArgumentException("Two embeddings share an entry id.", nameof(embeddings));
        }

        // Every vector is checked before the first statement, so a bad one stores nothing.
        var payload = rows
            .Select(embedding => new
            {
                id = embedding.EntryId,
                text = embedding.Text,
                embedding = embedding.Vector.ToVectorLiteral(Definition.Dimension, nameof(embeddings)),
            })
            .ToArray();
        if (payload.Length == 0)
        {
            return 0;
        }

        // The text comparison drops a vector whose entry changed after the vector was computed.
        string sql =
            $"""
            UPDATE {objects.Table} e
            SET embedding = r.embedding::{objects.VectorType}
            FROM jsonb_to_recordset(@rows::jsonb) AS r(id uuid, text text, embedding text)
            WHERE e.id = r.id AND e.text = r.text
            """;
        return await session.RunAsync(
            async commands =>
            {
                int stored = 0;
                foreach (var batch in payload.Chunk(EmbeddingBatchSize))
                {
                    stored += await commands.ExecuteAsync(sql, cancellationToken, ("rows", JsonSerializer.Serialize(batch))).ConfigureAwait(false);
                }

                return stored;
            },
            cancellationToken).ConfigureAwait(false);
    }

    public Task<int> ReplaceFacetsAsync(Guid ownerId, IReadOnlyCollection<string> facets, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(facets);
        // An entry of the same owner applies the facet rules; its id and text are never stored.
        string[] validated = [.. new SearchEntry(Guid.NewGuid(), Guid.NewGuid(), "facets", facets).Facets.Order(StringComparer.Ordinal)];
        return session.RunAsync(
            commands => commands.ExecuteAsync(
                $"UPDATE {objects.Table} SET facets = @facets WHERE owner_id = @owner",
                cancellationToken,
                ("facets", validated),
                ("owner", ownerId)),
            cancellationToken);
    }

    public Task<int> RemoveOwnersAsync(IReadOnlyCollection<Guid> ownerIds, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(ownerIds);
        Guid[] owners = [.. ownerIds.Distinct()];
        return owners.Length == 0
            ? Task.FromResult(0)
            : session.RunAsync(
                commands => commands.ExecuteAsync(
                    $"DELETE FROM {objects.Table} WHERE owner_id = ANY(@owners)",
                    cancellationToken,
                    ("owners", owners)),
                cancellationToken);
    }

    public Task<IReadOnlyList<SearchHit>> SearchAsync(SearchQuery query, CancellationToken cancellationToken = default)
    {
        SearchStatement statement = StatementFor(query);
        return session.RunAsync(
            async commands =>
            {
                await ApplySettingsAsync(commands, statement, cancellationToken).ConfigureAwait(false);
                return await commands.QueryAsync(statement.Sql, ReadHit, cancellationToken, statement.Parameters).ConfigureAwait(false);
            },
            cancellationToken);
    }

    /// <summary>
    /// The plan PostgreSQL chooses for <paramref name="query"/>, for the tests that prove each leg
    /// can be answered from its index.
    /// </summary>
    internal Task<string> ExplainAsync(SearchQuery query, CancellationToken cancellationToken)
    {
        SearchStatement statement = StatementFor(query);
        return session.RunAsync(
            async commands =>
            {
                await ApplySettingsAsync(commands, statement, cancellationToken).ConfigureAwait(false);
                IReadOnlyList<string> lines = await commands.QueryAsync(
                    "EXPLAIN " + statement.Sql, reader => reader.GetString(0), cancellationToken, statement.Parameters).ConfigureAwait(false);
                return string.Join('\n', lines);
            },
            cancellationToken);
    }

    // SET LOCAL binds to the transaction the session runs in. A filtered HNSW walk needs
    // ef_search >= candidates, or it stops before it has that many.
    private static async Task ApplySettingsAsync(SearchCommands commands, SearchStatement statement, CancellationToken cancellationToken)
    {
        if (statement.EfSearch is { } efSearch)
        {
            await commands.ExecuteAsync(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"SET LOCAL hnsw.ef_search = {efSearch}; SET LOCAL hnsw.iterative_scan = relaxed_order;"),
                cancellationToken).ConfigureAwait(false);
        }
    }

    private static SearchHit ReadHit(DbDataReader reader)
        => new(
            reader.GetGuid(0),
            reader.GetGuid(1),
            reader.GetString(2),
            reader.GetDouble(3),
            reader.IsDBNull(4) ? null : checked((int)reader.GetInt64(4)),
            reader.IsDBNull(5) ? null : checked((int)reader.GetInt64(5)));

    private SearchStatement StatementFor(SearchQuery query)
    {
        ArgumentNullException.ThrowIfNull(query);
        var parameters = new List<(string Name, object Value)>
        {
            ("candidates", query.Parameters.CandidatesPerLeg),
            ("k", query.Parameters.RrfK),
            ("maxResults", query.Parameters.MaxResults),
        };
        string filter = FilterFor(query.Filter, parameters);
        var legs = new List<string>();
        var sql = new StringBuilder("WITH ");

        if (query.Embedding is { } embedding)
        {
            parameters.Add(("embedding", embedding.ToVectorLiteral(Definition.Dimension, nameof(query))));
            sql.Append(
                CultureInfo.InvariantCulture,
                $"""
                vec AS (
                    SELECT id, row_number() OVER (ORDER BY distance, id) AS rank
                    FROM (
                        SELECT e.id, e.embedding <=> @embedding::{objects.VectorType} AS distance
                        FROM {objects.Table} e
                        WHERE e.embedding IS NOT NULL AND {filter}
                        ORDER BY e.embedding <=> @embedding::{objects.VectorType}
                        LIMIT @candidates) nearest
                ),

                """);
            legs.Add("SELECT id, rank, 'vec' AS leg FROM vec");
        }

        if (!string.IsNullOrWhiteSpace(query.Text))
        {
            parameters.Add(("text", query.Text));
            sql.Append(
                CultureInfo.InvariantCulture,
                $"""
                tsq AS (
                    SELECT websearch_to_tsquery('{objects.TextSearchConfig}'::regconfig, @text)
                           || websearch_to_tsquery('simple'::regconfig, @text) AS q
                ),
                lex AS (
                    SELECT e.id, row_number() OVER (ORDER BY ts_rank_cd(e.tsv, (SELECT q FROM tsq)) DESC, e.id) AS rank
                    FROM {objects.Table} e
                    WHERE {filter} AND e.tsv @@ (SELECT q FROM tsq)
                    ORDER BY rank
                    LIMIT @candidates
                ),

                """);
            legs.Add("SELECT id, rank, 'lex' AS leg FROM lex");
        }

        sql.Append(
            CultureInfo.InvariantCulture,
            $"""
            legs AS (
                {string.Join(" UNION ALL ", legs)}
            ),
            fused AS (
                SELECT id,
                       SUM(1.0 / (@k + rank)) AS score,
                       MAX(rank) FILTER (WHERE leg = 'vec') AS vector_rank,
                       MAX(rank) FILTER (WHERE leg = 'lex') AS text_rank
                FROM legs
                GROUP BY id
                ORDER BY score DESC, id
                LIMIT @maxResults
            )
            SELECT e.id, e.owner_id, e.text, f.score::double precision, f.vector_rank, f.text_rank
            FROM fused f
            JOIN {objects.Table} e ON e.id = f.id
            ORDER BY f.score DESC, e.id
            """);

        return new SearchStatement(
            sql.ToString(),
            [.. parameters],
            query.Embedding is null ? null : Math.Max(40, query.Parameters.CandidatesPerLeg));
    }

    // One overlap test per required group and one for the excluded facets, each answered by the
    // GIN index on facets or rechecked inside the leg's own index scan.
    private static string FilterFor(SearchFilter filter, List<(string Name, object Value)> parameters)
    {
        var conditions = new List<string>();
        for (int index = 0; index < filter.Require.Count; index++)
        {
            string name = string.Create(CultureInfo.InvariantCulture, $"require{index}");
            parameters.Add((name, filter.Require[index].Order(StringComparer.Ordinal).ToArray()));
            conditions.Add($"e.facets && @{name}");
        }

        if (filter.Exclude.Count > 0)
        {
            parameters.Add(("exclude", filter.Exclude.Order(StringComparer.Ordinal).ToArray()));
            conditions.Add("NOT (e.facets && @exclude)");
        }

        return conditions.Count == 0 ? "TRUE" : string.Join(" AND ", conditions);
    }

    private sealed record SearchStatement(string Sql, (string Name, object Value)[] Parameters, int? EfSearch);
}
