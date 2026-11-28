namespace Nordstein.Core.Search.Postgres.Internal;

/// <summary>
/// The schema step of the PostgreSQL search. Every index has one row in <c>search_identity</c> and
/// one entries table; both live in the connection's default schema and outside any EF model.
/// </summary>
/// <remarks>
/// <para>
/// Each method runs in one transaction that first takes a transaction-scoped advisory lock, so two
/// hosts that start at once create the objects one after the other instead of colliding.
/// </para>
/// <para>
/// A bound index whose objects all exist runs no DDL: even <c>CREATE INDEX IF NOT EXISTS</c> locks
/// its table against writes, and a host that boots must not stall another that is writing.
/// </para>
/// </remarks>
internal sealed class PostgresSearchSchema : ISearchSchema
{
    // An arbitrary constant shared by every host of this package: 'nsearch' in ASCII.
    private const long SchemaLock = 0x6E_73_65_61_72_63_68;

    private static readonly Version MinimumVectorVersion = new(0, 8, 0);

    private readonly SearchSession session;

    public PostgresSearchSchema(SearchSession session)
    {
        this.session = session;
    }

    public async Task EnsureAsync(SearchIndexDefinition definition, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(definition);
        var objects = new IndexObjects(definition);
        await session.RunAsync(
            async commands =>
            {
                await LockAsync(commands, cancellationToken).ConfigureAwait(false);
                await RequireTextSearchConfigAsync(commands, definition, cancellationToken).ConfigureAwait(false);
                await commands.ExecuteAsync(
                    $"""
                    CREATE EXTENSION IF NOT EXISTS vector;
                    CREATE TABLE IF NOT EXISTS {IndexObjects.IdentityTable} (
                        name varchar(40) NOT NULL,
                        embedding_model varchar({SearchIndexDefinition.MaxEmbeddingModelIdLength}) NOT NULL,
                        dimension integer NOT NULL,
                        text_search_config varchar(63) NOT NULL,
                        created_at timestamptz NOT NULL DEFAULT now(),
                        CONSTRAINT pk_search_identity PRIMARY KEY (name)
                    );
                    """,
                    cancellationToken).ConfigureAwait(false);
                await RequireVectorVersionAsync(commands, cancellationToken).ConfigureAwait(false);

                SearchIndexDefinition? bound = await ReadAsync(commands, definition.Name, cancellationToken).ConfigureAwait(false);
                if (bound is null)
                {
                    await commands.ExecuteAsync(
                        $"""
                        INSERT INTO {IndexObjects.IdentityTable} (name, embedding_model, dimension, text_search_config)
                        VALUES (@name, @model, @dimension, @config)
                        """,
                        cancellationToken,
                        ("name", definition.Name),
                        ("model", definition.EmbeddingModelId),
                        ("dimension", definition.Dimension),
                        ("config", definition.TextSearchConfig)).ConfigureAwait(false);
                }
                else if (bound != definition)
                {
                    throw new SearchSchemaMismatchException(bound, definition);
                }

                if (bound is null || !await ObjectsExistAsync(commands, objects, cancellationToken).ConfigureAwait(false))
                {
                    await commands.ExecuteAsync(CreateObjects(objects), cancellationToken).ConfigureAwait(false);
                }

                return true;
            },
            cancellationToken).ConfigureAwait(false);
    }

    public Task<SearchIndexDefinition?> FindAsync(string name, CancellationToken cancellationToken = default)
    {
        RequireIndexName(name);
        return session.RunAsync(
            async commands => await IdentityTableExistsAsync(commands, cancellationToken).ConfigureAwait(false)
                ? await ReadAsync(commands, name, cancellationToken).ConfigureAwait(false)
                : null,
            cancellationToken);
    }

