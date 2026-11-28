using AwesomeAssertions;
using Nordstein.Core.Search.Tests.Internal;

namespace Nordstein.Core.Search.Tests;

[TestClass]
public sealed class SearchValueTypeTests
{
    private static readonly Guid Id = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid Owner = Guid.Parse("22222222-2222-2222-2222-222222222222");

    [TestMethod]
    public void SearchEntry_ValidValues_KeepsThemAndCopiesFacets()
    {
        var facets = new List<string> { "kind:note", "state:open", "kind:note" };

        var entry = new SearchEntry(Id, Owner, "Some text.", facets);
        facets.Add("state:closed");

        entry.Id.Should().Be(Id);
        entry.OwnerId.Should().Be(Owner);
        entry.Text.Should().Be("Some text.");
        entry.Facets.Should().BeEquivalentTo(["kind:note", "state:open"]);
    }

    [TestMethod]
    public void SearchEntry_FacetsCompareOrdinally_KeepsBothCasings()
        => new SearchEntry(Id, Owner, "t", ["Kind:Note", "kind:note"]).Facets.Should().HaveCount(2);

    [TestMethod]
    public void SearchEntry_FacetWithSurrogatePair_IsAccepted()
        => new SearchEntry(Id, Owner, "t", ["mood:\uD83D\uDE00"]).Facets.Should().ContainSingle();

    [TestMethod]
    public void SearchEntry_EmptyIds_Throw()
    {
        Action id = () => _ = new SearchEntry(Guid.Empty, Owner, "t", []);
        Action owner = () => _ = new SearchEntry(Id, Guid.Empty, "t", []);

        id.Should().Throw<ArgumentException>().WithParameterName("id");
        owner.Should().Throw<ArgumentException>().WithParameterName("ownerId");
    }

    [TestMethod]
    [DataRow("")]
    [DataRow("   ")]
    [DataRow("with\0nul")]
    public void SearchEntry_InvalidText_Throws(string text)
    {
        Action act = () => _ = new SearchEntry(Id, Owner, text, []);

        act.Should().Throw<ArgumentException>().WithParameterName("text");
    }

    [TestMethod]
    public void SearchEntry_TextLengthAtAndAboveLimit_AcceptsThenThrows()
    {
        string longest = new('a', SearchEntry.MaxTextLength);
        Action tooLong = () => _ = new SearchEntry(Id, Owner, longest + "a", []);

        new SearchEntry(Id, Owner, longest, []).Text.Should().HaveLength(SearchEntry.MaxTextLength);
        tooLong.Should().Throw<ArgumentException>().WithParameterName("text");
    }

    [TestMethod]
    public void SearchEntry_NullArguments_Throw()
    {
        Action text = () => _ = new SearchEntry(Id, Owner, Missing.Reference<string>(), []);
        Action facets = () => _ = new SearchEntry(Id, Owner, "t", Missing.Reference<IEnumerable<string>>());

        text.Should().Throw<ArgumentNullException>().WithParameterName("text");
        facets.Should().Throw<ArgumentNullException>().WithParameterName("facets");
    }

    [TestMethod]
    [DataRow("")]
    [DataRow("line\nbreak")]
    [DataRow("nul\0")]
    public void SearchEntry_InvalidFacet_Throws(string facet)
    {
        Action act = () => _ = new SearchEntry(Id, Owner, "t", [facet]);

        act.Should().Throw<ArgumentException>().WithParameterName("facets");
    }

    [TestMethod]
    public void SearchEntry_LoneSurrogateInTextOrFacet_Throws()
    {
        // Built at run time: a lone surrogate has no UTF-8 form, so an attribute argument loses it.
        string high = "high" + (char)0xD83D;
        string low = (char)0xDE00 + "low";
        Action text = () => _ = new SearchEntry(Id, Owner, high + "text", []);
        Action highFacet = () => _ = new SearchEntry(Id, Owner, "t", [high]);
        Action lowFacet = () => _ = new SearchEntry(Id, Owner, "t", [low]);

        text.Should().Throw<ArgumentException>().WithParameterName("text");
        highFacet.Should().Throw<ArgumentException>().WithParameterName("facets");
        lowFacet.Should().Throw<ArgumentException>().WithParameterName("facets");
    }

    [TestMethod]
    public void SearchEntry_NullFacet_Throws()
    {
        Action act = () => _ = new SearchEntry(Id, Owner, "t", [Missing.Reference<string>()]);

        act.Should().Throw<ArgumentException>().WithParameterName("facets");
    }

