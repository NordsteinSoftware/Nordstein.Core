using System.ComponentModel.DataAnnotations;
using System.Text.Json.Nodes;
using AwesomeAssertions;
using Nordstein.Core.AI.Clients;
using Nordstein.Core.AI.Serialization;
using Nordstein.Core.Testing;

namespace Nordstein.Core.AI.Tests.Serialization;

/// <summary>
/// Exercises the strict parse of <see cref="JsonSchemaOutputFormat"/>: a reply is one complete JSON
/// document that validates against the schema's <c>type</c>, <c>required</c>, <c>enum</c>,
/// <c>properties</c>, <c>items</c> and <c>additionalProperties</c>, or it is an
/// <see cref="OutputFormatException"/> naming the JSON path and the violation, never the reply.
/// </summary>
[TestClass]
public sealed class JsonSchemaOutputFormatTests : BaseTest<Nordstein.Core.AI.Module>
{
    private const string OrderSchema = """
        {
          "type": "object",
          "properties": {
            "orderId": { "type": "string", "description": "The order number as printed." },
            "status": { "type": "string", "enum": ["open", "paid"] },
            "total": { "type": "number" },
            "note": { "type": ["string", "null"] },
            "lines": {
              "type": "array",
              "items": {
                "type": "object",
                "properties": {
                  "sku": { "type": "string" },
                  "quantity": { "type": "integer" }
                },
                "required": ["sku", "quantity"],
                "additionalProperties": false
              }
            }
          },
          "required": ["orderId", "status", "total", "note", "lines"],
          "additionalProperties": false
        }
        """;

    private const string ValidOrder = """
        {"orderId":"A-17","status":"paid","total":12.5,"note":null,"lines":[{"sku":"X1","quantity":2},{"sku":"X2","quantity":1}]}
        """;

    [TestMethod]
    public async Task JsonSchemaOutputFormat_ValidReply_Parses()
    {
        JsonSchemaOutputFormat format = Format(OrderSchema);

        JsonNode? result = await format.ParseAsync<JsonNode>(ValidOrder, CancellationToken);

        JsonNode.DeepEquals(result, JsonNode.Parse(ValidOrder)).Should().BeTrue();
    }

    [TestMethod]
    public async Task JsonSchemaOutputFormat_ValidReply_ReturnsAnIndependentMutableNode()
    {
        JsonSchemaOutputFormat format = Format(OrderSchema);

        JsonNode? result = await format.ParseAsync<JsonNode>(ValidOrder, CancellationToken);
        JsonObject order = result?.AsObject() ?? throw new AssertFailedException("No object parsed.");
        order["status"] = "open";

        order["status"]?.GetValue<string>().Should().Be("open");
        order["lines"]?[1]?["sku"]?.GetValue<string>().Should().Be("X2");
    }

    [TestMethod]
    [DataRow("{\"orderId\":\"A-17\",\"status\":\"pa")]
    [DataRow("{\"orderId\":\"A-17\",\"status\":\"paid\",\"total\":12.5,\"note\":null,\"lines\":[{\"sku\":\"X1\",\"quantity\":2}")]
    [DataRow("{\"orderId\":\"A-17\",")]
    [DataRow("{")]
    [DataRow("[1, 2")]
    [DataRow("tru")]
    [DataRow("```json\n{\"orderId\":\"A-17\"")]
    public async Task JsonSchemaOutputFormat_TruncatedReply_Throws(string reply)
    {
        JsonSchemaOutputFormat format = Format(OrderSchema);

        Func<Task> act = () => format.ParseAsync<JsonNode>(reply, CancellationToken);

        OutputFormatException exception = (await act.Should().ThrowAsync<OutputFormatException>()).Which;
        exception.Violation.Should().Be(OutputFormatViolation.Truncated);
        exception.Path.Should().Be("$");
    }

    [TestMethod]
    [DataRow("hello")]
    [DataRow("{\"orderId\":\"A-17\"} trailing")]
    [DataRow("{\"orderId\":\"A-17\"}}")]
    [DataRow("{\"orderId\":\"A-17\",}")]
    [DataRow("{\"orderId\":'A-17'}")]
    [DataRow("{\"orderId\":\"A-17\" /* comment */}")]
    [DataRow("{\"a\":1} {\"a\":2}")]
    [DataRow("json: {\"orderId\":\"A-17\"}")]
    public async Task JsonSchemaOutputFormat_MalformedReply_Throws(string reply)
    {
        JsonSchemaOutputFormat format = Format(OrderSchema);

        Func<Task> act = () => format.ParseAsync<JsonNode>(reply, CancellationToken);

        OutputFormatException exception = (await act.Should().ThrowAsync<OutputFormatException>()).Which;
        exception.Violation.Should().Be(OutputFormatViolation.Malformed);
        exception.Path.Should().Be("$");
    }

