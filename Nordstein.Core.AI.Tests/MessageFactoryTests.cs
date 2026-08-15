using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Nordstein.Core.AI.Messages;
using Nordstein.Core.AI.Prompts;
using Nordstein.Core.Testing;

namespace Nordstein.Core.AI.Tests;

/// <summary>
/// Covers the static <see cref="Message"/> factory methods that consumers use to build the message
/// hierarchy. The prompt-template overload is exercised through a real rendered template resolved
/// from the container, so a rendering regression would surface here.
/// </summary>
[TestClass]
public sealed class MessageFactoryTests : BaseTest<Nordstein.Core.AI.Module>
{
    [TestMethod]
    public void CreateUserMessage_FromString_HasUserRoleAndText()
    {
        var message = Message.CreateUserMessage("hello");

        message.Role.Should().Be(Role.User);
        message.GetText().Should().Be("hello");
    }

    [TestMethod]
    public void CreateUserMessage_FromContents_UsesContents()
    {
        var message = Message.CreateUserMessage([Content.FromText("a"), Content.FromText("b")]);

        message.Role.Should().Be(Role.User);
        message.Contents.Should().HaveCount(2);
    }

    [TestMethod]
    public void CreateSystemMessage_FromString_HasSystemRoleAndText()
    {
        var message = Message.CreateSystemMessage("be helpful");

        message.Role.Should().Be(Role.System);
        message.GetText().Should().Be("be helpful");
    }

    [TestMethod]
    public void CreateAssistantMessage_SetsContentsAndToolRequests()
    {
        var toolRequest = new ToolRequest("call-1", "lookup", "{}");

        var message = Message.CreateAssistantMessage([Content.FromText("thinking")], [toolRequest]);

        message.Role.Should().Be(Role.Assistant);
        message.GetText().Should().Be("thinking");
        message.ToolRequests.Should().ContainSingle().Which.Should().Be(toolRequest);
    }

    [TestMethod]
    public void CreateToolMessage_FromResponse_WrapsResponsePayload()
    {
        var response = new ToolResponse(new ToolRequest("call-1", "lookup", "{}"), [Content.FromText("done")]);

        var message = Message.CreateToolMessage(response);

        message.Role.Should().Be(Role.Tool);
        message.Id.Should().Be("call-1");
        message.GetText().Should().Be("done");
    }

    [TestMethod]
    public void CreateSystemMessage_FromTemplate_RendersVariablesIntoText()
    {
        IServiceProvider services = GetServices();
        var factory = services.GetRequiredService<IPromptTemplate.Create>();
        IPromptTemplate template = factory("greeting", "Hello {{name}}");

        var message = Message.CreateSystemMessage(
            template,
            new Dictionary<string, string> { ["name"] = "world" });

        message.Role.Should().Be(Role.System);
        message.GetText().Should().Be("Hello world");
    }
}
