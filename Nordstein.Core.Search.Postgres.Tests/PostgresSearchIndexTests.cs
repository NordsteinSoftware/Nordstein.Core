using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Nordstein.Core.Domain;
using Nordstein.Core.Search.Postgres.Tests.Internal;
using Nordstein.Core.Testing.Postgres;

namespace Nordstein.Core.Search.Postgres.Tests;

[TestClass]
[TestCategory("Integration")]
public sealed class PostgresSearchIndexTests : PostgresSearchTestBase
{
    [TestMethod]
    public async Task Open_Definition_IsExposed()
    {
        await using PostgresDatabase database = await SharedPostgres.CreateDatabaseAsync(CancellationToken);

        ISearchIndex index = await EnsureIndexAsync(ServicesFor(database), Notes);

        index.Definition.Should().Be(Notes);
    }

    [TestMethod]
    public async Task Open_NullDefinition_Throws()
    {
        await using PostgresDatabase database = await SharedPostgres.CreateDatabaseAsync(CancellationToken);
        ISearchIndex.Open open = ServicesFor(database).GetRequiredService<ISearchIndex.Open>();

        Action act = () => open(Missing.Reference<SearchIndexDefinition>());

        act.Should().Throw<ArgumentNullException>();
    }

    [TestMethod]
    public async Task Upsert_NewEntry_IsFoundByKeywordBeforeItHasAVector()
    {
        await using PostgresDatabase database = await SharedPostgres.CreateDatabaseAsync(CancellationToken);
        ISearchIndex index = await EnsureIndexAsync(ServicesFor(database), Notes);

        await index.UpsertAsync([Entry(1, "The migration locked the table for minutes.", "kind:note")], CancellationToken);

        SearchHit hit = (await index.SearchAsync(TextQuery("migrations locking"), CancellationToken)).Should().ContainSingle().Which;
        hit.EntryId.Should().Be(EntryId(1));
        hit.OwnerId.Should().Be(OwnerId(1));
        hit.Text.Should().Be("The migration locked the table for minutes.");
        hit.TextRank.Should().Be(1);
        hit.VectorRank.Should().BeNull();
        hit.Score.Should().BeApproximately(1.0 / 61, 1e-12);
    }

    [TestMethod]
    public async Task Upsert_Empty_IsNoOp()
    {
        await using PostgresDatabase database = await SharedPostgres.CreateDatabaseAsync(CancellationToken);
        ISearchIndex index = await EnsureIndexAsync(ServicesFor(database), Notes);

        await index.UpsertAsync([], CancellationToken);

        (await ScalarAsync<long>(database, "SELECT count(*) FROM search_notes_entries")).Should().Be(0);
    }

    [TestMethod]
    public async Task Upsert_NullCollectionNullEntryOrDuplicateIds_Throw()
    {
        await using PostgresDatabase database = await SharedPostgres.CreateDatabaseAsync(CancellationToken);
        ISearchIndex index = await EnsureIndexAsync(ServicesFor(database), Notes);

        await FluentActions.Awaiting(() => index.UpsertAsync(Missing.Reference<IReadOnlyCollection<SearchEntry>>(), CancellationToken))
            .Should().ThrowAsync<ArgumentNullException>();
        await FluentActions.Awaiting(() => index.UpsertAsync([Missing.Reference<SearchEntry>()], CancellationToken))
            .Should().ThrowAsync<ArgumentNullException>();
        await FluentActions.Awaiting(() => index.UpsertAsync([Entry(1, "a"), Entry(1, "b")], CancellationToken))
            .Should().ThrowAsync<ArgumentException>();
        (await ScalarAsync<long>(database, "SELECT count(*) FROM search_notes_entries")).Should().Be(0);
    }

