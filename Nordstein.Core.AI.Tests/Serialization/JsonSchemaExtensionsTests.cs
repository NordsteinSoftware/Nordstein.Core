using System.Text.Json.Nodes;
using AwesomeAssertions;
using Nordstein.Core.AI.Serialization;

namespace Nordstein.Core.AI.Tests.Serialization;

/// <summary>
/// Exercises <see cref="JsonSchemaExtensions.WithObjectsClosed"/>: every object schema closed and fully required, in every
/// subschema position (nested properties, array items, combinators, definitions), values that only look like schemas left
/// alone, the schemas it refuses to close, idempotence, and the input never changed.
/// </summary>
[TestClass]
public sealed class JsonSchemaExtensionsTests
{
    [TestMethod]
    public void WithObjectsClosed_FlatObject_ClosesItAndRequiresEveryProperty()
    {
        JsonNode closed = Parse("""{"type":"object","properties":{"total":{"type":"number"},"note":{"type":"string"}}}""").WithObjectsClosed();

        closed.ToJsonString().Should().Be("""{"type":"object","properties":{"total":{"type":"number"},"note":{"type":"string"}},"additionalProperties":false,"required":["total","note"]}""");
    }

    [TestMethod]
    public void WithObjectsClosed_PartlyRequired_KeepsTheListAndAppendsTheRestInDeclarationOrder()
    {
        JsonNode closed = Parse("""{"type":"object","required":["c"],"properties":{"a":{},"b":{},"c":{}}}""").WithObjectsClosed();

        closed["required"]?.ToJsonString().Should().Be("""["c","a","b"]""");
        closed.ToJsonString().Should().StartWith("""{"type":"object","required":["c","a","b"],""");
    }

    [TestMethod]
    public void WithObjectsClosed_NestedObjectsAndArraysOfObjects_ClosesEveryLevel()
    {
        const string schema = """
            {"type":"object","properties":{
              "party":{"type":"object","properties":{"name":{"type":"string"},"address":{"type":["object","null"],"properties":{"city":{"type":"string"}}}}},
              "lines":{"type":"array","items":{"type":"object","properties":{"sku":{"type":"string"}}}}}}
            """;

        JsonNode closed = Parse(schema).WithObjectsClosed();

        Closed(closed, "a", "party", "lines");
        Closed(closed["properties"]?["party"], "b", "name", "address");
        Closed(closed["properties"]?["party"]?["properties"]?["address"], "c", "city");
        Closed(closed["properties"]?["lines"]?["items"], "d", "sku");
    }

    [TestMethod]
    public void WithObjectsClosed_Combinators_ClosesEveryBranch()
    {
        const string schema = """
            {"anyOf":[{"type":"object","properties":{"a":{}}},{"type":"string"}],
             "oneOf":[{"properties":{"b":{}}}],
             "allOf":[{"type":"object","properties":{"c":{}}}],
             "not":{"type":"object","properties":{"d":{}}},
             "if":{"properties":{"e":{}}},"then":{"properties":{"f":{}}},"else":{"properties":{"g":{}}}}
            """;

        JsonNode closed = Parse(schema).WithObjectsClosed();

        Closed(closed["anyOf"]?[0], "anyOf", "a");
        closed["anyOf"]?[1]?.ToJsonString().Should().Be("""{"type":"string"}""");
        Closed(closed["oneOf"]?[0], "oneOf", "b");
        Closed(closed["allOf"]?[0], "allOf", "c");
        Closed(closed["not"], "not", "d");
        Closed(closed["if"], "if", "e");
        Closed(closed["then"], "then", "f");
        Closed(closed["else"], "else", "g");
        closed["additionalProperties"].Should().BeNull("the root holds no properties and names no object type");
    }

