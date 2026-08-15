using System.ComponentModel.DataAnnotations;
using AwesomeAssertions;
using Nordstein.Core.AI.Messages;

namespace Nordstein.Core.AI.Tests;

/// <summary>
/// Covers <see cref="SystemMessage"/> construction (both the text and the content-list
/// constructors), its <see cref="Role.System"/> identity, the "content must be text" validation
/// rule layered on top of the base cascade, and equality/ToString.
/// </summary>
[TestClass]
public sealed class SystemMessageTests
{
    [TestMethod]
    public void Constructor_FromText_HasSystemRoleAndSingleTextContent()
    {
        var message = new SystemMessage("be helpful");

        message.Role.Should().Be(Role.System);
        message.Contents.Should().ContainSingle().Which.Text.Should().Be("be helpful");
        message.GetText().Should().Be("be helpful");
    }

    [TestMethod]
    public void Constructor_FromContents_UsesProvidedContents()
    {
        var message = new SystemMessage([Content.FromText("line one"), Content.FromText(" line two")]);

        message.Role.Should().Be(Role.System);
        message.GetText().Should().Be("line one line two");
    }

    [TestMethod]
    public void Validate_WithTextContent_HasNoFailures()
    {
        var message = new SystemMessage("be helpful");

        Failures(message).Should().BeEmpty();
    }

    [TestMethod]
    public void Validate_WithImageContent_ReportsMustBeText()
    {
        var message = new SystemMessage([Content.FromImage(BinaryData.FromBytes([1, 2, 3], "image/png"))]);

        Failures(message).Should().Contain(result =>
            result.ErrorMessage != null && result.ErrorMessage.Contains("must be of kind Text"));
    }

    [TestMethod]
    public void Equals_SameText_AreEqual()
    {
        var a = new SystemMessage("be helpful");
        var b = new SystemMessage("be helpful");

        a.Equals(b).Should().BeTrue();
        a.GetHashCode().Should().Be(b.GetHashCode());
    }

    [TestMethod]
    public void Equals_DifferentText_AreNotEqual()
    {
        var a = new SystemMessage("be helpful");
        var b = new SystemMessage("be terse");

        a.Equals(b).Should().BeFalse();
    }

    [TestMethod]
    public void ToString_IncludesRoleAndText()
    {
        var message = new SystemMessage("be helpful");

        message.ToString().Should().Be("System: be helpful");
    }

    private static IReadOnlyList<ValidationResult> Failures(SystemMessage message)
        => message
            .Validate(new ValidationContext(message))
            .Where(result => result != ValidationResult.Success)
            .ToList();
}
