using AwesomeAssertions;

namespace Nordstein.Core.Storage.Tests;

/// <summary>
/// The <c>UpdatedAt</c> token is compared and truncated at microsecond granularity — the precision a
/// relational <c>timestamptz</c> round-trips — so a full-precision in-memory token does not spuriously
/// conflict with the value the database persisted.
/// </summary>
[TestClass]
public sealed class ConcurrencyTokenExtensionsTests
{
    [TestMethod]
    public void MatchesConcurrencyToken_IdenticalValues_Match()
    {
        var value = new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);
        value.MatchesConcurrencyToken(value).Should().BeTrue();
    }

    [TestMethod]
    public void MatchesConcurrencyToken_SubMicrosecondDifference_Match()
    {
        var microsecondAligned = new DateTimeOffset(
            2026, 1, 1, 12, 0, 0, TimeSpan.Zero).TruncateToMicroseconds();
        var withSubMicrosecond = microsecondAligned.AddTicks(3); // < 1µs (10 ticks)

        microsecondAligned.MatchesConcurrencyToken(withSubMicrosecond).Should().BeTrue();
        withSubMicrosecond.MatchesConcurrencyToken(microsecondAligned).Should().BeTrue();
    }

    [TestMethod]
    public void MatchesConcurrencyToken_OneMicrosecondDifference_DoesNotMatch()
    {
        var value = new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero).TruncateToMicroseconds();
        var oneMicrosecondLater = value.AddTicks(TimeSpan.TicksPerMicrosecond);

        value.MatchesConcurrencyToken(oneMicrosecondLater).Should().BeFalse();
    }

    [TestMethod]
    public void MatchesConcurrencyToken_DifferentOffsetsSameInstant_Match()
    {
        var utc = new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);
        var sameInstantOtherOffset = utc.ToOffset(TimeSpan.FromHours(2));

        utc.MatchesConcurrencyToken(sameInstantOtherOffset).Should().BeTrue();
    }

    [TestMethod]
    public void TruncateToMicroseconds_DropsSubMicrosecondTicks()
    {
        var microsecondAligned = new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);
        var withSubMicrosecond = microsecondAligned.AddTicks(7);

        withSubMicrosecond.TruncateToMicroseconds().Should().Be(microsecondAligned);
    }

    [TestMethod]
    public void TruncateToMicroseconds_NormalisesToUtc()
    {
        var withOffset = new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.FromHours(3));

        withOffset.TruncateToMicroseconds().Offset.Should().Be(TimeSpan.Zero);
        withOffset.TruncateToMicroseconds().Should().Be(withOffset.ToUniversalTime());
    }
}