    [TestMethod]
    public void SearchEntry_FacetLengthAndCountAtAndAboveLimit_AcceptsThenThrows()
    {
        string longest = new('f', SearchEntry.MaxFacetLength);
        string[] most = [.. Enumerable.Range(0, SearchEntry.MaxFacets).Select(index => $"f:{index}")];
        Action tooLong = () => _ = new SearchEntry(Id, Owner, "t", [longest + "f"]);
        Action tooMany = () => _ = new SearchEntry(Id, Owner, "t", [.. most, "one-more"]);

        new SearchEntry(Id, Owner, "t", [longest]).Facets.Should().ContainSingle();
        new SearchEntry(Id, Owner, "t", most).Facets.Should().HaveCount(SearchEntry.MaxFacets);
        tooLong.Should().Throw<ArgumentException>().WithParameterName("facets");
        tooMany.Should().Throw<ArgumentException>().WithParameterName("facets");
    }

    [TestMethod]
    public void SearchEmbedding_Values_AreKept()
    {
        ReadOnlyMemory<float> vector = new float[] { 0.5f, -0.5f };

        var embedding = new SearchEmbedding(Id, "t", vector);

        embedding.EntryId.Should().Be(Id);
        embedding.Text.Should().Be("t");
        embedding.Vector.ToArray().Should().Equal(0.5f, -0.5f);
    }

    [TestMethod]
    public void SearchFilter_None_PassesEverything()
    {
        SearchFilter.None.Require.Should().BeEmpty();
        SearchFilter.None.Exclude.Should().BeEmpty();
    }

    [TestMethod]
    public void SearchFilter_ValidGroups_AreCopied()
    {
        var group = new List<string> { "kind:note", "kind:memo" };

        var filter = new SearchFilter([group, ["state:open"]], ["state:hidden"]);
        group.Clear();

        filter.Require.Should().HaveCount(2);
        filter.Require[0].Should().BeEquivalentTo(["kind:note", "kind:memo"]);
        filter.Require[1].Should().BeEquivalentTo(["state:open"]);
        filter.Exclude.Should().BeEquivalentTo(["state:hidden"]);
    }

    [TestMethod]
    public void SearchFilter_EmptyGroup_Throws()
    {
        Action act = () => _ = new SearchFilter([[]], []);

        act.Should().Throw<ArgumentException>().WithParameterName("require");
    }

    [TestMethod]
    public void SearchFilter_GroupCountAtAndAboveLimit_AcceptsThenThrows()
    {
        string[][] most = [.. Enumerable.Range(0, SearchFilter.MaxGroups).Select(index => new[] { $"g:{index}" })];
        Action tooMany = () => _ = new SearchFilter([.. most, ["one-more"]], []);

        new SearchFilter(most, []).Require.Should().HaveCount(SearchFilter.MaxGroups);
        tooMany.Should().Throw<ArgumentException>().WithParameterName("require");
    }

    [TestMethod]
    public void SearchFilter_TooManyFacetsInGroupOrExclude_Throws()
    {
        string[] tooMany = [.. Enumerable.Range(0, SearchFilter.MaxFacetsPerGroup + 1).Select(index => $"f:{index}")];
        Action group = () => _ = new SearchFilter([tooMany], []);
        Action exclude = () => _ = new SearchFilter([], tooMany);

        group.Should().Throw<ArgumentException>().WithParameterName("require");
        exclude.Should().Throw<ArgumentException>().WithParameterName("exclude");
    }

    [TestMethod]
    public void SearchFilter_InvalidFacetOrNull_Throws()
    {
        Action facet = () => _ = new SearchFilter([[""]], []);
        Action excluded = () => _ = new SearchFilter([], ["bad\tfacet"]);
        Action require = () => _ = new SearchFilter(Missing.Reference<IEnumerable<IEnumerable<string>>>(), []);
        Action exclude = () => _ = new SearchFilter([], Missing.Reference<IEnumerable<string>>());
        Action nullGroup = () => _ = new SearchFilter([Missing.Reference<IEnumerable<string>>()], []);

        facet.Should().Throw<ArgumentException>().WithParameterName("require");
        excluded.Should().Throw<ArgumentException>().WithParameterName("exclude");
        require.Should().Throw<ArgumentNullException>().WithParameterName("require");
        exclude.Should().Throw<ArgumentNullException>().WithParameterName("exclude");
        nullGroup.Should().Throw<ArgumentNullException>().WithParameterName("require");
    }

    [TestMethod]
    public void SearchParameters_Default_IsOneHundredSixtyTen()
    {
        SearchParameters.Default.CandidatesPerLeg.Should().Be(100);
        SearchParameters.Default.RrfK.Should().Be(60);
        SearchParameters.Default.MaxResults.Should().Be(10);
        SearchParameters.Default.Should().Be(new SearchParameters(100, 60, 10));
    }

    [TestMethod]
    [DataRow(0, 60, 10, "candidatesPerLeg")]
    [DataRow(SearchParameters.MaxCandidatesPerLeg + 1, 60, 10, "candidatesPerLeg")]
    [DataRow(100, 0, 10, "rrfK")]
    [DataRow(100, SearchParameters.MaxRrfK + 1, 10, "rrfK")]
    [DataRow(100, 60, 0, "maxResults")]
    [DataRow(100, 60, SearchParameters.MaxMaxResults + 1, "maxResults")]
    public void SearchParameters_OutOfRange_Throws(int candidates, int k, int maxResults, string parameter)
    {
        Action act = () => _ = new SearchParameters(candidates, k, maxResults);

        act.Should().Throw<ArgumentOutOfRangeException>().WithParameterName(parameter);
    }