    [TestMethod]
    [DataRow("```[1]\n[[2]]\n```")]
    [DataRow("```{\"total\":1}\n{\"total\":2}\n```")]
    [DataRow("```\"x\"\n\"y\"\n```")]
    [DataRow("```1\n2\n```")]
    [DataRow("```true\nfalse\n```")]
    [DataRow("```null\n{}\n```")]
    [DataRow("```json {\"total\":1}\n{\"total\":2}\n```")]
    public async Task JsonSchemaOutputFormat_FenceOpeningLineCarriesMoreThanALanguageTag_ThrowsMalformed(string reply)
    {
        JsonSchemaOutputFormat format = Format("{}");

        Func<Task> act = () => format.ParseAsync<JsonNode>(reply, CancellationToken);

        OutputFormatException exception = (await act.Should().ThrowAsync<OutputFormatException>()).Which;
        exception.Violation.Should().Be(OutputFormatViolation.Malformed);
        exception.Path.Should().Be("$");
    }

    [TestMethod]
    public async Task JsonSchemaOutputFormat_DuplicateProperty_ThrowsMalformedWithoutTheName()
    {
        JsonSchemaOutputFormat format = Format("""{"type":"object"}""");

        Func<Task> act = () => format.ParseAsync<JsonNode>("""{"secretName":1,"secretName":2}""", CancellationToken);

        OutputFormatException exception = (await act.Should().ThrowAsync<OutputFormatException>()).Which;
        exception.Violation.Should().Be(OutputFormatViolation.Malformed);
        exception.Message.Should().NotContain("secretName");
        exception.InnerException.Should().BeNull();
    }

    [TestMethod]
    [DataRow("\"\\uD800\"")]
    [DataRow("{\"\\uDC00\":1}")]
    [DataRow("[\"ok\",\"\\uD83D x\"]")]
    public async Task JsonSchemaOutputFormat_EscapedLoneSurrogate_ThrowsMalformed(string reply)
    {
        JsonSchemaOutputFormat format = Format("{}");

        Func<Task> act = () => format.ParseAsync<JsonNode>(reply, CancellationToken);

        OutputFormatException exception = (await act.Should().ThrowAsync<OutputFormatException>()).Which;
        exception.Violation.Should().Be(OutputFormatViolation.Malformed);
    }

    [TestMethod]
    public async Task JsonSchemaOutputFormat_UnpairedSurrogateCharacter_ThrowsMalformed()
    {
        JsonSchemaOutputFormat format = Format("{}");
        string reply = "\"a" + '\uD800' + "\"";

        Func<Task> act = () => format.ParseAsync<JsonNode>(reply, CancellationToken);

        (await act.Should().ThrowAsync<OutputFormatException>()).Which.Violation.Should().Be(OutputFormatViolation.Malformed);
    }

    [TestMethod]
    public async Task JsonSchemaOutputFormat_NestedDeeperThan64_ThrowsMalformed()
    {
        JsonSchemaOutputFormat format = Format("{}");
        string reply = new string('[', 65) + new string(']', 65);

        Func<Task> act = () => format.ParseAsync<JsonNode>(reply, CancellationToken);

        (await act.Should().ThrowAsync<OutputFormatException>()).Which.Violation.Should().Be(OutputFormatViolation.Malformed);
    }

    [TestMethod]
    public async Task JsonSchemaOutputFormat_Nested64Deep_Parses()
    {
        JsonSchemaOutputFormat format = Format("{}");
        string reply = new string('[', 64) + new string(']', 64);

        JsonNode? result = await format.ParseAsync<JsonNode>(reply, CancellationToken);

        result.Should().BeOfType<JsonArray>();
    }