    [TestMethod]
    public void WithObjectsClosed_DefinitionsAndReferences_ClosesTheDefinitionsAndLeavesTheReferences()
    {
        const string schema = """
            {"type":"object","properties":{"party":{"$ref":"#/$defs/party"},"old":{"$ref":"#/definitions/old"}},
             "$defs":{"party":{"type":"object","properties":{"name":{"type":"string"}}}},
             "definitions":{"old":{"type":"object","properties":{"id":{"type":"integer"}}}}}
            """;

        JsonNode closed = Parse(schema).WithObjectsClosed();

        Closed(closed, "root", "party", "old");
        closed["properties"]?["party"]?.ToJsonString().Should().Be("""{"$ref":"#/$defs/party"}""");
        Closed(closed["$defs"]?["party"], "$defs", "name");
        Closed(closed["definitions"]?["old"], "definitions", "id");
    }

    [TestMethod]
    public void WithObjectsClosed_TupleItemsPrefixItemsAndPatternProperties_ClosesTheirSchemas()
    {
        const string schema = """
            {"type":"array","items":[{"type":"object","properties":{"a":{}}}],"prefixItems":[{"type":"object","properties":{"b":{}}}],
             "contains":{"type":"object"},
             "patternProperties":{"^x":{"type":"object","properties":{"c":{}}}},"dependentSchemas":{"y":{"properties":{"d":{}}}}}
            """;

        JsonNode closed = Parse(schema).WithObjectsClosed();

        Closed(closed["items"]?[0], "items", "a");
        Closed(closed["prefixItems"]?[0], "prefixItems", "b");
        closed["contains"]?.ToJsonString().Should().Be("""{"type":"object","additionalProperties":false}""");
        Closed(closed["patternProperties"]?["^x"], "patternProperties", "c");
        Closed(closed["dependentSchemas"]?["y"], "dependentSchemas", "d");
    }

    [TestMethod]
    public void WithObjectsClosed_RemainingSubschemaKeywords_CloseTheirSchemas()
    {
        const string schema = """
            {"additionalItems":{"type":"object"},"unevaluatedItems":{"type":"object"},"propertyNames":{"type":"object"}}
            """;

        JsonNode closed = Parse(schema).WithObjectsClosed();

        closed.ToJsonString().Should().Be(
            """{"additionalItems":{"type":"object","additionalProperties":false},"unevaluatedItems":{"type":"object","additionalProperties":false},"propertyNames":{"type":"object","additionalProperties":false}}""");
    }

    [TestMethod]
    [DataRow("""{"type":["object","null"]}""", true)]
    [DataRow("""{"type":["null","object"]}""", true)]
    [DataRow("""{"type":["string",5,null]}""", false)]
    [DataRow("""{"type":"string"}""", false)]
    [DataRow("""{"type":5}""", false)]
    [DataRow("""{"type":null}""", false)]
    public void WithObjectsClosed_TypeNamingObject_DecidesWhetherASchemaWithoutPropertiesCloses(string schema, bool closes)
    {
        JsonNode closed = Parse(schema).WithObjectsClosed();

        closed.AsObject().ContainsKey("additionalProperties").Should().Be(closes);
    }

    [TestMethod]
    public void WithObjectsClosed_ObjectTypeWithoutProperties_IsClosedWithoutARequiredList()
    {
        Parse("""{"type":"object"}""").WithObjectsClosed().ToJsonString().Should().Be("""{"type":"object","additionalProperties":false}""");
        Parse("""{"type":"object","properties":{}}""").WithObjectsClosed().ToJsonString().Should().Be("""{"type":"object","properties":{},"additionalProperties":false,"required":[]}""");
    }

    [TestMethod]
    public void WithObjectsClosed_AdditionalPropertiesTrue_BecomesFalse()
    {
        JsonNode closed = Parse("""{"type":"object","additionalProperties":true,"properties":{"a":{}}}""").WithObjectsClosed();

        closed.ToJsonString().Should().Be("""{"type":"object","additionalProperties":false,"properties":{"a":{}},"required":["a"]}""");
    }

