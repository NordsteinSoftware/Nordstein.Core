using System.Text.Json.Nodes;
using AwesomeAssertions;
using Nordstein.Core.AI.Clients;
using Nordstein.Core.AI.Serialization;

namespace Nordstein.Core.AI.Tests.Serialization;

/// <summary>
/// Exercises the construction of <see cref="JsonSchemaOutputFormat"/>: the argument guards, the
/// schema subset it accepts (every keyword it checks plus pure annotations) and the clear
/// <see cref="ArgumentException"/> for anything else, and the response format it carries.
/// </summary>
[TestClass]
public sealed class JsonSchemaOutputFormatSchemaTests
{
    [TestMethod]
    public void Ctor_NullSchema_Throws()
    {
        Action act = () => _ = new JsonSchemaOutputFormat(NullNode(), "fields");

        act.Should().Throw<ArgumentNullException>().WithParameterName("schema");
    }

    [TestMethod]
    public void Ctor_NullName_Throws()
    {
        Action act = () => _ = new JsonSchemaOutputFormat(new JsonObject(), (new string[1])[0]);

        act.Should().Throw<ArgumentNullException>().WithParameterName("name");
    }

    [TestMethod]
    [DataRow("")]
    [DataRow("two words")]
    public void Ctor_InvalidName_Throws(string name)
    {
        Action act = () => _ = new JsonSchemaOutputFormat(new JsonObject(), name);

        act.Should().Throw<ArgumentException>().WithParameterName("name");
    }

    [TestMethod]
    [DataRow("true")]
    [DataRow("[]")]
    [DataRow("\"object\"")]
    [DataRow("1")]
    public void Ctor_SchemaNotAnObject_Throws(string schema)
    {
        Action act = () => _ = new JsonSchemaOutputFormat(Parse(schema), "fields");

        act.Should().Throw<ArgumentException>().WithParameterName("schema");
    }

    [TestMethod]
    [DataRow("""{"$ref":"#/$defs/x"}""", "$ref", "#")]
    [DataRow("""{"$defs":{}}""", "$defs", "#")]
    [DataRow("""{"definitions":{}}""", "definitions", "#")]
    [DataRow("""{"anyOf":[{"type":"string"}]}""", "anyOf", "#")]
    [DataRow("""{"oneOf":[{"type":"string"}]}""", "oneOf", "#")]
    [DataRow("""{"allOf":[{"type":"string"}]}""", "allOf", "#")]
    [DataRow("""{"not":{"type":"string"}}""", "not", "#")]
    [DataRow("""{"if":{},"then":{}}""", "if", "#")]
    [DataRow("""{"const":"x"}""", "const", "#")]
    [DataRow("""{"pattern":"^a$"}""", "pattern", "#")]
    [DataRow("""{"format":"date"}""", "format", "#")]
    [DataRow("""{"minimum":0}""", "minimum", "#")]
    [DataRow("""{"maxItems":3}""", "maxItems", "#")]
    [DataRow("""{"patternProperties":{}}""", "patternProperties", "#")]
    [DataRow("""{"prefixItems":[]}""", "prefixItems", "#")]
    [DataRow("""{"type":"object","properties":{"a":{"type":"object","properties":{"b~/c":{"anyOf":[]}}}}}""", "anyOf", "#/properties/a/properties/b~0~1c")]
    [DataRow("""{"type":"array","items":{"$ref":"#"}}""", "$ref", "#/items")]
    public void Ctor_UnsupportedKeyword_ThrowsNamingKeywordAndSchemaPointer(string schema, string keyword, string pointer)
    {
        Action act = () => _ = new JsonSchemaOutputFormat(Parse(schema), "fields");

        act.Should().Throw<ArgumentException>()
            .WithParameterName("schema")
            .WithMessage($"*'{keyword}'*{pointer}*");
    }

