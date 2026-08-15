using AwesomeAssertions;
using Nordstein.Core.AI.Messages;

namespace Nordstein.Core.AI.Tests;

/// <summary>
/// Covers <see cref="UserMessage"/> construction, the immutable <see cref="UserMessage.Add"/>
/// append (which must return a new instance without mutating the original), and ToString.
/// </summary>
[TestClass]
public sealed class UserMessageTests
{
    [TestMethod]
    public void Constructor_SetsUserRoleAndContents()
    {
        var message = new UserMessage([Content.FromText("hi")]);

        message.Role.Should().Be(Role.User);
        message.Contents.Should().ContainSingle().Which.Text.Should().Be("hi");
    }

    [TestMethod]
    public void Add_AppendsContentAndReturnsNewInstance()
    {
        var original = new UserMessage([Content.FromText("a")]);

        var extended = original.Add(Content.FromText("b"));

        extended.Contents.Should().HaveCount(2);
        extended.GetText().Should().Be("ab");
    }

    [TestMethod]
    public void Add_DoesNotMutateOriginal()
    {
        var original = new UserMessage([Content.FromText("a")]);

        _ = original.Add(Content.FromText("b"));

        original.Contents.Should().ContainSingle();
    }

    [TestMethod]
    public void ToString_IncludesRoleAndText()
    {
        var message = new UserMessage([Content.FromText("hello")]);

        message.ToString().Should().Be("User: hello");
    }
}