    [TestMethod]
    public async Task Upsert_ExistingIdWithSameText_KeepsVectorAndReplacesFacetsAndOwner()
    {
        await using PostgresDatabase database = await SharedPostgres.CreateDatabaseAsync(CancellationToken);
        ISearchIndex index = await EnsureIndexAsync(ServicesFor(database), Notes);
        await index.UpsertAsync([Entry(1, "stable text", "state:open")], CancellationToken);
        await index.SetEmbeddingsAsync([new SearchEmbedding(EntryId(1), "stable text", Vector(1, 0, 0))], CancellationToken);

        await index.UpsertAsync([new SearchEntry(EntryId(1), OwnerId(9), "stable text", ["state:closed"])], CancellationToken);

        (await index.CountWithoutEmbeddingAsync(CancellationToken)).Should().Be(0);
        (await ScalarAsync<string>(database, "SELECT array_to_string(facets, ',') FROM search_notes_entries")).Should().Be("state:closed");
        (await ScalarAsync<Guid>(database, "SELECT owner_id FROM search_notes_entries")).Should().Be(OwnerId(9));
    }

    [TestMethod]
    public async Task Upsert_ExistingIdWithChangedText_DropsVector()
    {
        await using PostgresDatabase database = await SharedPostgres.CreateDatabaseAsync(CancellationToken);
        ISearchIndex index = await EnsureIndexAsync(ServicesFor(database), Notes);
        await index.UpsertAsync([Entry(1, "first text")], CancellationToken);
        await index.SetEmbeddingsAsync([new SearchEmbedding(EntryId(1), "first text", Vector(1, 0, 0))], CancellationToken);

        await index.UpsertAsync([Entry(1, "second text")], CancellationToken);

        (await index.ListWithoutEmbeddingAsync(10, null, CancellationToken)).Should().ContainSingle().Which.Text.Should().Be("second text");
    }

    [TestMethod]
    public async Task Upsert_MoreThanOneBatch_StoresEveryEntryWithItsText()
    {
        await using PostgresDatabase database = await SharedPostgres.CreateDatabaseAsync(CancellationToken);
        ISearchIndex index = await EnsureIndexAsync(ServicesFor(database), Notes);
        string awkward = "quotes ' \" backslash \\ braces {} percent % emoji \U0001F600 newline\nend";
        SearchEntry[] entries = [.. Enumerable.Range(1, 450).Select(number => Entry(number, number == 450 ? awkward : $"entry {number}", "bulk:yes", "a,b"))];

        await index.UpsertAsync(entries, CancellationToken);

        (await ScalarAsync<long>(database, "SELECT count(*) FROM search_notes_entries")).Should().Be(450);
        IReadOnlyList<SearchEntry> listed = await index.ListWithoutEmbeddingAsync(1000, null, CancellationToken);
        listed.Single(entry => entry.Id == EntryId(450)).Text.Should().Be(awkward);
        listed[0].Facets.Should().BeEquivalentTo(["bulk:yes", "a,b"]);
    }

    [TestMethod]
    public async Task ListWithoutEmbedding_SomeEmbedded_ReturnsTheRestInIdOrderUpToLimit()
    {
        await using PostgresDatabase database = await SharedPostgres.CreateDatabaseAsync(CancellationToken);
        ISearchIndex index = await EnsureIndexAsync(ServicesFor(database), Notes);
        await index.UpsertAsync([Entry(3, "c"), Entry(1, "a"), Entry(2, "b"), Entry(4, "d")], CancellationToken);
        await index.SetEmbeddingsAsync([new SearchEmbedding(EntryId(2), "b", Vector(0, 1, 0))], CancellationToken);

        IReadOnlyList<SearchEntry> pending = await index.ListWithoutEmbeddingAsync(2, null, CancellationToken);

        pending.Select(entry => entry.Id).Should().Equal(EntryId(1), EntryId(3));
        pending[0].OwnerId.Should().Be(OwnerId(1));
        (await index.CountWithoutEmbeddingAsync(CancellationToken)).Should().Be(3);
    }

