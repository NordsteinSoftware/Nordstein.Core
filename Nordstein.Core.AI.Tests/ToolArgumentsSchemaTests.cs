using System.Text.Json;
using AwesomeAssertions;
using Nordstein.Core.AI.Tools;
using NSubstitute;

namespace Nordstein.Core.AI.Tests;

[TestClass]
public sealed class ToolArgumentsSchemaTests
{
    [TestMethod]
    public void None_HasNoArgumentsAndEmptyObjectSchema()
    {
        ToolArguments.None.Count.Should().Be(0);
        ToolArguments.None.Arguments.Should().BeEmpty();

        using JsonDocument doc = JsonDocument.Parse(ToolArguments.None.JsonSchema);
        doc.RootElement.GetProperty("type").GetString().Should().Be("object");
        doc.RootElement.GetProperty("properties").EnumerateObject().Should().BeEmpty();
        doc.RootElement.GetProperty("required").EnumerateArray().Should().BeEmpty();
    }

    [TestMethod]
    public void Constructor_WithoutArguments_EqualsNoneContent()
    {
        var empty = new ToolArguments();

        empty.Count.Should().Be(0);
        empty.JsonSchema.Should().Be(ToolArguments.None.JsonSchema);
    }

    [TestMethod]
    public void ToJsonSchema_WithSingleRequiredArgument_MarksItRequiredAndCopiesSchema()
    {
        IToolArgument city = Arg("city", typeof(string), required: true,
            jsonSchema: """{"type":"string","description":"City name"}""");
        var toolArgs = new ToolArguments([city]);

        using JsonDocument doc = JsonDocument.Parse(toolArgs.JsonSchema);
        JsonElement root = doc.RootElement;
        root.GetProperty("type").GetString().Should().Be("object");

        JsonElement properties = root.GetProperty("properties");
        properties.TryGetProperty("city", out JsonElement cityProperty).Should().BeTrue();
        cityProperty.GetProperty("type").GetString().Should().Be("string");
        cityProperty.GetProperty("description").GetString().Should().Be("City name");

        RequiredNames(toolArgs.JsonSchema).Should().Equal("city");
    }

    [TestMethod]
    public void ToJsonSchema_WithOptionalArgument_LeavesRequiredEmpty()
    {
        IToolArgument city = Arg("city", typeof(string), required: false, jsonSchema: """{"type":"string"}""");
        var toolArgs = new ToolArguments([city]);

        PropertyNames(toolArgs.JsonSchema).Should().Equal("city");
        RequiredNames(toolArgs.JsonSchema).Should().BeEmpty();
    }

    [TestMethod]
    public void ToJsonSchema_WithCancellationTokenArgument_ExcludesItFromSchemaButNotFromCount()
    {
        IToolArgument query = Arg("query", typeof(string), required: true, jsonSchema: """{"type":"string"}""");
        IToolArgument token = Arg("cancellationToken", typeof(CancellationToken), required: false,
            jsonSchema: """{"type":"string"}""");
        var toolArgs = new ToolArguments([query, token]);

        // The token stays in the argument list (it is a real parameter) …
        toolArgs.Count.Should().Be(2);
        // … but it must never leak into the schema the model sees.
        PropertyNames(toolArgs.JsonSchema).Should().Equal("query");
        RequiredNames(toolArgs.JsonSchema).Should().Equal("query");
    }

    [TestMethod]
    public void ToJsonSchema_WithMultipleArguments_ListsEachAndOnlyRequiredOnesAsRequired()
    {
        IToolArgument city = Arg("city", typeof(string), required: true, jsonSchema: """{"type":"string"}""");
        IToolArgument limit = Arg("limit", typeof(int), required: false, jsonSchema: """{"type":"integer"}""");
        var toolArgs = new ToolArguments([city, limit]);

        PropertyNames(toolArgs.JsonSchema).Should().BeEquivalentTo("city", "limit");
        RequiredNames(toolArgs.JsonSchema).Should().Equal("city");
    }

    private static IToolArgument Arg(string name, Type type, bool required, string jsonSchema, string? description = null)
    {
        var arg = Substitute.For<IToolArgument>();
        arg.Name.Returns(name);
        arg.Type.Returns(type);
        arg.IsRequired.Returns(required);
        arg.JsonSchema.Returns(jsonSchema);
        arg.Description.Returns(description);
        return arg;
    }

    private static IReadOnlyList<string> PropertyNames(string jsonSchema)
    {
        using JsonDocument doc = JsonDocument.Parse(jsonSchema);
        return doc.RootElement.GetProperty("properties")
            .EnumerateObject()
            .Select(property => property.Name)
            .ToList();
    }

    private static IReadOnlyList<string> RequiredNames(string jsonSchema)
    {
        using JsonDocument doc = JsonDocument.Parse(jsonSchema);
        return doc.RootElement.GetProperty("required")
            .EnumerateArray()
            .Select(element => element.GetString() ?? string.Empty)
            .ToList();
    }
}