    [TestMethod]
    [DataRow(null)]
    [DataRow("")]
    [DataRow("  \n\t ")]
    [DataRow("```json\n```")]
    public async Task JsonSchemaOutputFormat_EmptyReply_Throws(string? reply)
    {
        JsonSchemaOutputFormat format = Format(OrderSchema);

        Func<Task> act = () => format.ParseAsync<JsonNode>(reply, CancellationToken);

        OutputFormatException exception = (await act.Should().ThrowAsync<OutputFormatException>()).Which;
        exception.Violation.Should().Be(OutputFormatViolation.Empty);
        exception.Path.Should().Be("$");
    }

    [TestMethod]
    [DataRow("```json\n{0}\n```")]
    [DataRow("```\n{0}\n```")]
    [DataRow("  \n{0}\n  ")]
    [DataRow("```JSON\n{0}\n```")]
    [DataRow("``` json \n{0}\n```")]
    [DataRow("```json\r\n{0}\r\n```")]
    [DataRow("```json5\n{0}\n```")]
    [DataRow("```{0}```")]
    public async Task JsonSchemaOutputFormat_WrappedReply_Parses(string wrapper)
    {
        JsonSchemaOutputFormat format = Format(OrderSchema);
        string reply = wrapper.Replace("{0}", ValidOrder.Trim(), StringComparison.Ordinal);

        JsonNode? result = await format.ParseAsync<JsonNode>(reply, CancellationToken);

        JsonNode.DeepEquals(result, JsonNode.Parse(ValidOrder)).Should().BeTrue();
    }

    [TestMethod]
    public async Task JsonSchemaOutputFormat_MissingRequired_Throws()
    {
        JsonSchemaOutputFormat format = Format(OrderSchema);
        const string reply = """{"orderId":"A-17","status":"paid","note":null,"lines":[]}""";

        Func<Task> act = () => format.ParseAsync<JsonNode>(reply, CancellationToken);

        OutputFormatException exception = (await act.Should().ThrowAsync<OutputFormatException>()).Which;
        exception.Violation.Should().Be(OutputFormatViolation.MissingRequired);
        exception.Path.Should().Be("$.total");
    }

    [TestMethod]
    public async Task JsonSchemaOutputFormat_MissingRequiredInNestedArrayItem_NamesItemPath()
    {
        JsonSchemaOutputFormat format = Format(OrderSchema);
        const string reply = """{"orderId":"A-17","status":"paid","total":1,"note":null,"lines":[{"sku":"X1","quantity":2},{"sku":"X2"}]}""";

        Func<Task> act = () => format.ParseAsync<JsonNode>(reply, CancellationToken);

        OutputFormatException exception = (await act.Should().ThrowAsync<OutputFormatException>()).Which;
        exception.Violation.Should().Be(OutputFormatViolation.MissingRequired);
        exception.Path.Should().Be("$.lines[1].quantity");
    }

    [TestMethod]
    public async Task JsonSchemaOutputFormat_RequiredPropertyNotDeclared_StillRequired()
    {
        JsonSchemaOutputFormat format = Format("""{"type":"object","required":["id"]}""");

        Func<Task> act = () => format.ParseAsync<JsonNode>("""{"other":1}""", CancellationToken);

        (await act.Should().ThrowAsync<OutputFormatException>()).Which.Path.Should().Be("$.id");
    }

    [TestMethod]
    public async Task JsonSchemaOutputFormat_OptionalPropertyAbsent_Parses()
    {
        JsonSchemaOutputFormat format = Format("""{"type":"object","properties":{"id":{"type":"string"},"note":{"type":"string"}},"required":["id"]}""");

        JsonNode? result = await format.ParseAsync<JsonNode>("""{"id":"a"}""", CancellationToken);

        result?["id"]?.GetValue<string>().Should().Be("a");
    }

    [TestMethod]
    public async Task JsonSchemaOutputFormat_WrongType_Throws()
    {
        JsonSchemaOutputFormat format = Format(OrderSchema);
        const string reply = """{"orderId":"A-17","status":"paid","total":"12.50","note":null,"lines":[]}""";

        Func<Task> act = () => format.ParseAsync<JsonNode>(reply, CancellationToken);

        OutputFormatException exception = (await act.Should().ThrowAsync<OutputFormatException>()).Which;
        exception.Violation.Should().Be(OutputFormatViolation.WrongType);
        exception.Path.Should().Be("$.total");
    }