    [TestMethod]
    [DataRow("""{"type":"date"}""")]
    [DataRow("""{"type":"Object"}""")]
    [DataRow("""{"type":[]}""")]
    [DataRow("""{"type":["string","string"]}""")]
    [DataRow("""{"type":["string",1]}""")]
    [DataRow("""{"type":1}""")]
    [DataRow("""{"type":null}""")]
    [DataRow("""{"properties":[]}""")]
    [DataRow("""{"properties":{"a":true}}""")]
    [DataRow("""{"properties":{"a":"string"}}""")]
    [DataRow("""{"required":"a"}""")]
    [DataRow("""{"required":["a",1]}""")]
    [DataRow("""{"items":[{"type":"string"}]}""")]
    [DataRow("""{"items":true}""")]
    [DataRow("""{"enum":[]}""")]
    [DataRow("""{"enum":"a"}""")]
    [DataRow("""{"additionalProperties":{"type":"string"}}""")]
    [DataRow("""{"additionalProperties":"false"}""")]
    public void Ctor_MalformedKeywordValue_Throws(string schema)
    {
        Action act = () => _ = new JsonSchemaOutputFormat(Parse(schema), "fields");

        act.Should().Throw<ArgumentException>().WithParameterName("schema");
    }

    [TestMethod]
    [DataRow("""{"enum":[1,1e99999999999999999999]}""", "#/enum")]
    [DataRow("""{"enum":[1e-2147483649]}""", "#/enum")]
    [DataRow("""{"type":"object","properties":{"a":{"enum":[{"b":[1e2147483648]}]}}}""", "#/properties/a/enum")]
    public void Ctor_EnumNumberWithExponentBeyondInt_ThrowsNamingThePointer(string schema, string pointer)
    {
        Action act = () => _ = new JsonSchemaOutputFormat(Parse(schema), "fields");

        act.Should().Throw<ArgumentException>().WithParameterName("schema").WithMessage($"*'enum'*{pointer}*");
    }

    [TestMethod]
    public void Ctor_EnumNumberWithExponentWithinInt_IsAccepted()
    {
        Action act = () => _ = new JsonSchemaOutputFormat(Parse("""{"enum":[1e2147483647,1e-2147483648]}"""), "fields");

        act.Should().NotThrow();
    }

    [TestMethod]
    [DataRow("""{"type":"string","enum":["a",1]}""", "#/enum")]
    [DataRow("""{"type":"integer","enum":[1,1.5]}""", "#/enum")]
    [DataRow("""{"type":"object","properties":{"s":{"type":["string","null"],"enum":["a",true]}}}""", "#/properties/s/enum")]
    public void Ctor_EnumValueTheTypeExcludes_ThrowsNamingThePointer(string schema, string pointer)
    {
        Action act = () => _ = new JsonSchemaOutputFormat(Parse(schema), "fields");

        act.Should().Throw<ArgumentException>().WithParameterName("schema").WithMessage($"*'enum'*{pointer}*");
    }

    [TestMethod]
    public void Ctor_RequiredNameForbiddenByAdditionalPropertiesFalse_ThrowsNamingThePointer()
    {
        const string schema = """{"type":"object","properties":{"lines":{"type":"object","properties":{"a":{}},"required":["a","b"],"additionalProperties":false}}}""";

        Action act = () => _ = new JsonSchemaOutputFormat(Parse(schema), "fields");

        act.Should().Throw<ArgumentException>().WithParameterName("schema").WithMessage("*'required'*#/properties/lines/required*'b'*");
    }

    [TestMethod]
    public void Ctor_RequiredNameUndeclaredWithAdditionalPropertiesAllowed_IsAccepted()
    {
        Action act = () => _ = new JsonSchemaOutputFormat(Parse("""{"type":"object","required":["b"],"additionalProperties":true}"""), "fields");

        act.Should().NotThrow();
    }