    [TestMethod]
    public async Task ListWithoutEmbedding_AfterId_ContinuesBehindItSoARefusedEntryCanBeSkipped()
    {
        await using PostgresDatabase database = await SharedPostgres.CreateDatabaseAsync(CancellationToken);
        ISearchIndex index = await EnsureIndexAsync(ServicesFor(database), Notes);
        await index.UpsertAsync([Entry(1, "a"), Entry(2, "b"), Entry(3, "c")], CancellationToken);

        IReadOnlyList<SearchEntry> first = await index.ListWithoutEmbeddingAsync(2, null, CancellationToken);
        IReadOnlyList<SearchEntry> second = await index.ListWithoutEmbeddingAsync(2, first[^1].Id, CancellationToken);
        IReadOnlyList<SearchEntry> third = await index.ListWithoutEmbeddingAsync(2, second[^1].Id, CancellationToken);

        first.Select(entry => entry.Id).Should().Equal(EntryId(1), EntryId(2));
        second.Select(entry => entry.Id).Should().Equal(EntryId(3));
        third.Should().BeEmpty();
    }

    [TestMethod]
    [DataRow(0)]
    [DataRow(1001)]
    public async Task ListWithoutEmbedding_LimitOutOfRange_Throws(int limit)
    {
        await using PostgresDatabase database = await SharedPostgres.CreateDatabaseAsync(CancellationToken);
        ISearchIndex index = await EnsureIndexAsync(ServicesFor(database), Notes);

        Func<Task> act = () => index.ListWithoutEmbeddingAsync(limit, null, CancellationToken);

        await act.Should().ThrowAsync<ArgumentOutOfRangeException>();
    }

    [TestMethod]
    public async Task SetEmbeddings_TextChangedOrEntryGone_SkipsThoseOnly()
    {
        await using PostgresDatabase database = await SharedPostgres.CreateDatabaseAsync(CancellationToken);
        ISearchIndex index = await EnsureIndexAsync(ServicesFor(database), Notes);
        await index.UpsertAsync([Entry(1, "current"), Entry(2, "edited since")], CancellationToken);

        int stored = await index.SetEmbeddingsAsync(
            [
                new SearchEmbedding(EntryId(1), "current", Vector(1, 0, 0)),
                new SearchEmbedding(EntryId(2), "the text before the edit", Vector(0, 1, 0)),
                new SearchEmbedding(EntryId(3), "never existed", Vector(0, 0, 1)),
            ],
            CancellationToken);

        stored.Should().Be(1);
        (await index.ListWithoutEmbeddingAsync(10, null, CancellationToken)).Select(entry => entry.Id).Should().Equal(EntryId(2));
    }

    [TestMethod]
    public async Task SetEmbeddings_MoreThanOneBatch_StoresAll()
    {
        await using PostgresDatabase database = await SharedPostgres.CreateDatabaseAsync(CancellationToken);
        ISearchIndex index = await EnsureIndexAsync(ServicesFor(database), Notes);
        await index.UpsertAsync([.. Enumerable.Range(1, 120).Select(number => Entry(number, $"entry {number}"))], CancellationToken);

        int stored = await index.SetEmbeddingsAsync(
            [.. Enumerable.Range(1, 120).Select(number => new SearchEmbedding(EntryId(number), $"entry {number}", Vector(number, 1, 0)))],
            CancellationToken);

        stored.Should().Be(120);
        (await index.CountWithoutEmbeddingAsync(CancellationToken)).Should().Be(0);
    }

