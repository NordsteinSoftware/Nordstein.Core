using System.ComponentModel.DataAnnotations;
using AwesomeAssertions;
using Nordstein.Core.AI.Messages;

namespace Nordstein.Core.AI.Tests;

/// <summary>
/// Covers <see cref="Conversation"/> as an immutable value object: its append/system-message
/// transforms (each returning a new instance), the defensive copy of the backing list, the
/// content-folding equality, validation cascade, and string rendering. The
/// <see cref="Conversation.ResolvedToolCallCount"/> counting rule is pinned separately in
/// <c>ConversationResolvedToolCallCountTests</c>.
/// </summary>
[TestClass]
public sealed class ConversationTests
{
    [TestMethod]
    public void Create_ReturnsEmptyConversation()
    {
        var conversation = Conversation.Create();

        conversation.Messages.Should().BeEmpty();
        conversation.SystemMessage.Should().BeNull();
    }

    [TestMethod]
    public void Constructor_CopiesMessages_MutatingSourceListDoesNotAffectConversation()
    {
        var source = new List<Message> { User("hello") };
        var conversation = new Conversation(source);

        source.Add(User("added later"));

        conversation.Messages.Should().ContainSingle();
    }

    [TestMethod]
    public void With_AppendsMessageInOrder()
    {
        var first = User("first");
        var second = Assistant("second");

        var conversation = Conversation.Create().With(first).With(second);

        conversation.Messages.Should().HaveCount(2);
        conversation.Messages[0].Should().Be(first);
        conversation.Messages[1].Should().Be(second);
    }

    [TestMethod]
    public void With_SystemMessage_Throws()
    {
        var conversation = Conversation.Create();

        var act = () => conversation.With(new SystemMessage("system"));

        act.Should().Throw<InvalidOperationException>();
    }

    [TestMethod]
    public void WithSystemMessage_PrependsSystemMessage()
    {
        var conversation = Conversation.Create().With(User("hi"));
        var system = new SystemMessage("be helpful");

        var result = conversation.WithSystemMessage(system);

        result.Messages[0].Should().Be(system);
        result.Messages.Should().HaveCount(2);
        result.SystemMessage.Should().Be(system);
    }

    [TestMethod]
    public void WithSystemMessage_WhenSystemAlreadyPresent_Throws()
    {
        var conversation = Conversation.Create().WithSystemMessage(new SystemMessage("first"));

        var act = () => conversation.WithSystemMessage(new SystemMessage("second"));

        act.Should().Throw<InvalidOperationException>();
    }

    [TestMethod]
    public void WithoutSystemMessage_RemovesSystemMessage()
    {
        var conversation = Conversation.Create()
            .With(User("hi"))
            .WithSystemMessage(new SystemMessage("be helpful"));

        var result = conversation.WithoutSystemMessage();

        result.SystemMessage.Should().BeNull();
        result.Messages.Should().ContainSingle();
    }

    [TestMethod]
    public void WithoutSystemMessage_WhenNoSystemMessage_ReturnsEquivalentConversation()
    {
        var conversation = Conversation.Create().With(User("hi"));

        var result = conversation.WithoutSystemMessage();

        result.Should().Be(conversation);
    }

    [TestMethod]
    public void SystemMessage_WhenPresent_ReturnsIt()
    {
        var system = new SystemMessage("be helpful");
        var conversation = Conversation.Create().WithSystemMessage(system);

        conversation.SystemMessage.Should().Be(system);
    }

    [TestMethod]
    public void SystemMessage_WhenAbsent_ReturnsNull()
    {
        var conversation = Conversation.Create().With(User("hi"));

        conversation.SystemMessage.Should().BeNull();
    }

    [TestMethod]
    public void ReplaceSystemMessage_ReplacesExistingSystemMessage()
    {
        var conversation = Conversation.Create()
            .With(User("hi"))
            .WithSystemMessage(new SystemMessage("old"));
        var replacement = new SystemMessage("new");

        var result = Conversation.ReplaceSystemMessage(conversation, replacement);

        result.SystemMessage.Should().Be(replacement);
        result.Messages.Should().HaveCount(2);
    }

    [TestMethod]
    public void ReplaceSystemMessage_WhenNoneExists_AddsSystemMessage()
    {
        var conversation = Conversation.Create().With(User("hi"));
        var system = new SystemMessage("new");

        var result = Conversation.ReplaceSystemMessage(conversation, system);

        result.SystemMessage.Should().Be(system);
        result.Messages[0].Should().Be(system);
    }

    [TestMethod]
    public void Equals_SameMessages_AreEqual()
    {
        var a = new Conversation([User("hi"), Assistant("hello")]);
        var b = new Conversation([User("hi"), Assistant("hello")]);

        a.Equals(b).Should().BeTrue();
        a.GetHashCode().Should().Be(b.GetHashCode());
    }

    [TestMethod]
    public void Equals_DifferentMessages_AreNotEqual()
    {
        var a = new Conversation([User("hi")]);
        var b = new Conversation([User("bye")]);

        a.Equals(b).Should().BeFalse();
    }

    [TestMethod]
    public void Equals_DifferentOrder_AreNotEqual()
    {
        var user = User("hi");
        var assistant = Assistant("hello");
        var a = new Conversation([user, assistant]);
        var b = new Conversation([assistant, user]);

        a.Equals(b).Should().BeFalse();
    }

    [TestMethod]
    public void Equals_Null_IsFalse()
    {
        var conversation = new Conversation([User("hi")]);

        conversation.Equals(null).Should().BeFalse();
    }

    [TestMethod]
    public void Validate_WithValidMessages_HasNoFailures()
    {
        var conversation = new Conversation([User("hi"), Assistant("hello")]);

        Failures(conversation).Should().BeEmpty();
    }

    [TestMethod]
    public void Validate_WithInvalidMessage_ReportsFailure()
    {
        // An empty-text content is invalid, so the cascade must surface a failure.
        var conversation = new Conversation([new UserMessage([Content.FromText("")])]);

        Failures(conversation).Should().NotBeEmpty();
    }

    [TestMethod]
    public void ToString_JoinsMessagesWithNewline()
    {
        var conversation = new Conversation([User("hi"), Assistant("hello")]);

        conversation.ToString().Should().Be($"User: hi{Environment.NewLine}Assistant: hello");
    }

    private static UserMessage User(string text)
        => new([Content.FromText(text)]);

    private static AssistantMessage Assistant(string text)
        => new([Content.FromText(text)], []);

    private static IReadOnlyList<ValidationResult> Failures(Conversation conversation)
        => conversation
            .Validate(new ValidationContext(conversation))
            .Where(result => result != ValidationResult.Success)
            .ToList();
}