    [TestMethod]
    public void Ctor_AnnotationsAndCheckedKeywords_AreAccepted()
    {
        const string schema = """
            {
              "$schema": "https://json-schema.org/draft/2020-12/schema",
              "$comment": "Built at runtime.",
              "title": "Fields",
              "description": "The fields of one document.",
              "type": "object",
              "properties": {
                "kind": { "type": "string", "enum": ["a", "b"], "default": "a", "examples": ["a"] }
              },
              "required": ["kind"],
              "additionalProperties": false
            }
            """;

        Action act = () => _ = new JsonSchemaOutputFormat(Parse(schema), "fields");

        act.Should().NotThrow();
    }

    [TestMethod]
    public void Ctor_PropertyNamedLikeAKeyword_IsAProperty()
    {
        const string schema = """{"type":"object","properties":{"anyOf":{"type":"string"},"$ref":{"type":"string"}}}""";

        Action act = () => _ = new JsonSchemaOutputFormat(Parse(schema), "fields");

        act.Should().NotThrow();
    }

    [TestMethod]
    public void ResponseFormat_CarriesNameCompactSchemaAndStrictByDefault()
    {
        const string schema = """{ "type": "object", "required": [ "a" ] }""";

        var format = new JsonSchemaOutputFormat(Parse(schema), "fields");

        format.ResponseFormat.Should().Be(new JsonSchemaResponseFormat("fields", """{"type":"object","required":["a"]}"""));
        format.ResponseFormat.Strict.Should().BeTrue();
    }

    [TestMethod]
    public void ResponseFormat_StrictFalse_IsCarried()
    {
        var format = new JsonSchemaOutputFormat(new JsonObject(), "fields", strict: false);

        format.ResponseFormat.Strict.Should().BeFalse();
    }

    [TestMethod]
    public void ResponseFormat_SchemaMutatedAfterConstruction_IsUnaffected()
    {
        var schema = new JsonObject { ["type"] = "object", ["required"] = new JsonArray("a") };
        var format = new JsonSchemaOutputFormat(schema, "fields");

        schema["type"] = "array";
        schema.Remove("required");

        format.ResponseFormat.Schema.Should().Be("""{"type":"object","required":["a"]}""");
    }

    [TestMethod]
    public async Task ParseAsync_SchemaMutatedAfterConstruction_ValidatesAgainstTheOriginal()
    {
        var schema = new JsonObject { ["type"] = "object", ["required"] = new JsonArray("a") };
        var format = new JsonSchemaOutputFormat(schema, "fields");
        schema.Remove("required");

        Func<Task> act = () => format.ParseAsync<JsonNode>("{}");

        (await act.Should().ThrowAsync<OutputFormatException>()).Which.Violation.Should().Be(OutputFormatViolation.MissingRequired);
    }

    [TestMethod]
    public void Ctor_SchemaStillAttachedToAParent_IsAccepted()
    {
        var parent = new JsonObject { ["schema"] = new JsonObject { ["type"] = "string" } };
        JsonNode schema = parent["schema"] ?? throw new AssertFailedException("No schema.");

        var format = new JsonSchemaOutputFormat(schema, "fields");

        format.ResponseFormat.Schema.Should().Be("""{"type":"string"}""");
        parent["schema"].Should().BeSameAs(schema);
    }

    [TestMethod]
    public void ResponseFormat_NonAsciiAnnotation_IsKeptReadable()
    {
        var schema = new JsonObject { ["type"] = "string", ["description"] = "Fälligkeitsdatum" };

        var format = new JsonSchemaOutputFormat(schema, "fields");

        format.ResponseFormat.Schema.Should().Be("""{"type":"string","description":"Fälligkeitsdatum"}""");
    }

    private static JsonNode Parse(string json) => JsonNode.Parse(json) ?? throw new AssertFailedException("The test schema is null.");

    // The constructor rejects null; a test still has to pass one without the forbidden '!'.
    private static JsonNode NullNode() => (new JsonNode[1])[0];
}
