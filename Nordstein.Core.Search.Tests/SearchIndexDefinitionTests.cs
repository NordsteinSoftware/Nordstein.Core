using AwesomeAssertions;
using Nordstein.Core.Search.Tests.Internal;

namespace Nordstein.Core.Search.Tests;

[TestClass]
public sealed class SearchIndexDefinitionTests
{
    [TestMethod]
    public void Constructor_ValidValues_KeepsThem()
    {
        var definition = new SearchIndexDefinition("notes_v2", "example-embed-1", 1024, "english");

        definition.Name.Should().Be("notes_v2");
        definition.EmbeddingModelId.Should().Be("example-embed-1");
        definition.Dimension.Should().Be(1024);
        definition.TextSearchConfig.Should().Be("english");
    }

    [TestMethod]
    public void Equals_SameValues_IsEqual()
    {
        var first = new SearchIndexDefinition("notes", "m", 8, "simple");
        var second = new SearchIndexDefinition("notes", "m", 8, "simple");

        first.Should().Be(second);
        first.Should().NotBe(new SearchIndexDefinition("notes", "m", 9, "simple"));
    }

    [TestMethod]
    [DataRow("")]
    [DataRow("Notes")]
    [DataRow("1notes")]
    [DataRow("_notes")]
    [DataRow("notes-v2")]
    [DataRow("notes v2")]
    [DataRow("notes;drop")]
    [DataRow("nötes")]
    [DataRow("a2345678901234567890123456789012345678901")]
    public void Constructor_InvalidName_Throws(string name)
    {
        Action act = () => _ = new SearchIndexDefinition(name, "m", 8, "simple");

        act.Should().Throw<ArgumentException>().WithParameterName("name");
    }

    [TestMethod]
    [DataRow("notes", true)]
    [DataRow("n", true)]
    [DataRow("Notes", false)]
    [DataRow("", false)]
    [DataRow(null, false)]
    public void IsValidName_Candidate_MatchesTheConstructorRule(string? name, bool expected)
        => SearchIndexDefinition.IsValidName(name).Should().Be(expected);

    [TestMethod]
    public void Constructor_NameOfFortyCharacters_IsAccepted()
    {
        string name = "a" + new string('1', 39);

        new SearchIndexDefinition(name, "m", 8, "simple").Name.Should().Be(name);
    }

    [TestMethod]
    [DataRow("")]
    [DataRow(" model")]
    [DataRow("model ")]
    public void Constructor_InvalidModelId_Throws(string modelId)
    {
        Action act = () => _ = new SearchIndexDefinition("notes", modelId, 8, "simple");

        act.Should().Throw<ArgumentException>().WithParameterName("embeddingModelId");
    }

    [TestMethod]
    public void Constructor_ModelIdLengthAtAndAboveLimit_AcceptsThenThrows()
    {
        string longest = new('m', SearchIndexDefinition.MaxEmbeddingModelIdLength);
        Action tooLong = () => _ = new SearchIndexDefinition("notes", longest + "m", 8, "simple");

        new SearchIndexDefinition("notes", longest, 8, "simple").EmbeddingModelId.Should().Be(longest);
        tooLong.Should().Throw<ArgumentException>().WithParameterName("embeddingModelId");
    }

    [TestMethod]
    [DataRow(0)]
    [DataRow(-1)]
    [DataRow(SearchIndexDefinition.MaxDimension + 1)]
    public void Constructor_DimensionOutOfRange_Throws(int dimension)
    {
        Action act = () => _ = new SearchIndexDefinition("notes", "m", dimension, "simple");

        act.Should().Throw<ArgumentOutOfRangeException>().WithParameterName("dimension");
    }

    [TestMethod]
    [DataRow(1)]
    [DataRow(SearchIndexDefinition.MaxDimension)]
    public void Constructor_DimensionAtBoundary_IsAccepted(int dimension)
        => new SearchIndexDefinition("notes", "m", dimension, "simple").Dimension.Should().Be(dimension);

    [TestMethod]
    [DataRow("")]
    [DataRow("English")]
    [DataRow("9english")]
    [DataRow("english'; drop table x; --")]
    [DataRow("pg_catalog.english")]
    public void Constructor_InvalidTextSearchConfig_Throws(string config)
    {
        Action act = () => _ = new SearchIndexDefinition("notes", "m", 8, config);

        act.Should().Throw<ArgumentException>().WithParameterName("textSearchConfig");
    }

    [TestMethod]
    public void Constructor_TextSearchConfigLengthAtAndAboveLimit_AcceptsThenThrows()
    {
        string longest = "_" + new string('a', 62);
        Action tooLong = () => _ = new SearchIndexDefinition("notes", "m", 8, longest + "a");

        new SearchIndexDefinition("notes", "m", 8, longest).TextSearchConfig.Should().Be(longest);
        tooLong.Should().Throw<ArgumentException>().WithParameterName("textSearchConfig");
    }

    [TestMethod]
    public void Constructor_NullString_Throws()
    {
        string missing = Missing.Reference<string>();
        Action name = () => _ = new SearchIndexDefinition(missing, "m", 8, "simple");
        Action model = () => _ = new SearchIndexDefinition("notes", missing, 8, "simple");
        Action config = () => _ = new SearchIndexDefinition("notes", "m", 8, missing);

        name.Should().Throw<ArgumentNullException>().WithParameterName("name");
        model.Should().Throw<ArgumentNullException>().WithParameterName("embeddingModelId");
        config.Should().Throw<ArgumentNullException>().WithParameterName("textSearchConfig");
    }
}
