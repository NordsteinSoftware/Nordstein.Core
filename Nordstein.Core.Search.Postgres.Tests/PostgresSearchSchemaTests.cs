using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Nordstein.Core.Search.Postgres.Internal;
using Nordstein.Core.Search.Postgres.Tests.Internal;
using Nordstein.Core.Testing.Postgres;

namespace Nordstein.Core.Search.Postgres.Tests;

[TestClass]
[TestCategory("Integration")]
public sealed class PostgresSearchSchemaTests : PostgresSearchTestBase
{
    [TestMethod]
    public async Task Ensure_NewIndex_BindsDefinitionAndCreatesObjects()
    {
        await using PostgresDatabase database = await SharedPostgres.CreateDatabaseAsync(CancellationToken);
        ISearchSchema schema = ServicesFor(database).GetRequiredService<ISearchSchema>();

        await schema.EnsureAsync(Notes, CancellationToken);

        (await schema.FindAsync("notes", CancellationToken)).Should().Be(Notes);
        (await ScalarAsync<long>(
            database,
            """
            SELECT count(*) FROM pg_class
            WHERE relname IN ('search_notes_entries', 'ix_search_notes_hnsw', 'ix_search_notes_tsv',
                              'ix_search_notes_facets', 'ix_search_notes_owner', 'ix_search_notes_pending')
            """)).Should().Be(6);
        (await ScalarAsync<string>(
            database,
            "SELECT format_type(atttypid, atttypmod) FROM pg_attribute WHERE attrelid = 'search_notes_entries'::regclass AND attname = 'embedding'"))
            .Should().Be("vector(3)");
    }

    [TestMethod]
    public async Task Ensure_SameDefinitionTwice_ChangesNothing()
    {
        await using PostgresDatabase database = await SharedPostgres.CreateDatabaseAsync(CancellationToken);
        IServiceProvider services = ServicesFor(database);
        ISearchIndex index = await EnsureIndexAsync(services, Notes);
        await index.UpsertAsync([Entry(1, "kept across a second boot")], CancellationToken);

        await services.GetRequiredService<ISearchSchema>().EnsureAsync(Notes, CancellationToken);

        (await ScalarAsync<long>(database, "SELECT count(*) FROM search_identity")).Should().Be(1);
        (await ScalarAsync<long>(database, "SELECT count(*) FROM search_notes_entries")).Should().Be(1);
    }

    [TestMethod]
    [DataRow("other-model", 3, "english")]
    [DataRow("example-embed-1", 4, "english")]
    [DataRow("example-embed-1", 3, "simple")]
    public async Task Ensure_DefinitionDiffersFromBound_ThrowsAndKeepsBound(string model, int dimension, string config)
    {
        await using PostgresDatabase database = await SharedPostgres.CreateDatabaseAsync(CancellationToken);
        ISearchSchema schema = ServicesFor(database).GetRequiredService<ISearchSchema>();
        await schema.EnsureAsync(Notes, CancellationToken);
        var requested = new SearchIndexDefinition("notes", model, dimension, config);

        Func<Task> act = () => schema.EnsureAsync(requested, CancellationToken);

        SearchSchemaMismatchException exception = (await act.Should().ThrowAsync<SearchSchemaMismatchException>()).Which;
        exception.Bound.Should().Be(Notes);
        exception.Requested.Should().Be(requested);
        (await schema.FindAsync("notes", CancellationToken)).Should().Be(Notes);
    }

    [TestMethod]
    public async Task Ensure_UnknownTextSearchConfig_ThrowsAndCreatesNothing()
    {
        await using PostgresDatabase database = await SharedPostgres.CreateDatabaseAsync(CancellationToken);
        ISearchSchema schema = ServicesFor(database).GetRequiredService<ISearchSchema>();

        Func<Task> act = () => schema.EnsureAsync(new SearchIndexDefinition("notes", "m", 3, "klingon"), CancellationToken);

        (await act.Should().ThrowAsync<ArgumentException>()).Which.Message.Should().Contain("klingon");
        (await ScalarAsync<bool>(database, "SELECT to_regclass('search_identity') IS NULL")).Should().BeTrue();
    }