    [TestMethod]
    public async Task SetEmbeddings_OneBadVector_ThrowsAndStoresNothing()
    {
        await using PostgresDatabase database = await SharedPostgres.CreateDatabaseAsync(CancellationToken);
        ISearchIndex index = await EnsureIndexAsync(ServicesFor(database), Notes);
        await index.UpsertAsync([Entry(1, "a"), Entry(2, "b")], CancellationToken);
        var good = new SearchEmbedding(EntryId(1), "a", Vector(1, 0, 0));

        await FluentActions.Awaiting(() => index.SetEmbeddingsAsync([good, new SearchEmbedding(EntryId(2), "b", Vector(1, 0))], CancellationToken))
            .Should().ThrowAsync<ArgumentException>().WithMessage("*2 dimensions*3*");
        await FluentActions.Awaiting(() => index.SetEmbeddingsAsync([good, new SearchEmbedding(EntryId(2), "b", Vector(1, float.NaN, 0))], CancellationToken))
            .Should().ThrowAsync<ArgumentException>().WithMessage("*component 1*");
        await FluentActions.Awaiting(() => index.SetEmbeddingsAsync([good, new SearchEmbedding(EntryId(2), "b", Vector(float.PositiveInfinity, 0, 0))], CancellationToken))
            .Should().ThrowAsync<ArgumentException>();
        (await index.CountWithoutEmbeddingAsync(CancellationToken)).Should().Be(2);
    }

    [TestMethod]
    public async Task SetEmbeddings_EmptyNullOrDuplicate_ReturnsZeroOrThrows()
    {
        await using PostgresDatabase database = await SharedPostgres.CreateDatabaseAsync(CancellationToken);
        ISearchIndex index = await EnsureIndexAsync(ServicesFor(database), Notes);
        var embedding = new SearchEmbedding(EntryId(1), "a", Vector(1, 0, 0));

        (await index.SetEmbeddingsAsync([], CancellationToken)).Should().Be(0);
        await FluentActions.Awaiting(() => index.SetEmbeddingsAsync(Missing.Reference<IReadOnlyCollection<SearchEmbedding>>(), CancellationToken))
            .Should().ThrowAsync<ArgumentNullException>();
        await FluentActions.Awaiting(() => index.SetEmbeddingsAsync([Missing.Reference<SearchEmbedding>()], CancellationToken))
            .Should().ThrowAsync<ArgumentNullException>();
        await FluentActions.Awaiting(() => index.SetEmbeddingsAsync([embedding, embedding], CancellationToken))
            .Should().ThrowAsync<ArgumentException>();
    }

    [TestMethod]
    public async Task Search_VectorOnly_RanksByCosineDistance()
    {
        await using PostgresDatabase database = await SharedPostgres.CreateDatabaseAsync(CancellationToken);
        ISearchIndex index = await EnsureIndexAsync(ServicesFor(database), Notes);
        await SeedAsync(index, (1, "alpha", Vector(1, 0, 0)), (2, "beta", Vector(0.8f, 0.6f, 0)), (3, "gamma", Vector(0, 0, 1)));

        IReadOnlyList<SearchHit> hits = await index.SearchAsync(
            new SearchQuery(" ", Vector(1, 0.1f, 0), SearchFilter.None, SearchParameters.Default), CancellationToken);

        hits.Select(hit => hit.EntryId).Should().Equal(EntryId(1), EntryId(2), EntryId(3));
        hits.Select(hit => hit.VectorRank).Should().Equal(1, 2, 3);
        hits.Should().OnlyContain(hit => hit.TextRank == null);
    }

    [TestMethod]
    public async Task Search_BothLegs_FusesReciprocalRanks()
    {
        await using PostgresDatabase database = await SharedPostgres.CreateDatabaseAsync(CancellationToken);
        ISearchIndex index = await EnsureIndexAsync(ServicesFor(database), Notes);
        await SeedAsync(
            index,
            (1, "pgvector index build is slow", Vector(0, 1, 0)),
            (2, "unrelated words entirely", Vector(1, 0, 0)),
            (3, "pgvector pgvector pgvector index index", Vector(0.9f, 0.1f, 0)));

        IReadOnlyList<SearchHit> hits = await index.SearchAsync(
            new SearchQuery("pgvector index", Vector(1, 0, 0), SearchFilter.None, new SearchParameters(100, 60, 10)), CancellationToken);

        // Vector ranks: 2, 3, 1. Keyword ranks: 3, 1 (entry 2 does not match). Found by both legs
        // beats first place in one.
        hits.Select(hit => hit.EntryId).Should().Equal(EntryId(3), EntryId(1), EntryId(2));
        hits[0].Should().Match<SearchHit>(hit => hit.VectorRank == 2 && hit.TextRank == 1);
        hits[0].Score.Should().BeApproximately((1.0 / 62) + (1.0 / 61), 1e-12);
        hits[1].Should().Match<SearchHit>(hit => hit.VectorRank == 3 && hit.TextRank == 2);
        hits[1].Score.Should().BeApproximately((1.0 / 63) + (1.0 / 62), 1e-12);
        hits[2].Should().Match<SearchHit>(hit => hit.VectorRank == 1 && hit.TextRank == null);
        hits[2].Score.Should().BeApproximately(1.0 / 61, 1e-12);
    }

