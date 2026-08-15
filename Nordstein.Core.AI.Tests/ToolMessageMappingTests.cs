using AwesomeAssertions;
using Nordstein.Core.AI.Messages;

namespace Nordstein.Core.AI.Tests;

/// <summary>
/// Pins how a <see cref="ToolResponse"/> maps onto a <see cref="ToolMessage"/>'s content slots:
/// a successful call keeps its results (or a placeholder when it returned none), a failed call is
/// rendered as a human-readable status line. Also covers the <see cref="ToolMessage.Id"/> accessor
/// and the <see cref="ToolMessage.Deconstruct"/> guards, which the multi-result happy-path tests in
/// <c>ToolMessageTests</c> do not exercise.
/// </summary>
[TestClass]
public sealed class ToolMessageMappingTests
{
    [TestMethod]
    public void Ctor_FromSuccessfulResponseWithResult_UsesResultAsPayload()
    {
        var response = new ToolResponse(
            new ToolRequest("call-1", "lookup_order", "{}"),
            [Content.FromText("delivered")]);

        var message = new ToolMessage(response);

        message.Id.Should().Be("call-1");
        message.GetText().Should().Be("delivered");
    }

    [TestMethod]
    public void Ctor_FromSuccessfulResponseWithNoResults_UsesPlaceholderPayload()
    {
        // An empty successful response would otherwise leave only the id slot, which fails
        // validation (a tool message needs id + at least one result). The placeholder keeps it valid.
        var response = new ToolResponse(
            new ToolRequest("call-1", "acknowledge", "{}"),
            []);

        var message = new ToolMessage(response);

        message.Contents.Should().HaveCount(2);
        message.GetText().Should().Be("Tool executed successfully. No result returned.");
    }

    [TestMethod]
    public void Ctor_FromFailedResponse_RendersFailureStatusAndErrorMessage()
    {
        var response = new ToolResponse(
            new ToolRequest("call-1", "lookup_order", "{}"),
            new InvalidOperationException("order not found"));

        var message = new ToolMessage(response);

        var text = message.GetText();
        text.Should().Contain("Status: Failure");
        text.Should().Contain("order not found");
    }

    [TestMethod]
    public void Ctor_FromFailedResponseWithNullError_RendersUnknownError()
    {
        // The JSON constructor allows a failed response without an error instance; the mapping must
        // still produce a payload rather than throwing on the null error message.
        var response = new ToolResponse("call-1", [], success: false, error: null);

        var message = new ToolMessage(response);

        message.GetText().Should().Contain("Unknown error");
    }

    [TestMethod]
    public void Deconstruct_WithSingleContent_Throws()
    {
        var message = new ToolMessage([Content.FromText("call-1")]);

        var act = () => message.Deconstruct();

        act.Should().Throw<InvalidOperationException>();
    }

    [TestMethod]
    public void Deconstruct_WithWhitespaceId_Throws()
    {
        var message = new ToolMessage([Content.FromText("   "), Content.FromText("payload")]);

        var act = () => message.Deconstruct();

        act.Should().Throw<InvalidOperationException>();
    }

    [TestMethod]
    public void Id_WithSingleContent_Throws()
    {
        // Id is Deconstruct().Id, so it inherits the same guard.
        var message = new ToolMessage([Content.FromText("call-1")]);

        var act = () => _ = message.Id;

        act.Should().Throw<InvalidOperationException>();
    }
}