    [TestMethod]
    public void WithObjectsClosed_ValuesThatLookLikeSchemas_AreLeftAlone()
    {
        const string schema = """
            {"type":"object","properties":{"properties":{"type":"object","properties":{"items":{}}},"kind":{"enum":[{"properties":{"x":{}}}],"const":{"type":"object"},"default":{"type":"object"},"examples":[{"type":"object"}]}}}
            """;

        JsonNode closed = Parse(schema).WithObjectsClosed();

        Closed(closed, "root", "properties", "kind");
        Closed(closed["properties"]?["properties"], "a property named properties", "items");
        closed["properties"]?["kind"]?.ToJsonString().Should().Be("""{"enum":[{"properties":{"x":{}}}],"const":{"type":"object"},"default":{"type":"object"},"examples":[{"type":"object"}]}""");
    }

    [TestMethod]
    public void WithObjectsClosed_BooleanSchemas_AreLeftAsGiven()
    {
        Parse("true").WithObjectsClosed().ToJsonString().Should().Be("true");
        Parse("""{"type":"object","properties":{"any":true,"none":false}}""").WithObjectsClosed()["properties"]?.ToJsonString().Should().Be("""{"any":true,"none":false}""");
    }

    [TestMethod]
    public void WithObjectsClosed_ClosedSchema_IsIdempotent()
    {
        JsonNode once = Parse("""{"type":"object","properties":{"a":{"type":"object","properties":{"b":{}}},"c":{"type":"array","items":{"type":"object"}}},"required":["c"]}""").WithObjectsClosed();

        JsonNode twice = once.WithObjectsClosed();

        JsonNode.DeepEquals(once, twice).Should().BeTrue();
        twice.ToJsonString().Should().Be(once.ToJsonString());
    }

    [TestMethod]
    public void WithObjectsClosed_Input_IsNeverChanged()
    {
        const string text = """{"type":"object","properties":{"a":{"type":"object","properties":{"b":{}}}},"$defs":{"d":{"type":"object"}}}""";
        JsonNode schema = Parse(text);

        JsonNode closed = schema.WithObjectsClosed();

        schema.ToJsonString().Should().Be(text);
        closed.Should().NotBeSameAs(schema);
        closed["properties"]?["a"].Should().NotBeSameAs(schema["properties"]?["a"]);
        closed.Parent.Should().BeNull();
    }

    [TestMethod]
    public void WithObjectsClosed_ChildOfALargerDocument_ReturnsAParentlessCopy()
    {
        JsonNode document = Parse("""{"response_format":{"type":"object","properties":{"a":{}}}}""");

        JsonNode closed = (document["response_format"] ?? throw new InvalidOperationException()).WithObjectsClosed();

        closed.Parent.Should().BeNull();
        document.ToJsonString().Should().Be("""{"response_format":{"type":"object","properties":{"a":{}}}}""");
    }

    [TestMethod]
    [DataRow("""{"type":"object","additionalProperties":{"type":"string"}}""", "/additionalProperties")]
    [DataRow("""{"type":"object","properties":{"tags":{"type":"object","additionalProperties":{}}}}""", "/properties/tags/additionalProperties")]
    [DataRow("""{"type":"object","properties":{"a/b~c":{"type":"object","additionalProperties":5}}}""", "/properties/a~1b~0c/additionalProperties")]
    [DataRow("""{"type":"object","additionalProperties":null}""", "/additionalProperties")]
    public void WithObjectsClosed_AdditionalPropertiesSchema_Refused(string schema, string pointer)
    {
        Action act = () => Parse(schema).WithObjectsClosed();

        act.Should().Throw<ArgumentException>().WithParameterName("schema").Which.Message.Should().Contain($"'{pointer}'");
    }