    [TestMethod]
    public async Task Search_EqualScores_BreakTiesOnEntryId()
    {
        await using PostgresDatabase database = await SharedPostgres.CreateDatabaseAsync(CancellationToken);
        ISearchIndex index = await EnsureIndexAsync(ServicesFor(database), Notes);
        await index.UpsertAsync([Entry(2, "apple"), Entry(1, "apple"), Entry(3, "apple")], CancellationToken);

        IReadOnlyList<SearchHit> hits = await index.SearchAsync(TextQuery("apple"), CancellationToken);

        hits.Select(hit => hit.EntryId).Should().Equal(EntryId(1), EntryId(2), EntryId(3));
    }

    [TestMethod]
    public async Task Search_MaxResultsAndCandidates_LimitTheHits()
    {
        await using PostgresDatabase database = await SharedPostgres.CreateDatabaseAsync(CancellationToken);
        ISearchIndex index = await EnsureIndexAsync(ServicesFor(database), Notes);
        await index.UpsertAsync([.. Enumerable.Range(1, 9).Select(number => Entry(number, "apple"))], CancellationToken);

        IReadOnlyList<SearchHit> capped = await index.SearchAsync(
            new SearchQuery("apple", null, SearchFilter.None, new SearchParameters(100, 60, 4)), CancellationToken);
        IReadOnlyList<SearchHit> narrow = await index.SearchAsync(
            new SearchQuery("apple", null, SearchFilter.None, new SearchParameters(2, 60, 10)), CancellationToken);

        capped.Should().HaveCount(4);
        narrow.Should().HaveCount(2);
    }

    [TestMethod]
    public async Task Search_RequireGroupsAndExclude_FilterBothLegs()
    {
        await using PostgresDatabase database = await SharedPostgres.CreateDatabaseAsync(CancellationToken);
        ISearchIndex index = await EnsureIndexAsync(ServicesFor(database), Notes);
        await index.UpsertAsync(
            [
                Entry(1, "apple", "kind:note", "state:open"),
                Entry(2, "apple", "kind:memo", "state:open"),
                Entry(3, "apple", "kind:note", "state:closed"),
                Entry(4, "apple", "kind:draft", "state:open"),
                Entry(5, "apple", "kind:note", "state:open", "hidden"),
            ],
            CancellationToken);
        await index.SetEmbeddingsAsync(
            [.. Enumerable.Range(1, 5).Select(number => new SearchEmbedding(EntryId(number), "apple", Vector(1, number, 0)))], CancellationToken);
        var filter = new SearchFilter([["kind:note", "kind:memo"], ["state:open"]], ["hidden"]);

        IReadOnlyList<SearchHit> byText = await index.SearchAsync(TextQuery("apple", filter), CancellationToken);
        IReadOnlyList<SearchHit> byVector = await index.SearchAsync(
            new SearchQuery(string.Empty, Vector(1, 1, 0), filter, SearchParameters.Default), CancellationToken);

        byText.Select(hit => hit.EntryId).Should().BeEquivalentTo([EntryId(1), EntryId(2)]);
        byVector.Select(hit => hit.EntryId).Should().Equal(EntryId(1), EntryId(2));
    }

