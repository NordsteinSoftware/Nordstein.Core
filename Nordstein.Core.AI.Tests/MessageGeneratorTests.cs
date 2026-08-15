using System.ComponentModel.DataAnnotations;
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Nordstein.Core.AI.Messages;
using Nordstein.Core.Domain;
using Nordstein.Core.Testing;

namespace Nordstein.Core.AI.Tests;

/// <summary>
/// Exercises the reflection-discovered domain-object generators for the message hierarchy. Each
/// resolves as <see cref="IDomainObjectGenerator{T}"/> from a container built on
/// <see cref="Nordstein.Core.AI.Module"/> and must produce a non-null instance that passes its own
/// validation — that is the contract seed scenarios and test fixtures rely on.
/// </summary>
[TestClass]
public sealed class MessageGeneratorTests : BaseTest<Nordstein.Core.AI.Module>
{
    [TestMethod]
    public async Task ContentGenerator_CreateAsync_ProducesValidContent()
    {
        IServiceProvider services = GetServices();
        var generator = services.GetRequiredService<IDomainObjectGenerator<Content>>();

        Content content = await generator.CreateAsync(CancellationToken);

        content.Should().NotBeNull();
        Failures(content).Should().BeEmpty();
    }

    [TestMethod]
    public async Task ToolRequestGenerator_CreateAsync_ProducesValidToolRequest()
    {
        IServiceProvider services = GetServices();
        var generator = services.GetRequiredService<IDomainObjectGenerator<ToolRequest>>();

        ToolRequest request = await generator.CreateAsync(CancellationToken);

        request.Should().NotBeNull();
        Failures(request).Should().BeEmpty();
    }

    [TestMethod]
    public async Task ToolResponseGenerator_CreateAsync_ProducesValidSuccessfulResponse()
    {
        IServiceProvider services = GetServices();
        var generator = services.GetRequiredService<IDomainObjectGenerator<ToolResponse>>();

        ToolResponse response = await generator.CreateAsync(CancellationToken);

        response.Should().NotBeNull();
        response.Success.Should().BeTrue();
        Failures(response).Should().BeEmpty();
    }

    [TestMethod]
    public async Task UserMessageGenerator_CreateAsync_ProducesValidUserMessage()
    {
        IServiceProvider services = GetServices();
        var generator = services.GetRequiredService<IDomainObjectGenerator<UserMessage>>();

        UserMessage message = await generator.CreateAsync(CancellationToken);

        message.Should().NotBeNull();
        message.Role.Should().Be(Role.User);
        Failures(message).Should().BeEmpty();
    }

    [TestMethod]
    public async Task AssistantMessageGenerator_CreateAsync_ProducesValidAssistantMessage()
    {
        IServiceProvider services = GetServices();
        var generator = services.GetRequiredService<IDomainObjectGenerator<AssistantMessage>>();

        AssistantMessage message = await generator.CreateAsync(CancellationToken);

        message.Should().NotBeNull();
        message.Role.Should().Be(Role.Assistant);
        Failures(message).Should().BeEmpty();
    }

    [TestMethod]
    public async Task SystemMessageGenerator_CreateAsync_ProducesValidSystemMessage()
    {
        IServiceProvider services = GetServices();
        var generator = services.GetRequiredService<IDomainObjectGenerator<SystemMessage>>();

        SystemMessage message = await generator.CreateAsync(CancellationToken);

        message.Should().NotBeNull();
        message.Role.Should().Be(Role.System);
        Failures(message).Should().BeEmpty();
    }

    [TestMethod]
    public async Task ToolMessageGenerator_CreateAsync_ProducesValidToolMessage()
    {
        IServiceProvider services = GetServices();
        var generator = services.GetRequiredService<IDomainObjectGenerator<ToolMessage>>();

        ToolMessage message = await generator.CreateAsync(CancellationToken);

        message.Should().NotBeNull();
        message.Role.Should().Be(Role.Tool);
        Failures(message).Should().BeEmpty();
    }

    [TestMethod]
    public async Task ConversationGenerator_CreateAsync_ProducesValidConversation()
    {
        IServiceProvider services = GetServices();
        var generator = services.GetRequiredService<IDomainObjectGenerator<Conversation>>();

        Conversation conversation = await generator.CreateAsync(CancellationToken);

        conversation.Should().NotBeNull();
        conversation.Messages.Should().HaveCount(2);
        Failures(conversation).Should().BeEmpty();
    }

    private static IReadOnlyList<ValidationResult> Failures(IDomainObject domainObject)
        => domainObject
            .Validate(new ValidationContext(domainObject))
            .Where(result => result != ValidationResult.Success)
            .ToList();
}
