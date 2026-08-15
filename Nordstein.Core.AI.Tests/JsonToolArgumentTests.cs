using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using AwesomeAssertions;
using Nordstein.Core.AI.Tools;
using Nordstein.Core.Common.Validation;

namespace Nordstein.Core.AI.Tests;

[TestClass]
public sealed class JsonToolArgumentTests
{
    [TestMethod]
    public void Constructor_WithDescription_PopulatesEveryMember()
    {
        using JsonDocument doc = JsonDocument.Parse("""{"type":"string","description":"A city"}""");
        string expectedRawSchema = doc.RootElement.GetRawText();

        var argument = new JsonToolArgument("city", isRequired: true, doc.RootElement);

        argument.Name.Should().Be("city");
        argument.IsRequired.Should().BeTrue();
        argument.Type.Should().Be(typeof(object));
        argument.Description.Should().Be("A city");
        argument.DefaultValue.Should().BeNull();
        argument.JsonSchema.Should().Be(expectedRawSchema);
    }

    [TestMethod]
    public void Constructor_WithoutDescription_LeavesDescriptionNull()
    {
        using JsonDocument doc = JsonDocument.Parse("""{"type":"string"}""");

        var argument = new JsonToolArgument("city", isRequired: false, doc.RootElement);

        argument.Description.Should().BeNull();
        argument.IsRequired.Should().BeFalse();
    }

    [TestMethod]
    public void Constructor_WithNonStringDescription_LeavesDescriptionNull()
    {
        // A non-string "description" (here a number) must not be surfaced as the description.
        using JsonDocument doc = JsonDocument.Parse("""{"type":"string","description":123}""");

        var argument = new JsonToolArgument("city", isRequired: false, doc.RootElement);

        argument.Description.Should().BeNull();
    }

    [TestMethod]
    public void FromJsonSchema_ProducesJsonToolArgumentsWithObjectTypeAndNullDefault()
    {
        ToolArguments toolArgs = ToolArguments.FromJsonSchema(
            """{"type":"object","properties":{"city":{"type":"string","description":"A city"}},"required":["city"]}""");

        IToolArgument argument = toolArgs.Single();
        argument.Name.Should().Be("city");
        argument.IsRequired.Should().BeTrue();
        argument.Type.Should().Be(typeof(object));
        argument.Description.Should().Be("A city");
        argument.DefaultValue.Should().BeNull();

        using JsonDocument schema = JsonDocument.Parse(argument.JsonSchema);
        schema.RootElement.GetProperty("type").GetString().Should().Be("string");
    }

    [TestMethod]
    public void Validate_WithValidNameAndSchema_DoesNotThrow()
    {
        using JsonDocument doc = JsonDocument.Parse("""{"type":"string"}""");
        var argument = new JsonToolArgument("city", isRequired: true, doc.RootElement);

        argument.Invoking(candidate => candidate.Validate()).Should().NotThrow();
    }

    [TestMethod]
    public void Validate_WithWhitespaceName_Throws()
    {
        using JsonDocument doc = JsonDocument.Parse("""{"type":"string"}""");
        var argument = new JsonToolArgument("   ", isRequired: true, doc.RootElement);

        argument.Invoking(candidate => candidate.Validate()).Should().Throw<ValidationException>();
    }
}
