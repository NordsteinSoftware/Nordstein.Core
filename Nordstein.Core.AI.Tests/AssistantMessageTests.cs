using System.ComponentModel.DataAnnotations;
using AwesomeAssertions;
using Nordstein.Core.AI.Messages;

namespace Nordstein.Core.AI.Tests;

/// <summary>
/// Covers the <see cref="AssistantMessage"/> branches not exercised by the display-text and
/// ToString tests: the validation cascade into tool requests, <see cref="AssistantMessage.GetTextResponse"/>
/// and its two guard clauses, the tool-request-aware equality, and the defensive copy of the
/// tool-request list.
/// </summary>
[TestClass]
public sealed class AssistantMessageTests
{
    [TestMethod]
    public void Validate_WithValidContentAndToolRequest_HasNoFailures()
    {
        var message = new AssistantMessage(
            [Content.FromText("thinking")],
            [new ToolRequest("call-1", "lookup", "{}")]);

        Failures(message).Should().BeEmpty();
    }

    [TestMethod]
    public void Validate_WithInvalidToolRequest_ReportsFailure()
    {
        // The tool request has a blank id and name, which its own Validate rejects; the cascade
        // must surface those failures.
        var message = new AssistantMessage(
            [Content.FromText("thinking")],
            [new ToolRequest("", "", "{}")]);

        Failures(message).Should().NotBeEmpty();
    }

    [TestMethod]
    public void GetTextResponse_WithOnlyText_ReturnsConcatenatedText()
    {
        var message = new AssistantMessage(
            [Content.FromText("Hello "), Content.FromText("world")],
            []);

        message.GetTextResponse().Should().Be("Hello world");
    }

    [TestMethod]
    public void GetTextResponse_WithToolRequests_Throws()
    {
        var message = new AssistantMessage(
            [Content.FromText("thinking")],
            [new ToolRequest("call-1", "lookup", "{}")]);

        var act = () => message.GetTextResponse();

        act.Should().Throw<InvalidOperationException>();
    }

    [TestMethod]
    public void GetTextResponse_WithNonTextContent_Throws()
    {
        var message = new AssistantMessage(
            [Content.FromImage(BinaryData.FromBytes([1, 2, 3], "image/png"))],
            []);

        var act = () => message.GetTextResponse();

        act.Should().Throw<InvalidOperationException>();
    }

    [TestMethod]
    public void Equals_SameContentAndToolRequests_AreEqual()
    {
        var a = new AssistantMessage([Content.FromText("hi")], [new ToolRequest("c", "lookup", "{}")]);
        var b = new AssistantMessage([Content.FromText("hi")], [new ToolRequest("c", "lookup", "{}")]);

        a.Equals(b).Should().BeTrue();
        a.GetHashCode().Should().Be(b.GetHashCode());
    }

    [TestMethod]
    public void Equals_DifferentToolRequests_AreNotEqual()
    {
        var a = new AssistantMessage([Content.FromText("hi")], [new ToolRequest("c", "lookup", "{}")]);
        var b = new AssistantMessage([Content.FromText("hi")], [new ToolRequest("c", "delete", "{}")]);

        a.Equals(b).Should().BeFalse();
    }

    [TestMethod]
    public void Equals_DifferentContent_AreNotEqual()
    {
        var a = new AssistantMessage([Content.FromText("hi")], []);
        var b = new AssistantMessage([Content.FromText("bye")], []);

        a.Equals(b).Should().BeFalse();
    }

    [TestMethod]
    public void Equals_SameContentDifferentToolRequestCount_AreNotEqual()
    {
        var a = new AssistantMessage([Content.FromText("hi")], []);
        var b = new AssistantMessage([Content.FromText("hi")], [new ToolRequest("c", "lookup", "{}")]);

        a.Equals(b).Should().BeFalse();
    }

    [TestMethod]
    public void Equals_Null_IsFalse()
    {
        var message = new AssistantMessage([Content.FromText("hi")], []);

        message.Equals(null).Should().BeFalse();
    }

    [TestMethod]
    public void ToolRequests_IsDefensiveCopy_MutatingSourceDoesNotAffectMessage()
    {
        var toolRequests = new List<ToolRequest> { new("c", "lookup", "{}") };
        var message = new AssistantMessage([Content.FromText("hi")], toolRequests);

        toolRequests.Add(new ToolRequest("c2", "delete", "{}"));

        message.ToolRequests.Should().ContainSingle();
    }

    private static IReadOnlyList<ValidationResult> Failures(AssistantMessage message)
        => message
            .Validate(new ValidationContext(message))
            .Where(result => result != ValidationResult.Success)
            .ToList();
}
