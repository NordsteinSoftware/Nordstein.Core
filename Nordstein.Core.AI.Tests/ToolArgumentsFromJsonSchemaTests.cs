using System.Text.Json;
using AwesomeAssertions;
using Nordstein.Core.AI.Tools;
using NSubstitute;

namespace Nordstein.Core.AI.Tests;

[TestClass]
public sealed class ToolArgumentsFromJsonSchemaTests
{
    [TestMethod]
    public void FromJsonSchema_String_WithoutProperties_ReturnsNone()
    {
        ToolArguments result = ToolArguments.FromJsonSchema("""{"type":"object"}""");

        result.Should().BeSameAs(ToolArguments.None);
    }

    [TestMethod]
    public void FromJsonSchema_JsonElement_WithoutProperties_ReturnsNone()
    {
        using JsonDocument doc = JsonDocument.Parse("""{"type":"object"}""");

        ToolArguments result = ToolArguments.FromJsonSchema(doc.RootElement);

        result.Should().BeSameAs(ToolArguments.None);
    }

    [TestMethod]
    public void FromJsonSchema_WithRequiredArray_MarksListedArgumentsRequired()
    {
        ToolArguments result = ToolArguments.FromJsonSchema("""
            {
              "type": "object",
              "properties": {
                "a": {"type":"string"},
                "b": {"type":"string"}
              },
              "required": ["a"]
            }
            """);

        result.Count.Should().Be(2);
        IReadOnlyDictionary<string, IToolArgument> byName = result.ToDictionary(argument => argument.Name);
        byName["a"].IsRequired.Should().BeTrue();
        byName["b"].IsRequired.Should().BeFalse();
    }

    [TestMethod]
    public void FromJsonSchema_WithNonArrayRequired_TreatsAllArgumentsAsOptional()
    {
        // A "required" value that is not an array is ignored entirely.
        ToolArguments result = ToolArguments.FromJsonSchema("""
            {
              "type": "object",
              "properties": { "a": {"type":"string"} },
              "required": "a"
            }
            """);

        result.Single().IsRequired.Should().BeFalse();
    }

    [TestMethod]
    public void FromJsonSchema_WithAbsentRequired_TreatsAllArgumentsAsOptional()
    {
        ToolArguments result = ToolArguments.FromJsonSchema("""
            {
              "type": "object",
              "properties": { "a": {"type":"string"} }
            }
            """);

        result.Single().IsRequired.Should().BeFalse();
    }

    [TestMethod]
    public void FromJsonSchema_WithNonStringRequiredItems_SkipsThem()
    {
        // Only the string entries in the required array are honoured; numbers/booleans/nulls are dropped.
        ToolArguments result = ToolArguments.FromJsonSchema("""
            {
              "type": "object",
              "properties": {
                "a": {"type":"string"},
                "b": {"type":"string"}
              },
              "required": ["a", 123, true, null]
            }
            """);

        IReadOnlyDictionary<string, IToolArgument> byName = result.ToDictionary(argument => argument.Name);
        byName["a"].IsRequired.Should().BeTrue();
        byName["b"].IsRequired.Should().BeFalse();
    }

    [TestMethod]
    public void FromJsonSchema_WithDescription_PicksItUp()
    {
        ToolArguments result = ToolArguments.FromJsonSchema("""
            {
              "type": "object",
              "properties": {
                "city": {"type":"string","description":"The city to look up"},
                "limit": {"type":"integer"}
              }
            }
            """);

        IReadOnlyDictionary<string, IToolArgument> byName = result.ToDictionary(argument => argument.Name);
        byName["city"].Description.Should().Be("The city to look up");
        byName["limit"].Description.Should().BeNull();
    }

    [TestMethod]
    public void FromJsonSchema_RoundTripsToJsonSchema_PreservingNamesRequirednessAndDescription()
    {
        IToolArgument city = Arg("city", typeof(string), required: true,
            jsonSchema: """{"type":"string","description":"The city"}""");
        IToolArgument limit = Arg("limit", typeof(int), required: false, jsonSchema: """{"type":"integer"}""");
        var original = new ToolArguments([city, limit]);

        ToolArguments parsed = ToolArguments.FromJsonSchema(original.JsonSchema);

        parsed.Count.Should().Be(2);
        IReadOnlyDictionary<string, IToolArgument> byName = parsed.ToDictionary(argument => argument.Name);
        byName.Keys.Should().BeEquivalentTo("city", "limit");
        byName["city"].IsRequired.Should().BeTrue();
        byName["city"].Description.Should().Be("The city");
        byName["city"].Type.Should().Be(typeof(object));
        byName["limit"].IsRequired.Should().BeFalse();
    }

    private static IToolArgument Arg(string name, Type type, bool required, string jsonSchema)
    {
        var arg = Substitute.For<IToolArgument>();
        arg.Name.Returns(name);
        arg.Type.Returns(type);
        arg.IsRequired.Returns(required);
        arg.JsonSchema.Returns(jsonSchema);
        arg.Description.Returns((string?)null);
        return arg;
    }
}
