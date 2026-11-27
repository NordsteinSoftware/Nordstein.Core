using AwesomeAssertions;
using Nordstein.Core.Common.Tests.Hosting.Internal;
using Nordstein.Core.Common.Time;

namespace Nordstein.Core.Common.Tests.Time;

/// <summary>
/// A <see cref="ShiftedTimeProvider"/> reports the inner provider's instant plus a fixed offset,
/// the same instant through <see cref="TimeProvider.GetUtcNow"/> and <see cref="IClock.UtcNow"/>,
/// always at zero offset; everything else is the inner provider's.
/// </summary>
[TestClass]
public sealed class ShiftedTimeProviderTests
{
    private static readonly TimeSpan Ahead = TimeSpan.FromDays(24) + TimeSpan.FromHours(7);

    [TestMethod]
    public void GetUtcNow_IsTheInnerInstantPlusTheOffset()
    {
        var inner = new ManualTimeProvider();

        var shifted = new ShiftedTimeProvider(inner, Ahead);

        shifted.GetUtcNow().Should().Be(inner.GetUtcNow() + Ahead);
    }

    [TestMethod]
    public void GetUtcNow_AfterTheInnerProviderAdvances_AdvancesByTheSameSpan()
    {
        var inner = new ManualTimeProvider();
        var shifted = new ShiftedTimeProvider(inner, Ahead);
        DateTimeOffset before = shifted.GetUtcNow();

        inner.Advance(TimeSpan.FromHours(3));

        shifted.GetUtcNow().Should().Be(before.AddHours(3));
    }

    [TestMethod]
    public void GetUtcNow_NegativeOffset_ReadsThePast()
    {
        var inner = new ManualTimeProvider();

        var shifted = new ShiftedTimeProvider(inner, TimeSpan.FromDays(-2));

        shifted.GetUtcNow().Should().Be(inner.GetUtcNow().AddDays(-2));
    }

    [TestMethod]
    public void GetUtcNow_TwoProvidersWithTheSameOffset_AgreeOnTheSameInnerInstant()
    {
        // Two processes sharing one offset (a server and the CLI beside it) read the same shifted time.
        var inner = new ManualTimeProvider();

        var first = new ShiftedTimeProvider(inner, Ahead);
        inner.Advance(TimeSpan.FromSeconds(10));
        var second = new ShiftedTimeProvider(inner, Ahead);

        second.GetUtcNow().Should().Be(first.GetUtcNow());
    }

    [TestMethod]
    public void UtcNow_ReadsTheSameInstantAsGetUtcNow()
    {
        var inner = new ManualTimeProvider();
        var shifted = new ShiftedTimeProvider(inner, Ahead);
        inner.Advance(TimeSpan.FromMinutes(5));

        IClock clock = shifted;

        clock.UtcNow.Should().Be(shifted.GetUtcNow());
    }

    [TestMethod]
    public void GetUtcNow_ReportsZeroOffset()
    {
        var shifted = new ShiftedTimeProvider(TimeProvider.System, Ahead);

        shifted.GetUtcNow().Offset.Should().Be(TimeSpan.Zero);
    }

    [TestMethod]
    public void GetUtcNow_SystemInner_IsMonotonicAcrossReads()
    {
        var shifted = new ShiftedTimeProvider(TimeProvider.System, Ahead);

        DateTimeOffset first = shifted.GetUtcNow();
        DateTimeOffset second = shifted.GetUtcNow();

        second.Should().BeOnOrAfter(first);
        first.Should().BeOnOrAfter(DateTimeOffset.UtcNow + Ahead - TimeSpan.FromMinutes(1));
    }

    [TestMethod]
    public void Offset_ReturnsTheConstructedShift()
    {
        var shifted = new ShiftedTimeProvider(new ManualTimeProvider(), Ahead);

        shifted.Offset.Should().Be(Ahead);
    }

    [TestMethod]
    public void Constructor_NullInner_Throws()
    {
        // guard test
        Action act = () => _ = new ShiftedTimeProvider(null!, Ahead);

        act.Should().Throw<ArgumentNullException>().WithParameterName("inner");
    }

    [TestMethod]
    public void TimestampFrequency_DelegatesToTheInnerProvider()
    {
        var inner = new ManualTimeProvider();

        var shifted = new ShiftedTimeProvider(inner, Ahead);

        shifted.TimestampFrequency.Should().Be(inner.TimestampFrequency);
    }

    [TestMethod]
    public void GetTimestamp_AfterTheInnerProviderAdvances_AdvancesWithIt()
    {
        var inner = new ManualTimeProvider();
        var shifted = new ShiftedTimeProvider(inner, Ahead);
        long before = shifted.GetTimestamp();

        inner.Advance(TimeSpan.FromSeconds(2));

        shifted.GetElapsedTime(before).Should().Be(TimeSpan.FromSeconds(2));
    }

    [TestMethod]
    public void LocalTimeZone_DelegatesToTheInnerProvider()
    {
        var inner = new ManualTimeProvider();

        var shifted = new ShiftedTimeProvider(inner, Ahead);

        shifted.LocalTimeZone.Should().Be(inner.LocalTimeZone);
    }

    [TestMethod]
    public async Task CreateTimer_RunsOnTheInnerProvider()
    {
        var inner = new ManualTimeProvider();
        var shifted = new ShiftedTimeProvider(inner, Ahead);
        var fired = new TaskCompletionSource();

        await using ITimer timer = shifted.CreateTimer(_ => fired.TrySetResult(), null, TimeSpan.FromSeconds(1), Timeout.InfiniteTimeSpan);
        inner.Advance(TimeSpan.FromSeconds(1));

        await fired.Task.WaitAsync(TimeSpan.FromSeconds(5));
    }
}