    [TestMethod]
    [DataRow("""{"type":"object","properties":{"a":{}},"required":"a"}""", "/required")]
    [DataRow("""{"type":"object","properties":{"a":{}},"required":[1]}""", "/required")]
    [DataRow("""{"type":"object","properties":{"a":{}},"required":[null]}""", "/required")]
    [DataRow("""{"type":"object","properties":{"a":{}},"required":["a","ghost"]}""", "/required")]
    [DataRow("""{"type":"object","required":["ghost"]}""", "/required")]
    [DataRow("""{"type":"object","properties":[]}""", "/properties")]
    [DataRow("""{"anyOf":{"type":"object"}}""", "/anyOf")]
    [DataRow("""{"$defs":[]}""", "/$defs")]
    public void WithObjectsClosed_KeywordThatCannotBeClosed_Refused(string schema, string pointer)
    {
        Action act = () => Parse(schema).WithObjectsClosed();

        act.Should().Throw<ArgumentException>().WithParameterName("schema").Which.Message.Should().Contain($"'{pointer}'");
    }

    [TestMethod]
    [DataRow("[]")]
    [DataRow("\"object\"")]
    [DataRow("5")]
    public void WithObjectsClosed_RootNeitherObjectNorBoolean_Refused(string schema)
    {
        Action act = () => Parse(schema).WithObjectsClosed();

        act.Should().Throw<ArgumentException>().WithParameterName("schema");
    }

    [TestMethod]
    public void WithObjectsClosed_NestedDeeperThan64Levels_Refused()
    {
        JsonNode schema = new JsonObject();
        JsonNode innermost = schema;
        for (int level = 1; level < 100; level++)
        {
            var next = new JsonObject();
            innermost.AsObject()["not"] = next;
            innermost = next;
        }

        Action act = () => schema.WithObjectsClosed();

        act.Should().Throw<ArgumentException>().WithParameterName("schema").Which.Message.Should().Contain("64");
    }

    [TestMethod]
    public void WithObjectsClosed_Exactly64Levels_IsClosed()
    {
        JsonNode schema = new JsonObject { ["type"] = "object" };
        JsonNode innermost = schema;
        for (int level = 1; level < 32; level++)
        {
            var next = new JsonObject { ["type"] = "object" };
            innermost.AsObject()["properties"] = new JsonObject { ["x"] = next };
            innermost = next;
        }

        JsonNode closed = schema.WithObjectsClosed();

        closed["additionalProperties"]?.GetValue<bool>().Should().BeFalse();
    }

    [TestMethod]
    public void WithObjectsClosed_Null_Throws()
    {
        Action act = () => NullNode().WithObjectsClosed();

        act.Should().Throw<ArgumentNullException>().WithParameterName("schema");
    }

    [TestMethod]
    public async Task WithObjectsClosed_FedToTheStrictOutputFormat_RejectsAnUndeclaredProperty()
    {
        var format = new JsonSchemaOutputFormat(Parse("""{"type":"object","properties":{"total":{"type":"number"}}}""").WithObjectsClosed(), "totals");

        Func<Task> extra = () => format.ParseAsync<JsonNode>("""{"total":1,"note":"x"}""");
        Func<Task> missing = () => format.ParseAsync<JsonNode>("{}");

        (await extra.Should().ThrowAsync<OutputFormatException>()).Which.Violation.Should().Be(OutputFormatViolation.UnexpectedProperty);
        (await missing.Should().ThrowAsync<OutputFormatException>()).Which.Violation.Should().Be(OutputFormatViolation.MissingRequired);
        format.ResponseFormat.Schema.Should().Be("""{"type":"object","properties":{"total":{"type":"number"}},"additionalProperties":false,"required":["total"]}""");
    }

    private static void Closed(JsonNode? schema, string because, params string[] required)
    {
        schema.Should().NotBeNull(because);
        schema?["additionalProperties"]?.GetValue<bool>().Should().BeFalse(because);
        schema?["required"]?.AsArray().Select(name => name?.GetValue<string>()).Should().Equal(required, because);
    }

    private static JsonNode Parse(string json) => JsonNode.Parse(json) ?? throw new InvalidOperationException("The test schema is null.");

    private static JsonNode NullNode() => (new JsonNode[1])[0];
}