    [TestMethod]
    public async Task Ensure_TwoIndexes_KeepSeparateEntries()
    {
        await using PostgresDatabase database = await SharedPostgres.CreateDatabaseAsync(CancellationToken);
        IServiceProvider services = ServicesFor(database);
        var memos = new SearchIndexDefinition("memos", "other-model", 5, "simple");
        ISearchIndex notes = await EnsureIndexAsync(services, Notes);
        ISearchIndex other = await EnsureIndexAsync(services, memos);

        await notes.UpsertAsync([Entry(1, "apple")], CancellationToken);
        await other.UpsertAsync([Entry(2, "apple")], CancellationToken);

        (await notes.SearchAsync(TextQuery("apple"), CancellationToken)).Select(hit => hit.EntryId).Should().Equal(EntryId(1));
        (await other.SearchAsync(TextQuery("apple"), CancellationToken)).Select(hit => hit.EntryId).Should().Equal(EntryId(2));
        (await services.GetRequiredService<ISearchSchema>().FindAsync("memos", CancellationToken)).Should().Be(memos);
    }

    [TestMethod]
    public async Task Ensure_ConcurrentCallers_AllSucceed()
    {
        await using PostgresDatabase database = await SharedPostgres.CreateDatabaseAsync(CancellationToken);
        IServiceProvider[] hosts = [.. Enumerable.Range(0, 4).Select(_ => ServicesFor(database))];

        await Task.WhenAll(hosts.Select(host => host.GetRequiredService<ISearchSchema>().EnsureAsync(Notes, CancellationToken)));

        (await ScalarAsync<long>(database, "SELECT count(*) FROM search_identity WHERE name = 'notes'")).Should().Be(1);
    }

    [TestMethod]
    public async Task Ensure_BoundIndexWithMissingObject_RecreatesIt()
    {
        await using PostgresDatabase database = await SharedPostgres.CreateDatabaseAsync(CancellationToken);
        ISearchSchema schema = ServicesFor(database).GetRequiredService<ISearchSchema>();
        await schema.EnsureAsync(Notes, CancellationToken);
        await ExecuteAsync(database, "DROP INDEX ix_search_notes_facets");

        await schema.EnsureAsync(Notes, CancellationToken);

        (await ScalarAsync<bool>(database, "SELECT to_regclass('ix_search_notes_facets') IS NOT NULL")).Should().BeTrue();
    }

    [TestMethod]
    public async Task Ensure_DimensionAboveFullPrecisionLimit_StoresHalfPrecision()
    {
        await using PostgresDatabase database = await SharedPostgres.CreateDatabaseAsync(CancellationToken);
        var wide = new SearchIndexDefinition("wide", "example-embed-large", 2001, "english");
        ISearchIndex index = await EnsureIndexAsync(ServicesFor(database), wide);
        float[] vector = new float[2001];
        vector[0] = 1f;
        await index.UpsertAsync([Entry(1, "a wide vector")], CancellationToken);

        int stored = await index.SetEmbeddingsAsync([new SearchEmbedding(EntryId(1), "a wide vector", vector)], CancellationToken);
        IReadOnlyList<SearchHit> hits = await index.SearchAsync(
            new SearchQuery(string.Empty, vector, SearchFilter.None, SearchParameters.Default), CancellationToken);

        stored.Should().Be(1);
        hits.Should().ContainSingle().Which.VectorRank.Should().Be(1);
        (await ScalarAsync<string>(
            database,
            "SELECT format_type(atttypid, atttypmod) FROM pg_attribute WHERE attrelid = 'search_wide_entries'::regclass AND attname = 'embedding'"))
            .Should().Be("halfvec(2001)");
    }

