using System.Text.Json;
using AwesomeAssertions;

namespace Nordstein.Core.Workflows.Tests;

// WHY: these tests pass null where the contract forbids it, to prove the runtime guards; the `!` operator is banned (rule 5).
#pragma warning disable CS8625

[TestClass]
public sealed class NodeTypeReferenceTests
{
    [TestMethod]
    public void NodeTypeReference_NameAtMajor_Parses()
    {
        NodeTypeReference.TryParse("invoicing.total-check@12", out NodeTypeReference? reference).Should().BeTrue();

        reference.Should().Be(new NodeTypeReference("invoicing.total-check", 12));
        reference?.ToString().Should().Be("invoicing.total-check@12");
    }

    [TestMethod]
    [DataRow("condition")]
    [DataRow("condition@")]
    [DataRow("@1")]
    [DataRow("condition@0")]
    [DataRow("condition@01")]
    [DataRow("condition@1.5")]
    [DataRow("condition@-1")]
    [DataRow("condition@1@2")]
    [DataRow("1condition@1")]
    [DataRow("con dition@1")]
    [DataRow("condition@9999999999")]
    [DataRow("")]
    public void NodeTypeReference_OtherText_DoesNotParse(string text)
    {
        NodeTypeReference.TryParse(text, out NodeTypeReference? reference).Should().BeFalse();

        reference.Should().BeNull();
        FluentActions.Invoking(() => NodeTypeReference.Parse(text)).Should().Throw<FormatException>().WithMessage($"*'{text}'*name@major*");
    }

    [TestMethod]
    public void NodeTypeReference_NullText_Throws()
    {
        FluentActions.Invoking(() => NodeTypeReference.TryParse(null, out _)).Should().Throw<ArgumentNullException>();
        FluentActions.Invoking(() => NodeTypeReference.Parse(null)).Should().Throw<ArgumentNullException>();
    }

    [TestMethod]
    public void NodeTypeReference_InvalidParts_Throw()
    {
        FluentActions.Invoking(() => new NodeTypeReference(null, 1)).Should().Throw<ArgumentNullException>();
        FluentActions.Invoking(() => new NodeTypeReference("", 1)).Should().Throw<ArgumentException>();
        FluentActions.Invoking(() => new NodeTypeReference("condition@1", 1)).Should().Throw<ArgumentException>();
        FluentActions.Invoking(() => new NodeTypeReference(new string('a', 65), 1)).Should().Throw<ArgumentException>();
        FluentActions.Invoking(() => new NodeTypeReference("condition", 0)).Should().Throw<ArgumentOutOfRangeException>();
    }

    [TestMethod]
    public void NodeTypeReference_Names_MatchesTheNameAtAnyMajor()
    {
        NodeTypeReference.Names("step@2", "step").Should().BeTrue();
        NodeTypeReference.Names("step@1", "ste").Should().BeFalse();
        NodeTypeReference.Names("step@1", "Step").Should().BeFalse();
        NodeTypeReference.Names("step", "step").Should().BeFalse();
        FluentActions.Invoking(() => NodeTypeReference.Names(null, "step")).Should().Throw<ArgumentNullException>();
        FluentActions.Invoking(() => NodeTypeReference.Names("step@1", null)).Should().Throw<ArgumentNullException>();
    }

    [TestMethod]
    public void NodeDefinition_HasName_MatchesTheNameAtAnyMajor()
    {
        using JsonDocument config = JsonDocument.Parse("{}");
        var node = new NodeDefinition("a", "step@3", config.RootElement.Clone());

        node.HasName("step").Should().BeTrue();
        node.HasName("other").Should().BeFalse();
        FluentActions.Invoking(() => NodeTypeExtensions.HasName(null, "step")).Should().Throw<ArgumentNullException>();
    }

    [TestMethod]
    public void NodeTypeReference_OfNodeType_NamesItsMajor()
    {
        var type = new TestNode("step") { Major = 3 };

        type.Reference().Should().Be(new NodeTypeReference("step", 3));
        type.Reference().ToString().Should().Be("step@3");
        FluentActions.Invoking(() => NodeTypeExtensions.Reference(null)).Should().Throw<ArgumentNullException>();
    }
}
