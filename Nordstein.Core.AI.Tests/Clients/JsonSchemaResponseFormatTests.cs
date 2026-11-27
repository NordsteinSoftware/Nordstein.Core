using System.Text.Json.Nodes;
using AwesomeAssertions;
using Nordstein.Core.AI.Clients;

namespace Nordstein.Core.AI.Tests.Clients;

/// <summary>
/// Exercises <see cref="JsonSchemaResponseFormat"/>: the constructor guards on name and schema, the
/// compact schema text, value equality, and the OpenAI-compatible <c>response_format</c> member it
/// renders.
/// </summary>
[TestClass]
public sealed class JsonSchemaResponseFormatTests
{
    private const string Schema = """{"type":"object","properties":{"total":{"type":"number"}},"required":["total"]}""";

    [TestMethod]
    public void Ctor_ValidArguments_ExposesNameSchemaAndStrict()
    {
        var format = new JsonSchemaResponseFormat("invoice_total", Schema, strict: false);

        format.Name.Should().Be("invoice_total");
        format.Schema.Should().Be(Schema);
        format.Strict.Should().BeFalse();
    }

    [TestMethod]
    public void Ctor_WithoutStrict_DefaultsToStrict()
    {
        var format = new JsonSchemaResponseFormat("invoice_total", Schema);

        format.Strict.Should().BeTrue();
    }

    [TestMethod]
    public void Ctor_IndentedSchema_StoresCompactText()
    {
        const string indented = """
            {
              "type": "object",
              "properties": { "total": { "type": "number" } },
              "required": [ "total" ]
            }
            """;

        var format = new JsonSchemaResponseFormat("invoice_total", indented);

        format.Schema.Should().Be(Schema);
    }

    [TestMethod]
    public void Ctor_NullName_Throws()
    {
        Action act = () => _ = new JsonSchemaResponseFormat(NullString(), Schema);

        act.Should().Throw<ArgumentNullException>().WithParameterName("name");
    }

    [TestMethod]
    [DataRow("")]
    [DataRow(" ")]
    [DataRow("has space")]
    [DataRow("dotted.name")]
    [DataRow("umlaut_ä")]
    [DataRow("a2345678901234567890123456789012345678901234567890123456789012345")]
    public void Ctor_NameOutsideTheWireAlphabet_Throws(string name)
    {
        Action act = () => _ = new JsonSchemaResponseFormat(name, Schema);

        act.Should().Throw<ArgumentException>().WithParameterName("name");
    }

    [TestMethod]
    [DataRow("a")]
    [DataRow("Invoice-Fields_2")]
    [DataRow("a234567890123456789012345678901234567890123456789012345678901234")]
    public void Ctor_NameInsideTheWireAlphabet_IsAccepted(string name)
    {
        var format = new JsonSchemaResponseFormat(name, Schema);

        format.Name.Should().Be(name);
    }

    [TestMethod]
    public void Ctor_NullSchema_Throws()
    {
        Action act = () => _ = new JsonSchemaResponseFormat("fields", NullString());

        act.Should().Throw<ArgumentNullException>().WithParameterName("schema");
    }

    [TestMethod]
    [DataRow("")]
    [DataRow("{")]
    [DataRow("{\"type\":\"object\"} trailing")]
    [DataRow("[]")]
    [DataRow("true")]
    [DataRow("\"object\"")]
    [DataRow("{\"type\":\"object\",\"type\":\"array\"}")]
    public void Ctor_SchemaNotOneJsonObject_Throws(string schema)
    {
        Action act = () => _ = new JsonSchemaResponseFormat("fields", schema);

        act.Should().Throw<ArgumentException>().WithParameterName("schema");
    }

    [TestMethod]
    public void Ctor_NonAsciiText_IsKeptUnescaped()
    {
        const string schema = """{"type":"object","description":"Größe in m², <b>"}""";

        var format = new JsonSchemaResponseFormat("fields", schema);

        format.Schema.Should().Be(schema);
    }

    [TestMethod]
    public void Ctor_SchemaEscapingAnUnpairedSurrogate_Throws()
    {
        Action act = () => _ = new JsonSchemaResponseFormat("fields", """{"description":"\uD800"}""");

        act.Should().Throw<ArgumentException>().WithParameterName("schema");
    }

    [TestMethod]
    public void ToOpenAiCompatibleJson_Strict_RendersJsonSchemaMember()
    {
        var format = new JsonSchemaResponseFormat("invoice_total", Schema);

        string json = format.ToOpenAiCompatibleJson().ToJsonString();

        json.Should().Be(
            """{"type":"json_schema","json_schema":{"name":"invoice_total","schema":""" + Schema + ""","strict":true}}""");
    }

    [TestMethod]
    public void ToOpenAiCompatibleJson_NotStrict_RendersStrictFalse()
    {
        var format = new JsonSchemaResponseFormat("invoice_total", Schema, strict: false);

        JsonObject json = format.ToOpenAiCompatibleJson();

        json["json_schema"]?["strict"]?.GetValue<bool>().Should().BeFalse();
    }

    [TestMethod]
    public void ToOpenAiCompatibleJson_ResultMutated_NextCallUnaffected()
    {
        var format = new JsonSchemaResponseFormat("invoice_total", Schema);
        JsonObject first = format.ToOpenAiCompatibleJson();
        first["type"] = "text";
        first["json_schema"]?["schema"]?.AsObject().Remove("required");

        string second = format.ToOpenAiCompatibleJson().ToJsonString();

        second.Should().Contain("\"type\":\"json_schema\"").And.Contain("\"required\":[\"total\"]");
    }

    [TestMethod]
    public void Equality_SameNameSchemaAndStrict_AreEqual()
    {
        var a = new JsonSchemaResponseFormat("fields", Schema);
        var b = new JsonSchemaResponseFormat("fields", Schema);

        a.Should().Be(b);
        a.GetHashCode().Should().Be(b.GetHashCode());
    }

    [TestMethod]
    [DataRow("other", Schema, true)]
    [DataRow("fields", """{"type":"object"}""", true)]
    [DataRow("fields", Schema, false)]
    public void Equality_AnyMemberDiffers_AreNotEqual(string name, string schema, bool strict)
    {
        var a = new JsonSchemaResponseFormat("fields", Schema);

        a.Should().NotBe(new JsonSchemaResponseFormat(name, schema, strict));
    }

    [TestMethod]
    public void JsonSchemaResponseFormat_IsAResponseFormat()
    {
        ResponseFormat format = new JsonSchemaResponseFormat("fields", Schema);

        format.Should().BeOfType<JsonSchemaResponseFormat>();
    }

    // The constructors reject null; a test still has to pass one without the forbidden '!'.
    private static string NullString() => (new string[1])[0];
}