    [TestMethod]
    public async Task Search_NarrowFilterOnVectorLeg_StillFillsTheCandidates()
    {
        await using PostgresDatabase database = await SharedPostgres.CreateDatabaseAsync(CancellationToken);
        ISearchIndex index = await EnsureIndexAsync(ServicesFor(database), Notes);
        SearchEntry[] entries = [.. Enumerable.Range(1, 600).Select(number => Entry(number, $"entry {number}", number % 100 == 0 ? "rare" : "common"))];
        await index.UpsertAsync(entries, CancellationToken);
        await index.SetEmbeddingsAsync(
            [.. entries.Select((entry, position) => new SearchEmbedding(entry.Id, entry.Text, Vector(1, position / 600f, 0)))], CancellationToken);

        IReadOnlyList<SearchHit> hits = await index.SearchAsync(
            new SearchQuery(string.Empty, Vector(1, 0, 0), new SearchFilter([["rare"]], []), new SearchParameters(6, 60, 6)), CancellationToken);

        hits.Select(hit => hit.EntryId).Should().Equal(Enumerable.Range(1, 6).Select(number => EntryId(number * 100)));
    }

    [TestMethod]
    public async Task Search_NoMatchOrHostileText_ReturnsEmptyAndHarmsNothing()
    {
        await using PostgresDatabase database = await SharedPostgres.CreateDatabaseAsync(CancellationToken);
        ISearchIndex index = await EnsureIndexAsync(ServicesFor(database), Notes);
        await index.UpsertAsync([Entry(1, "apple")], CancellationToken);

        (await index.SearchAsync(TextQuery("zebra"), CancellationToken)).Should().BeEmpty();
        (await index.SearchAsync(TextQuery("'; DROP TABLE search_notes_entries; --"), CancellationToken)).Should().BeEmpty();
        (await index.SearchAsync(TextQuery("\"unterminated or -"), CancellationToken)).Should().BeEmpty();
        (await ScalarAsync<long>(database, "SELECT count(*) FROM search_notes_entries")).Should().Be(1);
    }

    [TestMethod]
    public async Task Search_BadEmbeddingOrNullQuery_Throws()
    {
        await using PostgresDatabase database = await SharedPostgres.CreateDatabaseAsync(CancellationToken);
        ISearchIndex index = await EnsureIndexAsync(ServicesFor(database), Notes);

        await FluentActions.Awaiting(() => index.SearchAsync(new SearchQuery("a", Vector(1, 0), SearchFilter.None, SearchParameters.Default), CancellationToken))
            .Should().ThrowAsync<ArgumentException>();
        await FluentActions.Awaiting(() => index.SearchAsync(new SearchQuery("a", Vector(1, 0, float.NegativeInfinity), SearchFilter.None, SearchParameters.Default), CancellationToken))
            .Should().ThrowAsync<ArgumentException>();
        await FluentActions.Awaiting(() => index.SearchAsync(Missing.Reference<SearchQuery>(), CancellationToken))
            .Should().ThrowAsync<ArgumentNullException>();
    }

    [TestMethod]
    public async Task Search_ForcedOffSequentialScans_AnswersEachLegFromItsIndex()
    {
        await using PostgresDatabase database = await SharedPostgres.CreateDatabaseAsync(CancellationToken);
        IServiceProvider services = ServicesFor(database);
        var index = (Postgres.Internal.PostgresSearchIndex)await EnsureIndexAsync(services, Notes);
        await SeedAsync(index, (1, "apple", Vector(1, 0, 0)), (2, "pear", Vector(0, 1, 0)));
        var unfiltered = new SearchQuery("apple", Vector(1, 0, 0), SearchFilter.None, SearchParameters.Default);
        var filtered = new SearchQuery("apple", Vector(1, 0, 0), new SearchFilter([["kind:note"]], ["hidden"]), SearchParameters.Default);

        // The planner prefers a sequential scan on two rows. With that choice taken away, a plan
        // without one proves the operators of each leg match an index.
        (string Unfiltered, string Filtered) plans = await services.GetRequiredService<ITransaction>().InvokeAsync(
            async () =>
            {
                await services.GetRequiredService<Func<DbContext>>()().Database
                    .ExecuteSqlRawAsync("SET LOCAL enable_seqscan = off", CancellationToken);
                return (await index.ExplainAsync(unfiltered, CancellationToken), await index.ExplainAsync(filtered, CancellationToken));
            },
            CancellationToken);

        plans.Unfiltered.Should().Contain("ix_search_notes_hnsw").And.Contain("ix_search_notes_tsv");
        plans.Unfiltered.Should().NotContain("Seq Scan on search_notes_entries");
        plans.Filtered.Should().NotContain("Seq Scan on search_notes_entries");
    }