    [TestMethod]
    public async Task JsonSchemaOutputFormat_WrongTypeInNestedArrayItem_NamesItemPath()
    {
        JsonSchemaOutputFormat format = Format(OrderSchema);
        const string reply = """{"orderId":"A-17","status":"paid","total":1,"note":null,"lines":[{"sku":"X1","quantity":2},{"sku":"X2","quantity":1.5}]}""";

        Func<Task> act = () => format.ParseAsync<JsonNode>(reply, CancellationToken);

        OutputFormatException exception = (await act.Should().ThrowAsync<OutputFormatException>()).Which;
        exception.Violation.Should().Be(OutputFormatViolation.WrongType);
        exception.Path.Should().Be("$.lines[1].quantity");
    }

    [TestMethod]
    [DataRow("\"object\"", "{}", true)]
    [DataRow("\"object\"", "[]", false)]
    [DataRow("\"object\"", "null", false)]
    [DataRow("\"array\"", "[]", true)]
    [DataRow("\"array\"", "{}", false)]
    [DataRow("\"string\"", "\"x\"", true)]
    [DataRow("\"string\"", "1", false)]
    [DataRow("\"boolean\"", "false", true)]
    [DataRow("\"boolean\"", "\"true\"", false)]
    [DataRow("\"boolean\"", "0", false)]
    [DataRow("\"null\"", "null", true)]
    [DataRow("\"null\"", "\"null\"", false)]
    [DataRow("\"number\"", "1", true)]
    [DataRow("\"number\"", "-1.5e-3", true)]
    [DataRow("\"number\"", "\"1\"", false)]
    [DataRow("[\"string\",\"null\"]", "null", true)]
    [DataRow("[\"string\",\"null\"]", "\"x\"", true)]
    [DataRow("[\"string\",\"null\"]", "1", false)]
    [DataRow("[\"integer\",\"string\"]", "\"x\"", true)]
    [DataRow("[\"integer\",\"string\"]", "2", true)]
    [DataRow("[\"integer\",\"string\"]", "2.5", false)]
    public async Task JsonSchemaOutputFormat_TypeKeyword_AcceptsOnlyTheNamedTypes(string type, string value, bool accepted)
    {
        JsonSchemaOutputFormat format = Format($$$"""{"type":"object","properties":{"v":{"type":{{{type}}}}},"required":["v"]}""");

        Func<Task> act = () => format.ParseAsync<JsonNode>($$"""{"v":{{value}}}""", CancellationToken);

        if (accepted)
        {
            await act.Should().NotThrowAsync();
        }
        else
        {
            (await act.Should().ThrowAsync<OutputFormatException>()).Which.Violation.Should().Be(OutputFormatViolation.WrongType);
        }
    }

    [TestMethod]
    [DataRow("1", true)]
    [DataRow("-7", true)]
    [DataRow("0", true)]
    [DataRow("-0", true)]
    [DataRow("1.0", true)]
    [DataRow("0.000", true)]
    [DataRow("1e2", true)]
    [DataRow("1E+2", true)]
    [DataRow("100e-2", true)]
    [DataRow("1.50e1", true)]
    [DataRow("0e-5", true)]
    [DataRow("123456789012345678901234567890", true)]
    [DataRow("1e99999999999999999999", true)]
    [DataRow("1.5e9223372036854775807", true)]
    [DataRow("1e-9223372036854775808", false)]
    [DataRow("1e-9223372036854775809", false)]
    [DataRow("0.0e-9223372036854775809", true)]
    [DataRow("1.5", false)]
    [DataRow("-0.1", false)]
    [DataRow("1e-1", false)]
    [DataRow("1.55e1", false)]
    [DataRow("10e-2", false)]
    [DataRow("0.00000000000000000000000000001", false)]
    [DataRow("1e-99999999999999999999", false)]
    public async Task JsonSchemaOutputFormat_IntegerType_AcceptsOnlyNumbersWithoutFraction(string number, bool accepted)
    {
        JsonSchemaOutputFormat format = Format("""{"type":"integer"}""");

        Func<Task> act = () => format.ParseAsync<JsonNode>(number, CancellationToken);

        if (accepted)
        {
            await act.Should().NotThrowAsync();
        }
        else
        {
            (await act.Should().ThrowAsync<OutputFormatException>()).Which.Violation.Should().Be(OutputFormatViolation.WrongType);
        }
    }