    public async Task RemoveAsync(string name, CancellationToken cancellationToken = default)
    {
        RequireIndexName(name);
        await session.RunAsync(
            async commands =>
            {
                await LockAsync(commands, cancellationToken).ConfigureAwait(false);
                if (await IdentityTableExistsAsync(commands, cancellationToken).ConfigureAwait(false))
                {
                    await commands.ExecuteAsync(
                        $"DELETE FROM {IndexObjects.IdentityTable} WHERE name = @name",
                        cancellationToken,
                        ("name", name)).ConfigureAwait(false);
                }

                await commands.ExecuteAsync($"DROP TABLE IF EXISTS {IndexObjects.TableOf(name)}", cancellationToken).ConfigureAwait(false);
                return true;
            },
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Whether pgvector <paramref name="version"/> has what the search statement uses: iterative
    /// index scans arrived in 0.8.0.
    /// </summary>
    internal static bool IsSupportedVectorVersion(string version)
        => Version.TryParse(version, out Version? parsed) && parsed >= MinimumVectorVersion;

    private static void RequireIndexName(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        if (!SearchIndexDefinition.IsValidName(name))
        {
            throw new ArgumentException($"'{name}' is not an index name.", nameof(name));
        }
    }

    private static Task<int> LockAsync(SearchCommands commands, CancellationToken cancellationToken)
        => commands.ExecuteAsync("SELECT pg_advisory_xact_lock(@key)", cancellationToken, ("key", SchemaLock));

    private static async Task RequireTextSearchConfigAsync(
        SearchCommands commands,
        SearchIndexDefinition definition,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<bool> known = await commands.QueryAsync(
            "SELECT EXISTS (SELECT 1 FROM pg_ts_config WHERE cfgname = @config)",
            reader => reader.GetBoolean(0),
            cancellationToken,
            ("config", definition.TextSearchConfig)).ConfigureAwait(false);
        if (!known[0])
        {
            throw new ArgumentException(
                $"'{definition.TextSearchConfig}' is not an installed text-search configuration (pg_ts_config).",
                nameof(definition));
        }
    }

    private static async Task RequireVectorVersionAsync(SearchCommands commands, CancellationToken cancellationToken)
    {
        IReadOnlyList<string> versions = await commands.QueryAsync(
            "SELECT extversion FROM pg_extension WHERE extname = 'vector'",
            reader => reader.GetString(0),
            cancellationToken).ConfigureAwait(false);
        string version = versions.Count == 1 ? versions[0] : "none";
        if (!IsSupportedVectorVersion(version))
        {
            throw new InvalidOperationException(
                $"The search index needs the pgvector extension in version {MinimumVectorVersion} or newer; the database has {version}.");
        }
    }

    private static async Task<bool> IdentityTableExistsAsync(SearchCommands commands, CancellationToken cancellationToken)
        => (await commands.QueryAsync(
            $"SELECT to_regclass('{IndexObjects.IdentityTable}') IS NOT NULL",
            reader => reader.GetBoolean(0),
            cancellationToken).ConfigureAwait(false))[0];

    private static async Task<SearchIndexDefinition?> ReadAsync(SearchCommands commands, string name, CancellationToken cancellationToken)
        => (await commands.QueryAsync(
            $"SELECT embedding_model, dimension, text_search_config FROM {IndexObjects.IdentityTable} WHERE name = @name",
            reader => new SearchIndexDefinition(name, reader.GetString(0), reader.GetInt32(1), reader.GetString(2)),
            cancellationToken,
            ("name", name)).ConfigureAwait(false)).SingleOrDefault();

    // A catalog read: it takes no lock on the entries table.
    private static async Task<bool> ObjectsExistAsync(SearchCommands commands, IndexObjects objects, CancellationToken cancellationToken)
    {
        string checks = string.Join(
            " AND ",
            objects.Indexes.Prepend(objects.Table).Select(name => $"to_regclass('{name}') IS NOT NULL"));
        return (await commands.QueryAsync($"SELECT {checks}", reader => reader.GetBoolean(0), cancellationToken).ConfigureAwait(false))[0];
    }

    // The names, the dimension and the configuration are DDL positions a parameter cannot take;
    // SearchIndexDefinition admits only plain identifiers and an integer for them.
    private static string CreateObjects(IndexObjects objects)
        => $"""
            CREATE TABLE IF NOT EXISTS {objects.Table} (
                id uuid NOT NULL,
                owner_id uuid NOT NULL,
                text text NOT NULL,
                facets text[] NOT NULL DEFAULT '{"{}"}',
                embedding {objects.VectorType}({objects.Dimension}) NULL,
                tsv tsvector GENERATED ALWAYS AS (
                    to_tsvector('{objects.TextSearchConfig}'::regconfig, text) || to_tsvector('simple'::regconfig, text)) STORED,
                CONSTRAINT {objects.PrimaryKey} PRIMARY KEY (id)
            );
            CREATE INDEX IF NOT EXISTS {objects.HnswIndex} ON {objects.Table} USING hnsw (embedding {objects.VectorOperatorClass});
            CREATE INDEX IF NOT EXISTS {objects.TextIndex} ON {objects.Table} USING gin (tsv);
            CREATE INDEX IF NOT EXISTS {objects.FacetIndex} ON {objects.Table} USING gin (facets);
            CREATE INDEX IF NOT EXISTS {objects.OwnerIndex} ON {objects.Table} (owner_id);
            CREATE INDEX IF NOT EXISTS {objects.PendingIndex} ON {objects.Table} (id) WHERE embedding IS NULL;
            """;
}