    [TestMethod]
    public async Task ReplaceFacets_Owner_ChangesEveryEntryOfThatOwnerOnly()
    {
        await using PostgresDatabase database = await SharedPostgres.CreateDatabaseAsync(CancellationToken);
        ISearchIndex index = await EnsureIndexAsync(ServicesFor(database), Notes);
        await index.UpsertAsync(
            [
                new SearchEntry(EntryId(1), OwnerId(1), "apple one", ["state:open"]),
                new SearchEntry(EntryId(2), OwnerId(1), "apple two", ["state:open"]),
                new SearchEntry(EntryId(3), OwnerId(2), "apple three", ["state:open"]),
            ],
            CancellationToken);
        await index.SetEmbeddingsAsync([new SearchEmbedding(EntryId(1), "apple one", Vector(1, 0, 0))], CancellationToken);

        int changed = await index.ReplaceFacetsAsync(OwnerId(1), ["state:closed", "state:closed"], CancellationToken);

        changed.Should().Be(2);
        (await index.SearchAsync(TextQuery("apple", new SearchFilter([["state:open"]], [])), CancellationToken))
            .Select(hit => hit.EntryId).Should().Equal(EntryId(3));
        (await index.CountWithoutEmbeddingAsync(CancellationToken)).Should().Be(2, "replacing facets keeps the vector");
        (await index.ReplaceFacetsAsync(OwnerId(7), [], CancellationToken)).Should().Be(0);
    }

    [TestMethod]
    public async Task ReplaceFacets_EmptyFacets_ClearsThem()
    {
        await using PostgresDatabase database = await SharedPostgres.CreateDatabaseAsync(CancellationToken);
        ISearchIndex index = await EnsureIndexAsync(ServicesFor(database), Notes);
        await index.UpsertAsync([Entry(1, "apple", "state:open")], CancellationToken);

        await index.ReplaceFacetsAsync(OwnerId(1), [], CancellationToken);

        (await ScalarAsync<int>(database, "SELECT cardinality(facets) FROM search_notes_entries")).Should().Be(0);
    }

    [TestMethod]
    public async Task ReplaceFacets_InvalidFacetOrNull_Throws()
    {
        await using PostgresDatabase database = await SharedPostgres.CreateDatabaseAsync(CancellationToken);
        ISearchIndex index = await EnsureIndexAsync(ServicesFor(database), Notes);

        await FluentActions.Awaiting(() => index.ReplaceFacetsAsync(OwnerId(1), [""], CancellationToken)).Should().ThrowAsync<ArgumentException>();
        await FluentActions.Awaiting(() => index.ReplaceFacetsAsync(OwnerId(1), Missing.Reference<IReadOnlyCollection<string>>(), CancellationToken))
            .Should().ThrowAsync<ArgumentNullException>();
    }

