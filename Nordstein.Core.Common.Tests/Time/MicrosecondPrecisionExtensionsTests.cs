using AwesomeAssertions;
using Nordstein.Core.Common.Time;

namespace Nordstein.Core.Common.Tests.Time;

[TestClass]
public sealed class MicrosecondPrecisionExtensionsTests
{
    [TestMethod]
    public void TruncateToMicroseconds_SubMicrosecondTicks_DropsThem()
    {
        var value = new DateTimeOffset(2026, 11, 8, 1, 2, 3, TimeSpan.Zero).AddTicks(1_234_567);

        value.TruncateToMicroseconds().Ticks.Should().Be(value.Ticks - 7);
    }

    [TestMethod]
    public void TruncateToMicroseconds_NonZeroOffset_ReturnsTheSameInstantInUtc()
    {
        var local = new DateTimeOffset(2026, 11, 8, 3, 0, 0, TimeSpan.FromHours(2)).AddTicks(15);

        DateTimeOffset truncated = local.TruncateToMicroseconds();

        truncated.Offset.Should().Be(TimeSpan.Zero);
        truncated.UtcDateTime.Should().Be(new DateTime(2026, 11, 8, 1, 0, 0, DateTimeKind.Utc).AddTicks(10));
    }

    [TestMethod]
    public void TruncateToMicroseconds_AlreadyTruncated_IsUnchanged()
    {
        var value = new DateTimeOffset(2026, 11, 8, 1, 2, 3, TimeSpan.Zero).AddTicks(40);

        value.TruncateToMicroseconds().Should().Be(value);
        value.TruncateToMicroseconds().TruncateToMicroseconds().Should().Be(value.TruncateToMicroseconds());
    }

    [TestMethod]
    public void TruncateToMicroseconds_MinAndMaxValue_StayInRange()
    {
        DateTimeOffset.MinValue.TruncateToMicroseconds().Should().Be(DateTimeOffset.MinValue);
        DateTimeOffset max = DateTimeOffset.MaxValue.TruncateToMicroseconds();
        max.Ticks.Should().Be(DateTimeOffset.MaxValue.Ticks - (DateTimeOffset.MaxValue.Ticks % TimeSpan.TicksPerMicrosecond));
        max.Offset.Should().Be(TimeSpan.Zero);
    }
}
