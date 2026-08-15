using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Nordstein.Core.AI.Tools;
using Nordstein.Core.Common.Serialization;
using Nordstein.Core.Testing;

namespace Nordstein.Core.AI.Tests;

[TestClass]
public sealed class ToolArgumentsJsonConverterTests : BaseTest<Nordstein.Core.AI.Module>
{
    [TestMethod]
    public void RoundTrip_None_PreservesEquality()
    {
        IServiceProvider services = GetServices();
        var serializer = services.GetRequiredService<ISerializer>();

        ToolArguments restored = serializer.DeserializeRequired<ToolArguments>(serializer.Serialize(ToolArguments.None));

        // ToolArguments is IEnumerable, so equality is asserted through Equals rather than .Be(...).
        restored.Equals(ToolArguments.None).Should().BeTrue();
    }

    [TestMethod]
    public void RoundTrip_WithArguments_PreservesEquality()
    {
        IServiceProvider services = GetServices();
        var serializer = services.GetRequiredService<ISerializer>();

        ToolArguments original = ToolArguments.FromJsonSchema(
            """{"type":"object","properties":{"city":{"type":"string","description":"The city"},"limit":{"type":"integer"}},"required":["city"]}""");

        // The converter writes the schema through the serializer's (compact) writer and reads it back
        // via FromJsonSchema, so an argument's raw schema text is re-formatted on the first pass.
        // After that normalization the round trip is idempotent and preserves full equality.
        ToolArguments normalized = serializer.DeserializeRequired<ToolArguments>(serializer.Serialize(original));
        ToolArguments restored = serializer.DeserializeRequired<ToolArguments>(serializer.Serialize(normalized));

        restored.Equals(normalized).Should().BeTrue();
        restored.Count.Should().Be(2);
        restored.JsonSchema.Should().Be(original.JsonSchema);
    }

    [TestMethod]
    public void RoundTrip_WithArguments_PreservesTheJsonSchema()
    {
        IServiceProvider services = GetServices();
        var serializer = services.GetRequiredService<ISerializer>();

        ToolArguments original = ToolArguments.FromJsonSchema(
            """{"type":"object","properties":{"city":{"type":"string"}},"required":["city"]}""");

        ToolArguments restored = serializer.DeserializeRequired<ToolArguments>(serializer.Serialize(original));

        restored.JsonSchema.Should().Be(original.JsonSchema);
    }
}