    [TestMethod]
    public async Task JsonSchemaOutputFormat_UnknownEnum_Throws()
    {
        JsonSchemaOutputFormat format = Format(OrderSchema);
        const string reply = """{"orderId":"A-17","status":"cancelled","total":1,"note":null,"lines":[]}""";

        Func<Task> act = () => format.ParseAsync<JsonNode>(reply, CancellationToken);

        OutputFormatException exception = (await act.Should().ThrowAsync<OutputFormatException>()).Which;
        exception.Violation.Should().Be(OutputFormatViolation.NotInEnum);
        exception.Path.Should().Be("$.status");
    }

    [TestMethod]
    [DataRow("1", true)]
    [DataRow("1.0", true)]
    [DataRow("10e-1", true)]
    [DataRow("\"1\"", false)]
    [DataRow("\"one\"", true)]
    [DataRow("\"One\"", false)]
    [DataRow("null", true)]
    [DataRow("true", true)]
    [DataRow("false", false)]
    [DataRow("{\"b\":[1,2],\"a\":1}", true)]
    [DataRow("{\"a\":1,\"b\":[2,1]}", false)]
    [DataRow("[\"x\"]", true)]
    [DataRow("[]", false)]
    [DataRow("1e99999999999999999999", false)]
    [DataRow("1e-99999999999999999999", false)]
    [DataRow("1e2147483648", false)]
    [DataRow("0e99999999999999999999", false)]
    [DataRow("{\"a\":1e99999999999999999999,\"b\":[1,2]}", false)]
    [DataRow("-1", false)]
    public async Task JsonSchemaOutputFormat_EnumOfMixedTypes_MatchesByJsonValue(string value, bool accepted)
    {
        JsonSchemaOutputFormat format = Format("""{"enum":[1,"one",null,true,{"a":1,"b":[1,2]},["x"]]}""");

        Func<Task> act = () => format.ParseAsync<JsonNode>(value, CancellationToken);

        if (accepted)
        {
            await act.Should().NotThrowAsync();
        }
        else
        {
            (await act.Should().ThrowAsync<OutputFormatException>()).Which.Violation.Should().Be(OutputFormatViolation.NotInEnum);
        }
    }

    [TestMethod]
    [DataRow("1e99999999999999999999")]
    [DataRow("1e-99999999999999999999")]
    public async Task JsonSchemaOutputFormat_EnumAgainstNumberWithHugeExponent_ThrowsNotInEnum(string reply)
    {
        JsonSchemaOutputFormat format = Format("""{"enum":[1,"a"]}""");

        Func<Task> act = () => format.ParseAsync<JsonNode>(reply, CancellationToken);

        (await act.Should().ThrowAsync<OutputFormatException>()).Which.Violation.Should().Be(OutputFormatViolation.NotInEnum);
    }

    [TestMethod]
    [DataRow("0", "0.0")]
    [DataRow("0", "-0e5")]
    [DataRow("120", "1.2e2")]
    [DataRow("120", "12000e-2")]
    [DataRow("-0.5", "-5e-1")]
    [DataRow("1e2147483647", "10e2147483646")]
    public async Task JsonSchemaOutputFormat_EnumNumber_MatchesEveryLiteralOfTheSameValue(string enumValue, string reply)
    {
        JsonSchemaOutputFormat format = Format($$"""{"enum":[{{enumValue}}]}""");

        Func<Task> act = () => format.ParseAsync<JsonNode>(reply, CancellationToken);

        await act.Should().NotThrowAsync();
    }

    [TestMethod]
    public async Task JsonSchemaOutputFormat_EnumWithNullableType_AcceptsNull()
    {
        JsonSchemaOutputFormat format = Format("""{"type":["string","null"],"enum":["open","paid",null]}""");

        JsonNode? result = await format.ParseAsync<JsonNode>("null", CancellationToken);

        result.Should().BeNull();
    }

    [TestMethod]
    public async Task JsonSchemaOutputFormat_UndeclaredPropertyWithAdditionalPropertiesFalse_ThrowsAtObjectPath()
    {
        JsonSchemaOutputFormat format = Format(OrderSchema);
        const string reply = """{"orderId":"A-17","status":"paid","total":1,"note":null,"lines":[{"sku":"X1","quantity":2,"secretExtra":"leak"}]}""";

        Func<Task> act = () => format.ParseAsync<JsonNode>(reply, CancellationToken);

        OutputFormatException exception = (await act.Should().ThrowAsync<OutputFormatException>()).Which;
        exception.Violation.Should().Be(OutputFormatViolation.UnexpectedProperty);
        exception.Path.Should().Be("$.lines[0]");
        exception.Message.Should().NotContain("secretExtra").And.NotContain("leak");
    }

