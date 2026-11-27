using System.Text.Json.Nodes;
using AwesomeAssertions;
using Nordstein.Core.Common.Serialization;

namespace Nordstein.Core.Common.Tests.Serialization;

/// <summary>
/// Canonical JSON: one spelling per JSON value, whatever order or spacing the input had, so a hash
/// over it is stable.
/// </summary>
[TestClass]
public sealed class CanonicalJsonTests
{
    [TestMethod]
    public void CanonicalJson_ReorderedAndSpaced_SameText()
    {
        JsonNode first = JsonNode.Parse("""{"b":1,"a":{"d":[3,{"z":true,"y":null}],"c":"x"}}""") ?? throw new InvalidOperationException();
        JsonNode second = JsonNode.Parse("{ \"a\" : { \"c\":\"x\",\n \"d\": [3, {\"y\": null, \"z\": true}] }, \"b\": 1 }") ?? throw new InvalidOperationException();

        CanonicalJson.Serialize(second).Should().Be(CanonicalJson.Serialize(first));
    }

    [TestMethod]
    public void CanonicalJson_SortsOrdinallyWithoutWhitespace()
    {
        JsonNode node = JsonNode.Parse("""{ "b": 1, "B": 2, "a": [ 2, 1 ], "_": "x" }""") ?? throw new InvalidOperationException();

        CanonicalJson.Serialize(node).Should().Be("""{"B":2,"_":"x","a":[2,1],"b":1}""");
    }

    [TestMethod]
    public void CanonicalJson_KeepsNumberSpellingAndArrayOrder()
        => CanonicalJson.Serialize(JsonNode.Parse("[1.50,1e3,-0]")).Should().Be("[1.50,1e3,-0]");

    [TestMethod]
    public void CanonicalJson_EscapesLikeSystemTextJsonDefaults()
        => CanonicalJson.Serialize(JsonNode.Parse("""{"k":"Jänner <b>"}""")).Should().Be("""{"k":"J\u00E4nner \u003Cb\u003E"}""");

    [TestMethod]
    public void CanonicalJson_Null_IsTheLiteral()
        => CanonicalJson.Serialize(null).Should().Be("null");

    [TestMethod]
    public void CanonicalJson_Scalar_IsItsText()
        => CanonicalJson.Serialize(JsonValue.Create("x")).Should().Be("\"x\"");

    [TestMethod]
    public void CanonicalJson_LeavesTheInputUnchanged()
    {
        JsonNode node = JsonNode.Parse("""{"b":1,"a":2}""") ?? throw new InvalidOperationException();

        CanonicalJson.Serialize(node);

        node.ToJsonString().Should().Be("""{"b":1,"a":2}""");
    }

    [TestMethod]
    public void CanonicalJson_NodeWithParent_Serializes()
    {
        JsonNode root = JsonNode.Parse("""{"outer":{"b":1,"a":2}}""") ?? throw new InvalidOperationException();

        CanonicalJson.Serialize(root["outer"]).Should().Be("""{"a":2,"b":1}""");
    }
}
