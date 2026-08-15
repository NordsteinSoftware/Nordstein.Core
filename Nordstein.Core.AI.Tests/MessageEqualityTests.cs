using AwesomeAssertions;
using Nordstein.Core.AI.Messages;

namespace Nordstein.Core.AI.Tests;

/// <summary>
/// Covers the base <see cref="Message"/> behaviour shared by every concrete message type: role-
/// and content-aware equality, the <see cref="Message.GetText"/> handling of a content part with no
/// text (an image), and the <see cref="Message.Role"/>/<see cref="Message.Contents"/> accessors.
/// </summary>
[TestClass]
public sealed class MessageEqualityTests
{
    [TestMethod]
    public void Equals_SameRoleAndContents_AreEqual()
    {
        Message a = new UserMessage([Content.FromText("hi")]);
        Message b = new UserMessage([Content.FromText("hi")]);

        a.Equals(b).Should().BeTrue();
        a.GetHashCode().Should().Be(b.GetHashCode());
    }

    [TestMethod]
    public void Equals_DifferentRoleSameContent_AreNotEqual()
    {
        // Same single text content, but a user turn and an assistant turn are different messages.
        Message user = new UserMessage([Content.FromText("hi")]);
        Message assistant = new AssistantMessage([Content.FromText("hi")], []);

        user.Equals(assistant).Should().BeFalse();
    }

    [TestMethod]
    public void Equals_Null_IsFalse()
    {
        Message message = new UserMessage([Content.FromText("hi")]);

        message.Equals(null).Should().BeFalse();
    }

    [TestMethod]
    public void GetText_WithImageContent_TreatsMissingTextAsEmpty()
    {
        // A content part with no text (an image) contributes an empty string to the concatenation
        // rather than throwing or emitting "null".
        var message = new UserMessage(
        [
            Content.FromText("before "),
            Content.FromImage(BinaryData.FromBytes([1, 2, 3], "image/png")),
            Content.FromText("after"),
        ]);

        message.GetText().Should().Be("before after");
    }

    [TestMethod]
    public void Role_And_Contents_ExposeConstructorValues()
    {
        var contents = new[] { Content.FromText("a"), Content.FromText("b") };
        var message = new UserMessage(contents);

        message.Role.Should().Be(Role.User);
        message.Contents.Should().HaveCount(2);
    }
}