    [TestMethod]
    [DataRow("""{"type":"object","properties":{"id":{"type":"string"}}}""")]
    [DataRow("""{"type":"object","properties":{"id":{"type":"string"}},"additionalProperties":true}""")]
    public async Task JsonSchemaOutputFormat_UndeclaredPropertyAllowed_IsKeptUnvalidated(string schema)
    {
        JsonSchemaOutputFormat format = Format(schema);

        JsonNode? result = await format.ParseAsync<JsonNode>("""{"id":"a","extra":[1,{"x":null}]}""", CancellationToken);

        result?["extra"]?[1]?.AsObject().ContainsKey("x").Should().BeTrue();
    }

    [TestMethod]
    public async Task JsonSchemaOutputFormat_PropertiesOnNonObject_AreNotApplied()
    {
        JsonSchemaOutputFormat format = Format("""{"properties":{"id":{"type":"string"}},"required":["id"],"items":{"type":"string"}}""");

        JsonNode? result = await format.ParseAsync<JsonNode>("42", CancellationToken);

        result?.GetValue<int>().Should().Be(42);
    }

    [TestMethod]
    public async Task JsonSchemaOutputFormat_ArrayWithoutItems_AcceptsAnyElements()
    {
        JsonSchemaOutputFormat format = Format("""{"type":"array"}""");

        JsonNode? result = await format.ParseAsync<JsonNode>("""[1,"x",null,{"a":[]}]""", CancellationToken);

        result?.AsArray().Count.Should().Be(4);
    }

    [TestMethod]
    public async Task JsonSchemaOutputFormat_NestedArraysOfObjects_ValidatesEveryLevel()
    {
        JsonSchemaOutputFormat format = Format("""
            {"type":"array","items":{"type":"object","properties":{"rows":{"type":"array","items":{"type":"array","items":{"type":"object","properties":{"v":{"type":"integer"}},"required":["v"]}}}},"required":["rows"]}}
            """);
        const string reply = """[{"rows":[[{"v":1},{"v":2}]]},{"rows":[[{"v":3}],[{"v":4},{"v":"5"}]]}]""";

        Func<Task> act = () => format.ParseAsync<JsonNode>(reply, CancellationToken);

        OutputFormatException exception = (await act.Should().ThrowAsync<OutputFormatException>()).Which;
        exception.Violation.Should().Be(OutputFormatViolation.WrongType);
        exception.Path.Should().Be("$[1].rows[1][1].v");
    }

    [TestMethod]
    public async Task JsonSchemaOutputFormat_EmptyObjectSchema_AcceptsAnyDocument()
    {
        JsonSchemaOutputFormat format = Format("{}");

        JsonNode? result = await format.ParseAsync<JsonNode>("""["x",1,{"y":true}]""", CancellationToken);

        result?.AsArray().Count.Should().Be(3);
    }

    [TestMethod]
    public async Task JsonSchemaOutputFormat_NullReplyAllowedBySchema_ReturnsNull()
    {
        JsonSchemaOutputFormat format = Format("""{"type":["object","null"]}""");

        JsonNode? result = await format.ParseAsync<JsonNode>("null", CancellationToken);

        result.Should().BeNull();
    }

    [TestMethod]
    [DataRow("line items", "$['line items']")]
    [DataRow("it's", "$['it\\'s']")]
    [DataRow("back\\slash", "$['back\\\\slash']")]
    [DataRow("9lives", "$['9lives']")]
    [DataRow("", "$['']")]
    [DataRow("_private2", "$._private2")]
    public async Task JsonSchemaOutputFormat_PropertyNameNotAnIdentifier_UsesBracketNotation(string name, string expectedPath)
    {
        var schema = new JsonObject
        {
            ["type"] = "object",
            ["required"] = new JsonArray(name),
        };
        var format = new JsonSchemaOutputFormat(schema, "names");

        Func<Task> act = () => format.ParseAsync<JsonNode>("{}", CancellationToken);

        (await act.Should().ThrowAsync<OutputFormatException>()).Which.Path.Should().Be(expectedPath);
    }

