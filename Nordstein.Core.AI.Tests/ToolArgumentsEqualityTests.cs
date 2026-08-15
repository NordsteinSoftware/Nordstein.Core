using System.Text.Json;
using AwesomeAssertions;
using Nordstein.Core.AI.Tools;

namespace Nordstein.Core.AI.Tests;

[TestClass]
public sealed class ToolArgumentsEqualityTests
{
    // ToolArguments is IEnumerable<IToolArgument>, so `.Should().Be(...)` would bind to the
    // collection assertions; equality is asserted through Equals/== and GetHashCode directly.

    [TestMethod]
    public void Equals_WithSameContent_IsTrueAndHashesMatch()
    {
        ToolArguments a = Build(("city", true, """{"type":"string"}"""));
        ToolArguments b = Build(("city", true, """{"type":"string"}"""));

        a.Equals(b).Should().BeTrue();
        (a == b).Should().BeTrue();
        a.GetHashCode().Should().Be(b.GetHashCode());
    }

    [TestMethod]
    public void Equals_WithDifferentArgumentSchema_IsFalse()
    {
        ToolArguments a = Build(("city", true, """{"type":"string"}"""));
        ToolArguments other = Build(("city", true, """{"type":"number"}"""));

        a.Equals(other).Should().BeFalse();
        (a != other).Should().BeTrue();
    }

    [TestMethod]
    public void Equals_WithDifferentRequiredFlag_IsFalse()
    {
        ToolArguments a = Build(("city", true, """{"type":"string"}"""));
        ToolArguments other = Build(("city", false, """{"type":"string"}"""));

        a.Equals(other).Should().BeFalse();
    }

    [TestMethod]
    public void Equals_WithDifferentArgumentCount_IsFalse()
    {
        ToolArguments a = Build(("city", true, """{"type":"string"}"""));
        ToolArguments other = Build(
            ("city", true, """{"type":"string"}"""),
            ("country", false, """{"type":"string"}"""));

        a.Equals(other).Should().BeFalse();
    }

    [TestMethod]
    public void Equals_WithNull_IsFalse()
    {
        ToolArguments a = Build(("city", true, """{"type":"string"}"""));

        a.Equals((ToolArguments?)null).Should().BeFalse();
        (a == null).Should().BeFalse();
    }

    [TestMethod]
    public void Equals_WithItself_IsTrue()
    {
        ToolArguments a = Build(("city", true, """{"type":"string"}"""));

        a.Equals(a).Should().BeTrue();
    }

    private static ToolArguments Build(params (string Name, bool Required, string Schema)[] specifications)
    {
        IReadOnlyList<IToolArgument> arguments = specifications
            .Select(specification =>
            {
                using JsonDocument doc = JsonDocument.Parse(specification.Schema);
                return (IToolArgument)new JsonToolArgument(specification.Name, specification.Required, doc.RootElement);
            })
            .ToList();
        return new ToolArguments(arguments);
    }
}
