using AwesomeAssertions;
using Nordstein.Core.AI.Completions;

namespace Nordstein.Core.AI.Tests;

/// <summary>
/// Exercises the <see cref="TypedCompletion{TOutput}"/> record: its positional members, the
/// nullability of the parsed response and usage, deconstruction, and value equality.
/// </summary>
[TestClass]
public sealed class TypedCompletionTests
{
    [TestMethod]
    public void Constructor_ExposesResponseUsageAndLatency()
    {
        var usage = new TokenUsage(1, 2);
        TimeSpan latency = TimeSpan.FromSeconds(3);

        var completion = new TypedCompletion<string>("answer", usage, latency);

        completion.Response.Should().Be("answer");
        completion.Usage.Should().Be(usage);
        completion.Latency.Should().Be(latency);
    }

    [TestMethod]
    public void Constructor_WithNullResponseAndUsage_AllowsNulls()
    {
        var completion = new TypedCompletion<string>(null, null, TimeSpan.Zero);

        completion.Response.Should().BeNull();
        completion.Usage.Should().BeNull();
        completion.Latency.Should().Be(TimeSpan.Zero);
    }

    [TestMethod]
    public void Deconstruct_YieldsPositionalComponents()
    {
        var usage = new TokenUsage(5, 6);
        var completion = new TypedCompletion<int>(42, usage, TimeSpan.FromMilliseconds(100));

        (int response, TokenUsage? reportedUsage, TimeSpan latency) = completion;

        response.Should().Be(42);
        reportedUsage.Should().Be(usage);
        latency.Should().Be(TimeSpan.FromMilliseconds(100));
    }

    [TestMethod]
    public void Equals_SameValues_AreEqual()
    {
        var usage = new TokenUsage(1, 2);
        var a = new TypedCompletion<string>("x", usage, TimeSpan.FromSeconds(1));
        var b = new TypedCompletion<string>("x", usage, TimeSpan.FromSeconds(1));

        a.Should().Be(b);
        a.GetHashCode().Should().Be(b.GetHashCode());
    }

    [TestMethod]
    public void Equals_DifferentResponse_AreNotEqual()
    {
        var usage = new TokenUsage(1, 2);
        var a = new TypedCompletion<string>("x", usage, TimeSpan.FromSeconds(1));
        var b = new TypedCompletion<string>("y", usage, TimeSpan.FromSeconds(1));

        a.Should().NotBe(b);
    }
}