    [TestMethod]
    public void SearchParameters_AtUpperBounds_AreAccepted()
    {
        var parameters = new SearchParameters(
            SearchParameters.MaxCandidatesPerLeg, SearchParameters.MaxRrfK, SearchParameters.MaxMaxResults);

        parameters.CandidatesPerLeg.Should().Be(SearchParameters.MaxCandidatesPerLeg);
        parameters.RrfK.Should().Be(SearchParameters.MaxRrfK);
        parameters.MaxResults.Should().Be(SearchParameters.MaxMaxResults);
    }

    [TestMethod]
    public void SearchQuery_TextAndEmbedding_AreKept()
    {
        ReadOnlyMemory<float> embedding = new float[] { 1f };

        var query = new SearchQuery("words", embedding, SearchFilter.None, SearchParameters.Default);

        query.Text.Should().Be("words");
        query.Embedding.Should().NotBeNull();
        query.Filter.Should().BeSameAs(SearchFilter.None);
        query.Parameters.Should().BeSameAs(SearchParameters.Default);
    }

    [TestMethod]
    public void SearchQuery_BlankTextWithEmbedding_IsAccepted()
        => new SearchQuery(" ", new float[] { 1f }, SearchFilter.None, SearchParameters.Default).Text.Should().Be(" ");

    [TestMethod]
    public void SearchQuery_BlankTextWithoutEmbedding_Throws()
    {
        Action act = () => _ = new SearchQuery(" ", null, SearchFilter.None, SearchParameters.Default);

        act.Should().Throw<ArgumentException>().WithParameterName("text");
    }

    [TestMethod]
    public void SearchQuery_TextTooLongOrWithNul_Throws()
    {
        string longest = new('a', SearchQuery.MaxTextLength);
        Action tooLong = () => _ = new SearchQuery(longest + "a", null, SearchFilter.None, SearchParameters.Default);
        Action nul = () => _ = new SearchQuery("a\0b", null, SearchFilter.None, SearchParameters.Default);
        Action surrogate = () => _ = new SearchQuery("a" + (char)0xDC00 + "b", null, SearchFilter.None, SearchParameters.Default);

        new SearchQuery(longest, null, SearchFilter.None, SearchParameters.Default).Text.Should().HaveLength(SearchQuery.MaxTextLength);
        tooLong.Should().Throw<ArgumentException>().WithParameterName("text");
        nul.Should().Throw<ArgumentException>().WithParameterName("text");
        surrogate.Should().Throw<ArgumentException>().WithParameterName("text");
    }

    [TestMethod]
    public void SearchQuery_NullArguments_Throw()
    {
        Action text = () => _ = new SearchQuery(Missing.Reference<string>(), null, SearchFilter.None, SearchParameters.Default);
        Action filter = () => _ = new SearchQuery("t", null, Missing.Reference<SearchFilter>(), SearchParameters.Default);
        Action parameters = () => _ = new SearchQuery("t", null, SearchFilter.None, Missing.Reference<SearchParameters>());

        text.Should().Throw<ArgumentNullException>().WithParameterName("text");
        filter.Should().Throw<ArgumentNullException>().WithParameterName("filter");
        parameters.Should().Throw<ArgumentNullException>().WithParameterName("parameters");
    }

    [TestMethod]
    public void SearchHit_Values_AreKept()
    {
        var hit = new SearchHit(Id, Owner, "t", 0.03, 1, null);

        hit.Should().Be(new SearchHit(Id, Owner, "t", 0.03, 1, null));
        hit.VectorRank.Should().Be(1);
        hit.TextRank.Should().BeNull();
    }

    [TestMethod]
    public void SearchSchemaMismatchException_Definitions_AreKeptAndNamed()
    {
        var bound = new SearchIndexDefinition("notes", "old-model", 8, "simple");
        var requested = new SearchIndexDefinition("notes", "new-model", 16, "english");

        var exception = new SearchSchemaMismatchException(bound, requested);

        exception.Bound.Should().Be(bound);
        exception.Requested.Should().Be(requested);
        exception.Message.Should().Contain("old-model").And.Contain("new-model").And.Contain("16").And.Contain("english");
    }

    [TestMethod]
    public void SearchSchemaMismatchException_NullDefinition_Throws()
    {
        var definition = new SearchIndexDefinition("notes", "m", 8, "simple");
        Action bound = () => _ = new SearchSchemaMismatchException(Missing.Reference<SearchIndexDefinition>(), definition);
        Action requested = () => _ = new SearchSchemaMismatchException(definition, Missing.Reference<SearchIndexDefinition>());

        bound.Should().Throw<ArgumentNullException>().WithParameterName("bound");
        requested.Should().Throw<ArgumentNullException>().WithParameterName("requested");
    }
}