    [TestMethod]
    public async Task Ensure_NullDefinition_Throws()
    {
        await using PostgresDatabase database = await SharedPostgres.CreateDatabaseAsync(CancellationToken);
        ISearchSchema schema = ServicesFor(database).GetRequiredService<ISearchSchema>();

        Func<Task> act = () => schema.EnsureAsync(Missing.Reference<SearchIndexDefinition>(), CancellationToken);

        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [TestMethod]
    public async Task Ensure_CancelledToken_ThrowsAndCreatesNothing()
    {
        await using PostgresDatabase database = await SharedPostgres.CreateDatabaseAsync(CancellationToken);
        ISearchSchema schema = ServicesFor(database).GetRequiredService<ISearchSchema>();
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        Func<Task> act = () => schema.EnsureAsync(Notes, cancelled.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        (await ScalarAsync<bool>(database, "SELECT to_regclass('search_identity') IS NULL")).Should().BeTrue();
    }

    [TestMethod]
    public async Task Find_NoIndexEver_ReturnsNull()
    {
        await using PostgresDatabase database = await SharedPostgres.CreateDatabaseAsync(CancellationToken);
        ISearchSchema schema = ServicesFor(database).GetRequiredService<ISearchSchema>();

        (await schema.FindAsync("notes", CancellationToken)).Should().BeNull();
    }

    [TestMethod]
    public async Task Find_OtherIndexExists_ReturnsNull()
    {
        await using PostgresDatabase database = await SharedPostgres.CreateDatabaseAsync(CancellationToken);
        ISearchSchema schema = ServicesFor(database).GetRequiredService<ISearchSchema>();
        await schema.EnsureAsync(Notes, CancellationToken);

        (await schema.FindAsync("memos", CancellationToken)).Should().BeNull();
    }

    [TestMethod]
    public async Task FindAndRemove_InvalidOrNullName_Throw()
    {
        await using PostgresDatabase database = await SharedPostgres.CreateDatabaseAsync(CancellationToken);
        ISearchSchema schema = ServicesFor(database).GetRequiredService<ISearchSchema>();

        await FluentActions.Awaiting(() => schema.FindAsync("No Name", CancellationToken)).Should().ThrowAsync<ArgumentException>();
        await FluentActions.Awaiting(() => schema.RemoveAsync("x; DROP TABLE y", CancellationToken)).Should().ThrowAsync<ArgumentException>();
        await FluentActions.Awaiting(() => schema.FindAsync(Missing.Reference<string>(), CancellationToken)).Should().ThrowAsync<ArgumentNullException>();
        await FluentActions.Awaiting(() => schema.RemoveAsync(Missing.Reference<string>(), CancellationToken)).Should().ThrowAsync<ArgumentNullException>();
    }

    [TestMethod]
    public async Task Remove_ExistingIndex_DropsItAndAllowsAnotherModel()
    {
        await using PostgresDatabase database = await SharedPostgres.CreateDatabaseAsync(CancellationToken);
        ISearchSchema schema = ServicesFor(database).GetRequiredService<ISearchSchema>();
        await schema.EnsureAsync(Notes, CancellationToken);
        var rebuilt = new SearchIndexDefinition("notes", "newer-model", 6, "simple");

        await schema.RemoveAsync("notes", CancellationToken);

        (await schema.FindAsync("notes", CancellationToken)).Should().BeNull();
        (await ScalarAsync<bool>(database, "SELECT to_regclass('search_notes_entries') IS NULL")).Should().BeTrue();
        await schema.EnsureAsync(rebuilt, CancellationToken);
        (await schema.FindAsync("notes", CancellationToken)).Should().Be(rebuilt);
    }

    [TestMethod]
    public async Task Remove_UnknownIndex_IsNoOp()
    {
        await using PostgresDatabase database = await SharedPostgres.CreateDatabaseAsync(CancellationToken);
        ISearchSchema schema = ServicesFor(database).GetRequiredService<ISearchSchema>();

        await schema.RemoveAsync("notes", CancellationToken);
        await schema.EnsureAsync(Notes, CancellationToken);
        await schema.RemoveAsync("memos", CancellationToken);

        (await schema.FindAsync("notes", CancellationToken)).Should().Be(Notes);
    }

    [TestMethod]
    [DataRow("0.8.0", true)]
    [DataRow("0.8.2", true)]
    [DataRow("1.0.0", true)]
    [DataRow("0.7.4", false)]
    [DataRow("none", false)]
    [DataRow("", false)]
    public void IsSupportedVectorVersion_Version_NeedsIterativeScans(string version, bool expected)
        => PostgresSearchSchema.IsSupportedVectorVersion(version).Should().Be(expected);
}