    [TestMethod]
    public async Task JsonSchemaOutputFormat_ExceptionNamesPathNotContent()
    {
        JsonSchemaOutputFormat format = Format(OrderSchema);
        const string secret = "Confidential-Customer-4711";
        string[] replies =
        [
            $$"""{"orderId":"{{secret}}","status":"{{secret}}","total":1,"note":null,"lines":[]}""",
            $$"""{"orderId":"A-17","status":"paid","total":"{{secret}}","note":null,"lines":[]}""",
            $$"""{"orderId":"{{secret}}","status":"paid","note":null,"lines":[]}""",
            $$"""{"orderId":"{{secret}}","status":"paid","total":1,"note":null,"lines":[],"{{secret}}":1}""",
            $$"""{"orderId":"{{secret}}","status":"pa""",
            $$"""{"orderId":"{{secret}}"} {{secret}}""",
            $$"""{{secret}}""",
            $$"""{"{{secret}}":1,"{{secret}}":2}""",
        ];

        foreach (string reply in replies)
        {
            Func<Task> act = () => format.ParseAsync<JsonNode>(reply, CancellationToken);

            OutputFormatException exception = (await act.Should().ThrowAsync<OutputFormatException>()).Which;
            exception.Message.Should().Contain(exception.Path).And.NotContain(secret).And.NotContain("Confidential");
            exception.InnerException.Should().BeNull();
            exception.ToString().Should().NotContain(secret);
        }
    }

    [TestMethod]
    public async Task JsonSchemaOutputFormat_TargetOtherThanJsonNode_Throws()
    {
        JsonSchemaOutputFormat format = Format(OrderSchema);

        Func<Task> asObject = () => format.ParseAsync<JsonObject>(ValidOrder, CancellationToken);
        Func<Task> asString = () => format.ParseAsync<string>(ValidOrder, CancellationToken);

        await asObject.Should().ThrowAsync<InvalidOperationException>().WithMessage("*JsonNode*");
        await asString.Should().ThrowAsync<InvalidOperationException>().WithMessage("*JsonNode*");
    }

    [TestMethod]
    public async Task JsonSchemaOutputFormat_CancelledToken_ThrowsOperationCanceled()
    {
        JsonSchemaOutputFormat format = Format(OrderSchema);
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        Func<Task> act = () => format.ParseAsync<JsonNode>(ValidOrder, cancellation.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [TestMethod]
    public async Task JsonSchemaOutputFormat_ConcurrentParses_AllSucceedIndependently()
    {
        JsonSchemaOutputFormat format = Format(OrderSchema);
        const string invalid = """{"orderId":"A-17","status":"cancelled","total":1,"note":null,"lines":[]}""";

        Task<JsonNode?>[] valid = [.. Enumerable.Range(0, 64).Select(_ => Task.Run(() => format.ParseAsync<JsonNode>(ValidOrder, CancellationToken), CancellationToken))];
        Task[] rejected = [.. Enumerable.Range(0, 64).Select(_ => Task.Run(() => format.ParseAsync<JsonNode>(invalid, CancellationToken), CancellationToken))];

        JsonNode?[] results = await Task.WhenAll(valid);
        results.Should().AllSatisfy(result => JsonNode.DeepEquals(result, JsonNode.Parse(ValidOrder)).Should().BeTrue());
        foreach (Task task in rejected)
        {
            Func<Task> act = () => task;
            (await act.Should().ThrowAsync<OutputFormatException>()).Which.Violation.Should().Be(OutputFormatViolation.NotInEnum);
        }
    }

    [TestMethod]
    public void ToPromptString_EmbedsTheCompactSchema()
    {
        JsonSchemaOutputFormat format = Format(OrderSchema);

        string? instruction = format.ToPromptString();

        instruction.Should().StartWith("Respond only with one JSON document").And.EndWith(format.ResponseFormat.Schema);
    }

    [TestMethod]
    public void Validate_ReturnsNoErrors()
    {
        JsonSchemaOutputFormat format = Format(OrderSchema);

        format.Validate(new ValidationContext(format)).Should().BeEmpty();
    }

    [TestMethod]
    public void JsonSchemaOutputFormat_IsAnOutputFormat()
    {
        IOutputFormat format = Format(OrderSchema);

        format.Should().BeOfType<JsonSchemaOutputFormat>();
    }

    private static JsonSchemaOutputFormat Format(string schema)
        => new(JsonNode.Parse(schema) ?? throw new AssertFailedException("The test schema is null."), "order");
}
