using System.ComponentModel.DataAnnotations;
using AwesomeAssertions;
using Nordstein.Core.AI.Messages;

namespace Nordstein.Core.AI.Tests;

/// <summary>
/// Covers the three <see cref="ToolResponse"/> constructors, its success/error validation
/// invariant, and its content-folding equality. These are the value-object semantics every
/// tool round-trip and comparison relies on.
/// </summary>
[TestClass]
public sealed class ToolResponseTests
{
    [TestMethod]
    public void Ctor_SuccessFromRequest_SetsSuccessAndCopiesIdWithNoError()
    {
        var request = new ToolRequest("call-1", "lookup_order", "{}");

        var response = new ToolResponse(request, [Content.FromText("result")]);

        response.Id.Should().Be("call-1");
        response.Success.Should().BeTrue();
        response.Error.Should().BeNull();
        response.Results.Should().ContainSingle().Which.Text.Should().Be("result");
    }

    [TestMethod]
    public void Ctor_FailureFromRequest_SetsErrorAndEmptyResults()
    {
        var request = new ToolRequest("call-1", "lookup_order", "{}");
        var error = new InvalidOperationException("boom");

        var response = new ToolResponse(request, error);

        response.Id.Should().Be("call-1");
        response.Success.Should().BeFalse();
        response.Error.Should().BeSameAs(error);
        response.Results.Should().BeEmpty();
    }

    [TestMethod]
    public void Ctor_Json_SetsAllProperties()
    {
        var response = new ToolResponse(
            id: "call-1",
            results: [Content.FromText("a"), Content.FromText("b")],
            success: true,
            error: null);

        response.Id.Should().Be("call-1");
        response.Results.Should().HaveCount(2);
        response.Success.Should().BeTrue();
        response.Error.Should().BeNull();
    }

    [TestMethod]
    public void Validate_SuccessWithNullError_HasNoFailures()
    {
        var response = new ToolResponse("call-1", [Content.FromText("ok")], success: true, error: null);

        Failures(response).Should().BeEmpty();
    }

    [TestMethod]
    public void Validate_SuccessWithError_ReportsErrorMustBeNull()
    {
        // A success must not also carry an error — the invariant Validate enforces.
        var response = new ToolResponse(
            "call-1",
            [Content.FromText("ok")],
            success: true,
            error: new InvalidOperationException("boom"));

        Failures(response).Should().NotBeEmpty();
    }

    [TestMethod]
    public void Validate_FailureWithError_HasNoFailures()
    {
        var response = new ToolResponse("call-1", [], success: false, error: new InvalidOperationException("boom"));

        Failures(response).Should().BeEmpty();
    }

    [TestMethod]
    public void Validate_FailureWithNullError_ReportsMissingError()
    {
        var response = new ToolResponse("call-1", [], success: false, error: null);

        Failures(response).Should().NotBeEmpty();
    }

    [TestMethod]
    public void Validate_WhitespaceId_ReportsFailure()
    {
        var response = new ToolResponse("   ", [Content.FromText("ok")], success: true, error: null);

        Failures(response).Should().NotBeEmpty();
    }

    [TestMethod]
    public void Equals_SameValues_AreEqual()
    {
        var a = new ToolResponse("call-1", [Content.FromText("r")], success: true, error: null);
        var b = new ToolResponse("call-1", [Content.FromText("r")], success: true, error: null);

        a.Equals(b).Should().BeTrue();
        a.GetHashCode().Should().Be(b.GetHashCode());
    }

    [TestMethod]
    public void Equals_SameErrorInstance_AreEqual()
    {
        var error = new InvalidOperationException("boom");
        var a = new ToolResponse("call-1", [], success: false, error: error);
        var b = new ToolResponse("call-1", [], success: false, error: error);

        a.Equals(b).Should().BeTrue();
        a.GetHashCode().Should().Be(b.GetHashCode());
    }

    [TestMethod]
    public void Equals_DifferentId_AreNotEqual()
    {
        var a = new ToolResponse("call-1", [Content.FromText("r")], success: true, error: null);
        var b = new ToolResponse("call-2", [Content.FromText("r")], success: true, error: null);

        a.Equals(b).Should().BeFalse();
    }

    [TestMethod]
    public void Equals_DifferentResults_AreNotEqual()
    {
        var a = new ToolResponse("call-1", [Content.FromText("r1")], success: true, error: null);
        var b = new ToolResponse("call-1", [Content.FromText("r2")], success: true, error: null);

        a.Equals(b).Should().BeFalse();
    }

    [TestMethod]
    public void Equals_DifferentSuccess_AreNotEqual()
    {
        var a = new ToolResponse("call-1", [], success: false, error: new InvalidOperationException("boom"));
        var b = new ToolResponse("call-1", [], success: true, error: null);

        a.Equals(b).Should().BeFalse();
    }

    [TestMethod]
    public void Equals_DifferentErrorInstances_AreNotEqual()
    {
        var a = new ToolResponse("call-1", [], success: false, error: new InvalidOperationException("a"));
        var b = new ToolResponse("call-1", [], success: false, error: new InvalidOperationException("b"));

        a.Equals(b).Should().BeFalse();
    }

    [TestMethod]
    public void Equals_Null_IsFalse()
    {
        var response = new ToolResponse("call-1", [Content.FromText("r")], success: true, error: null);

        response.Equals(null).Should().BeFalse();
    }

    private static IReadOnlyList<ValidationResult> Failures(ToolResponse response)
        => response
            .Validate(new ValidationContext(response))
            .Where(result => result != ValidationResult.Success)
            .ToList();
}
