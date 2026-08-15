using System.ComponentModel.DataAnnotations;
using AwesomeAssertions;
using Nordstein.Core.AI.Completions;
using Nordstein.Core.Common.Validation;

namespace Nordstein.Core.AI.Tests;

/// <summary>
/// Covers the <see cref="TokenUsage"/> surface not exercised by <c>TokenUsageTests</c>: the zero
/// factories, the additive operator, the <see cref="TokenUsage.Create(ulong?, ulong?)"/> factories,
/// the cached-input subset, equality across the cached field, and validation.
/// </summary>
[TestClass]
public sealed class TokenUsageArithmeticTests
{
    [TestMethod]
    public void None_HasZeroCounts()
    {
        TokenUsage none = TokenUsage.None;

        none.InputTokenCount.Should().Be(0UL);
        none.OutputTokenCount.Should().Be(0UL);
        none.CachedInputTokenCount.Should().Be(0UL);
    }

    [TestMethod]
    public void DefaultConstructor_HasZeroCounts()
    {
        var usage = new TokenUsage();

        usage.Should().Be(TokenUsage.None);
    }

    [TestMethod]
    public void TwoArgumentConstructor_SetsCachedToZero()
    {
        var usage = new TokenUsage(inputTokenCount: 40, outputTokenCount: 12);

        usage.InputTokenCount.Should().Be(40UL);
        usage.OutputTokenCount.Should().Be(12UL);
        usage.CachedInputTokenCount.Should().Be(0UL);
    }

    [TestMethod]
    public void Plus_TwoValues_AddsEachTokenKind()
    {
        var a = new TokenUsage(100, 50, 20);
        var b = new TokenUsage(1, 2, 3);

        TokenUsage? result = a + b;

        result.Should().Be(new TokenUsage(101, 52, 23));
    }

    [TestMethod]
    public void Plus_BothNull_ReturnsNull()
    {
        TokenUsage? a = null;
        TokenUsage? b = null;

        (a + b).Should().BeNull();
    }

    [TestMethod]
    public void Create_WithBothCounts_ReturnsInstance()
    {
        TokenUsage? usage = TokenUsage.Create(inputTokenCount: 12, outputTokenCount: 7);

        usage.Should().Be(new TokenUsage(12, 7, 0));
    }

    [TestMethod]
    public void Create_WithMissingInputCount_ReturnsNull()
        => TokenUsage.Create(inputTokenCount: null, outputTokenCount: 7).Should().BeNull();

    [TestMethod]
    public void Create_WithMissingOutputCount_ReturnsNull()
        => TokenUsage.Create(inputTokenCount: 12, outputTokenCount: null).Should().BeNull();

    [TestMethod]
    public void Create_WithCachedSubset_SetsCached()
    {
        TokenUsage? usage = TokenUsage.Create(inputTokenCount: 30, outputTokenCount: 10, cachedInputTokenCount: 8);

        usage.Should().Be(new TokenUsage(30, 10, 8));
    }

    [TestMethod]
    public void Create_WithNullCached_DefaultsToZero()
    {
        TokenUsage? usage = TokenUsage.Create(inputTokenCount: 30, outputTokenCount: 10, cachedInputTokenCount: null);

        usage.Should().Be(new TokenUsage(30, 10, 0));
    }

    [TestMethod]
    public void Equals_SameCounts_AreEqual()
    {
        var a = new TokenUsage(5, 6, 7);
        var b = new TokenUsage(5, 6, 7);

        a.Should().Be(b);
        a.GetHashCode().Should().Be(b.GetHashCode());
    }

    [TestMethod]
    public void Equals_DifferingOnlyInCached_AreNotEqual()
    {
        var a = new TokenUsage(5, 6, 7);
        var b = new TokenUsage(5, 6, 8);

        a.Should().NotBe(b);
    }

    [TestMethod]
    public void Validate_ReturnsNoErrors()
    {
        var usage = new TokenUsage(5, 6, 7);

        usage.Validate(new ValidationContext(usage)).Should().BeEmpty();
        FluentActions.Invoking(() => usage.Validate()).Should().NotThrow();
    }
}