    [TestMethod]
    public async Task RemoveOwners_GivenOwners_RemovesTheirEntriesOnly()
    {
        await using PostgresDatabase database = await SharedPostgres.CreateDatabaseAsync(CancellationToken);
        ISearchIndex index = await EnsureIndexAsync(ServicesFor(database), Notes);
        await index.UpsertAsync(
            [
                new SearchEntry(EntryId(1), OwnerId(1), "apple", []),
                new SearchEntry(EntryId(2), OwnerId(1), "apple", []),
                Entry(3, "apple"),
                Entry(4, "apple"),
            ],
            CancellationToken);

        int removed = await index.RemoveOwnersAsync([OwnerId(1), OwnerId(3), OwnerId(3), OwnerId(99)], CancellationToken);

        removed.Should().Be(3);
        (await index.SearchAsync(TextQuery("apple"), CancellationToken)).Select(hit => hit.EntryId).Should().Equal(EntryId(4));
    }

    [TestMethod]
    public async Task RemoveOwners_EmptyOrNull_ReturnsZeroOrThrows()
    {
        await using PostgresDatabase database = await SharedPostgres.CreateDatabaseAsync(CancellationToken);
        ISearchIndex index = await EnsureIndexAsync(ServicesFor(database), Notes);

        (await index.RemoveOwnersAsync([], CancellationToken)).Should().Be(0);
        await FluentActions.Awaiting(() => index.RemoveOwnersAsync(Missing.Reference<IReadOnlyCollection<Guid>>(), CancellationToken))
            .Should().ThrowAsync<ArgumentNullException>();
    }

    [TestMethod]
    public async Task Upsert_InsideFailingAmbientTransaction_IsRolledBack()
    {
        await using PostgresDatabase database = await SharedPostgres.CreateDatabaseAsync(CancellationToken);
        IServiceProvider services = ServicesFor(database);
        ISearchIndex index = await EnsureIndexAsync(services, Notes);

        Func<Task> act = () => services.GetRequiredService<ITransaction>().InvokeAsync(
            async () =>
            {
                await index.UpsertAsync([Entry(1, "apple")], CancellationToken);
                (await index.SearchAsync(TextQuery("apple"), CancellationToken)).Should().ContainSingle("the unit of work reads its own writes");
                throw new InvalidOperationException("planted");
            },
            CancellationToken);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("planted");
        (await ScalarAsync<long>(database, "SELECT count(*) FROM search_notes_entries")).Should().Be(0);
    }

    [TestMethod]
    public async Task AnyCall_CancelledToken_Throws()
    {
        await using PostgresDatabase database = await SharedPostgres.CreateDatabaseAsync(CancellationToken);
        ISearchIndex index = await EnsureIndexAsync(ServicesFor(database), Notes);
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        await FluentActions.Awaiting(() => index.UpsertAsync([Entry(1, "apple")], cancelled.Token)).Should().ThrowAsync<OperationCanceledException>();
        await FluentActions.Awaiting(() => index.SearchAsync(TextQuery("apple"), cancelled.Token)).Should().ThrowAsync<OperationCanceledException>();
        await FluentActions.Awaiting(() => index.CountWithoutEmbeddingAsync(cancelled.Token)).Should().ThrowAsync<OperationCanceledException>();
        (await ScalarAsync<long>(database, "SELECT count(*) FROM search_notes_entries")).Should().Be(0);
    }

    [TestMethod]
    public async Task AnyCall_IndexNeverEnsured_FailsWithTheStoresException()
    {
        await using PostgresDatabase database = await SharedPostgres.CreateDatabaseAsync(CancellationToken);
        ISearchIndex index = ServicesFor(database).GetRequiredService<ISearchIndex.Open>()(Notes);

        Func<Task> act = () => index.CountWithoutEmbeddingAsync(CancellationToken);

        (await act.Should().ThrowAsync<System.Data.Common.DbException>()).Which.SqlState.Should().Be("42P01");
    }

    private async Task SeedAsync(ISearchIndex index, params (int Number, string Text, ReadOnlyMemory<float> Vector)[] rows)
    {
        await index.UpsertAsync([.. rows.Select(row => Entry(row.Number, row.Text))], CancellationToken);
        await index.SetEmbeddingsAsync(
            [.. rows.Select(row => new SearchEmbedding(EntryId(row.Number), row.Text, row.Vector))], CancellationToken);
    }
}
