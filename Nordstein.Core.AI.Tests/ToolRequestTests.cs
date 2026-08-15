using System.ComponentModel.DataAnnotations;
using AwesomeAssertions;
using Nordstein.Core.AI.Messages;

namespace Nordstein.Core.AI.Tests;

/// <summary>
/// Covers <see cref="ToolRequest"/> construction, its id/name validation (arguments are
/// deliberately not validated as a string), and record equality across all three components.
/// </summary>
[TestClass]
public sealed class ToolRequestTests
{
    [TestMethod]
    public void Constructor_SetsAllProperties()
    {
        var request = new ToolRequest("call-1", "lookup_order", """{"id":1}""");

        request.Id.Should().Be("call-1");
        request.Name.Should().Be("lookup_order");
        request.Arguments.Should().Be("""{"id":1}""");
    }

    [TestMethod]
    public void Validate_WithValidIdAndName_HasNoFailures()
    {
        var request = new ToolRequest("call-1", "lookup_order", "{}");

        Failures(request).Should().BeEmpty();
    }

    [TestMethod]
    public void Validate_WithWhitespaceId_ReportsFailure()
    {
        var request = new ToolRequest("   ", "lookup_order", "{}");

        Failures(request).Should().NotBeEmpty();
    }

    [TestMethod]
    public void Validate_WithEmptyName_ReportsFailure()
    {
        var request = new ToolRequest("call-1", "", "{}");

        Failures(request).Should().NotBeEmpty();
    }

    [TestMethod]
    public void Validate_WithEmptyArguments_HasNoFailures()
    {
        // Arguments are not validated (they may legitimately be empty for a no-arg tool).
        var request = new ToolRequest("call-1", "lookup_order", "");

        Failures(request).Should().BeEmpty();
    }

    [TestMethod]
    public void Equals_SameValues_AreEqual()
    {
        var a = new ToolRequest("call-1", "lookup_order", "{}");
        var b = new ToolRequest("call-1", "lookup_order", "{}");

        a.Should().Be(b);
        a.GetHashCode().Should().Be(b.GetHashCode());
    }

    [TestMethod]
    public void Equals_DifferentArguments_AreNotEqual()
    {
        var a = new ToolRequest("call-1", "lookup_order", """{"id":1}""");
        var b = new ToolRequest("call-1", "lookup_order", """{"id":2}""");

        a.Should().NotBe(b);
    }

    [TestMethod]
    public void Equals_DifferentName_AreNotEqual()
    {
        var a = new ToolRequest("call-1", "lookup_order", "{}");
        var b = new ToolRequest("call-1", "delete_order", "{}");

        a.Should().NotBe(b);
    }

    private static IReadOnlyList<ValidationResult> Failures(ToolRequest request)
        => request
            .Validate(new ValidationContext(request))
            .Where(result => result != ValidationResult.Success)
            .ToList();
}
