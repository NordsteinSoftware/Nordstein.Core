using AwesomeAssertions;
using Nordstein.Core.Common.Time;
using Nordstein.Core.Testing.Time;

namespace Nordstein.Core.Testing.Tests.Time;

/// <summary>
/// <see cref="ManualClock"/>: one instant read through <see cref="IClock"/> and
/// <see cref="TimeProvider"/>, moved only by the test; timers that fire only inside
/// <see cref="ManualClock.Advance"/> and a forward <see cref="ManualClock.SetUtcNow"/>, in due
/// order, on the advancing thread.
/// </summary>
[TestClass]
public sealed class ManualClockTests
{
    private static readonly DateTimeOffset Start = new(2030, 6, 1, 12, 0, 0, TimeSpan.Zero);

    [TestMethod]
    public void Constructor_WithoutStart_ReadsTheDefaultStart()
    {
        var clock = new ManualClock();

        clock.UtcNow.Should().Be(ManualClock.DefaultStart);
        ManualClock.DefaultStart.Should().Be(new DateTimeOffset(2000, 1, 1, 0, 0, 0, TimeSpan.Zero));
    }

    [TestMethod]
    public void Constructor_WithAnOffsetStart_ReadsTheSameInstantAtZeroOffset()
    {
        var clock = new ManualClock(new DateTimeOffset(2030, 6, 1, 14, 0, 0, TimeSpan.FromHours(2)));

        clock.UtcNow.Should().Be(Start);
        clock.UtcNow.Offset.Should().Be(TimeSpan.Zero);
        clock.GetUtcNow().Offset.Should().Be(TimeSpan.Zero);
    }

    [TestMethod]
    public void UtcNow_ThroughBothSeams_ReadsTheSameInstant()
    {
        var clock = new ManualClock(Start);
        IClock asClock = clock;
        TimeProvider asProvider = clock;

        asClock.UtcNow.Should().Be(Start);
        asProvider.GetUtcNow().Should().Be(Start);
    }

    [TestMethod]
    public void LocalTimeZone_ByDefault_IsUtc()
        => new ManualClock(Start).LocalTimeZone.Should().Be(TimeZoneInfo.Utc);

    [TestMethod]
    public void LocalTimeZone_WhenGiven_IsThatZoneAndConvertsLocalTime()
    {
        TimeZoneInfo zone = TimeZoneInfo.CreateCustomTimeZone("Test+05", TimeSpan.FromHours(5), "Test+05", "Test+05");

        var clock = new ManualClock(Start, zone);

        clock.LocalTimeZone.Should().BeSameAs(zone);
        clock.GetLocalNow().Should().Be(Start);
        clock.GetLocalNow().Offset.Should().Be(TimeSpan.FromHours(5));
    }

    [TestMethod]
    public void Constructor_WithNullZone_Throws()
    {
#pragma warning disable CS8625 // WHY: the guard under test is reachable only with the null the annotations forbid.
        Action act = () => _ = new ManualClock(Start, null);
#pragma warning restore CS8625

        act.Should().Throw<ArgumentNullException>().WithParameterName("localTimeZone");
    }

    [TestMethod]
    public void Advance_MovesUtcNowAndTheTimestampByTheSameSpan()
    {
        var clock = new ManualClock(Start);
        long before = clock.GetTimestamp();

        clock.Advance(TimeSpan.FromMinutes(90));

        clock.UtcNow.Should().Be(Start.AddMinutes(90));
        clock.GetElapsedTime(before).Should().Be(TimeSpan.FromMinutes(90));
        clock.TimestampFrequency.Should().Be(TimeSpan.TicksPerSecond);
    }

    [TestMethod]
    public void Advance_ByZero_LeavesTheTimeAlone()
    {
        var clock = new ManualClock(Start);

        clock.Advance(TimeSpan.Zero);

        clock.UtcNow.Should().Be(Start);
    }

    [TestMethod]
    public void Advance_ByANegativeSpan_ThrowsAndLeavesTheTimeAlone()
    {
        var clock = new ManualClock(Start);

        Action act = () => clock.Advance(TimeSpan.FromTicks(-1));

        act.Should().Throw<ArgumentOutOfRangeException>().WithParameterName("delta");
        clock.UtcNow.Should().Be(Start);
    }

    [TestMethod]
    public void Advance_PastTheLastRepresentableInstant_ThrowsAndLeavesTheTimeAlone()
    {
        DateTimeOffset nearEnd = DateTimeOffset.MaxValue.AddTicks(-10);
        var clock = new ManualClock(nearEnd);

        Action act = () => clock.Advance(TimeSpan.FromTicks(11));

        act.Should().Throw<ArgumentOutOfRangeException>().WithParameterName("delta");
        clock.UtcNow.Should().Be(nearEnd);
        clock.Advance(TimeSpan.FromTicks(10));
        clock.UtcNow.Should().Be(DateTimeOffset.MaxValue);
    }

    [TestMethod]
    public void Advance_PastTheLongestMonotonicTimestamp_ThrowsAndLeavesTheTimeAlone()
    {
        // Setting the wall clock back keeps the monotonic line growing; after enough round trips
        // across the whole calendar it would overflow a long.
        var clock = new ManualClock(DateTimeOffset.MinValue);
        long fullRange = DateTimeOffset.MaxValue.UtcTicks;
        clock.Advance(TimeSpan.FromTicks(fullRange));
        clock.SetUtcNow(DateTimeOffset.MinValue);
        clock.Advance(TimeSpan.FromTicks(fullRange));
        clock.SetUtcNow(DateTimeOffset.MinValue);
        long timestamp = clock.GetTimestamp();

        Action advance = () => clock.Advance(TimeSpan.FromTicks(fullRange));
        Action set = () => clock.SetUtcNow(DateTimeOffset.MaxValue);

        advance.Should().Throw<ArgumentOutOfRangeException>().WithParameterName("delta");
        set.Should().Throw<ArgumentOutOfRangeException>().WithParameterName("value");
        clock.UtcNow.Should().Be(DateTimeOffset.MinValue);
        clock.GetTimestamp().Should().Be(timestamp);
    }

    [TestMethod]
    public void SetUtcNow_Forward_MovesTheTimestampAndFiresDueTimers()
    {
        var clock = new ManualClock(Start);
        long before = clock.GetTimestamp();
        int fired = 0;
        using ITimer timer = clock.CreateTimer(_ => fired++, null, TimeSpan.FromHours(1), Timeout.InfiniteTimeSpan);

        clock.SetUtcNow(Start.AddHours(2));

        clock.UtcNow.Should().Be(Start.AddHours(2));
        clock.GetElapsedTime(before).Should().Be(TimeSpan.FromHours(2));
        fired.Should().Be(1);
    }

    [TestMethod]
    public void SetUtcNow_Backward_MovesOnlyTheWallClock()
    {
        // A wall-clock correction (an NTP step, clock skew): timestamps and timers measure elapsed
        // time and do not move back, exactly as with TimeProvider.System.
        var clock = new ManualClock(Start);
        long before = clock.GetTimestamp();
        int fired = 0;
        using ITimer timer = clock.CreateTimer(_ => fired++, null, TimeSpan.FromMinutes(10), Timeout.InfiniteTimeSpan);

        clock.SetUtcNow(Start.AddDays(-3));

        clock.UtcNow.Should().Be(Start.AddDays(-3));
        clock.GetTimestamp().Should().Be(before);
        fired.Should().Be(0);

        clock.Advance(TimeSpan.FromMinutes(10));

        fired.Should().Be(1, "the timer was due ten elapsed minutes after it was armed");
        clock.UtcNow.Should().Be(Start.AddDays(-3).AddMinutes(10));
    }

    [TestMethod]
    public void SetUtcNow_WithAnOffset_StoresTheInstantAtZeroOffset()
    {
        var clock = new ManualClock(Start);

        clock.SetUtcNow(new DateTimeOffset(2031, 1, 1, 1, 0, 0, TimeSpan.FromHours(1)));

        clock.UtcNow.Should().Be(new DateTimeOffset(2031, 1, 1, 0, 0, 0, TimeSpan.Zero));
        clock.UtcNow.Offset.Should().Be(TimeSpan.Zero);
    }

    [TestMethod]
    public void CreateTimer_WithNullCallback_Throws()
    {
        var clock = new ManualClock(Start);

#pragma warning disable CS8625 // WHY: the guard under test is reachable only with the null the annotations forbid.
        Action act = () => clock.CreateTimer(null, null, TimeSpan.Zero, Timeout.InfiniteTimeSpan);
#pragma warning restore CS8625

        act.Should().Throw<ArgumentNullException>().WithParameterName("callback");
    }

    [TestMethod]
    [DataRow(-2L, 0L, "dueTime")]
    [DataRow(4294967295L, 0L, "dueTime")]
    [DataRow(0L, -2L, "period")]
    [DataRow(0L, 4294967295L, "period")]
    public void CreateTimer_WithASpanTheSystemTimerRejects_Throws(long dueMilliseconds, long periodMilliseconds, string parameter)
    {
        var clock = new ManualClock(Start);

        Action act = () => clock.CreateTimer(
            _ => { },
            null,
            TimeSpan.FromMilliseconds(dueMilliseconds),
            TimeSpan.FromMilliseconds(periodMilliseconds));

        act.Should().Throw<ArgumentOutOfRangeException>().WithParameterName(parameter);
        clock.HasPendingTimer.Should().BeFalse("a rejected timer is never armed");
    }

    [TestMethod]
    public void CreateTimer_WithTheLongestSpanTheSystemTimerAccepts_IsArmed()
    {
        var clock = new ManualClock(Start);
        TimeSpan longest = TimeSpan.FromMilliseconds(4294967294L);

        using ITimer timer = clock.CreateTimer(_ => { }, null, longest, longest);

        clock.HasPendingTimer.Should().BeTrue();
    }

    [TestMethod]
    public void Timer_FiresOnlyOnceItsDueTimeIsReached_WithItsStateAtTheDueInstant()
    {
        var clock = new ManualClock(Start);
        var seen = new List<(object? State, DateTimeOffset At)>();
        object state = new();
        using ITimer timer = clock.CreateTimer(s => seen.Add((s, clock.UtcNow)), state, TimeSpan.FromSeconds(30), Timeout.InfiniteTimeSpan);

        clock.Advance(TimeSpan.FromSeconds(29));
        seen.Should().BeEmpty();

        clock.Advance(TimeSpan.FromSeconds(31));

        seen.Should().ContainSingle();
        seen[0].State.Should().BeSameAs(state);
        seen[0].At.Should().Be(Start.AddSeconds(30), "the callback observes the instant it was due");
        clock.UtcNow.Should().Be(Start.AddMinutes(1));
        clock.HasPendingTimer.Should().BeFalse("a one-shot timer disarms once it fired");
    }

    [TestMethod]
    public void Timer_DueNow_FiresOnTheNextAdvanceNotWhileBeingCreated()
    {
        var clock = new ManualClock(Start);
        int fired = 0;

        using ITimer timer = clock.CreateTimer(_ => fired++, null, TimeSpan.Zero, Timeout.InfiniteTimeSpan);

        fired.Should().Be(0);
        clock.Advance(TimeSpan.Zero);
        fired.Should().Be(1);
    }

    [TestMethod]
    public void Timers_FireInDueOrder_TiesInTheOrderTheyWereArmed()
    {
        var clock = new ManualClock(Start);
        var order = new List<string>();
        using ITimer late = clock.CreateTimer(_ => order.Add("late"), null, TimeSpan.FromSeconds(3), Timeout.InfiniteTimeSpan);
        using ITimer firstTie = clock.CreateTimer(_ => order.Add("tie-1"), null, TimeSpan.FromSeconds(2), Timeout.InfiniteTimeSpan);
        using ITimer early = clock.CreateTimer(_ => order.Add("early"), null, TimeSpan.FromSeconds(1), Timeout.InfiniteTimeSpan);
        using ITimer secondTie = clock.CreateTimer(_ => order.Add("tie-2"), null, TimeSpan.FromSeconds(2), Timeout.InfiniteTimeSpan);

        clock.Advance(TimeSpan.FromSeconds(5));

        order.Should().Equal("early", "tie-1", "tie-2", "late");
    }

    [TestMethod]
    public void PeriodicTimer_FiresOncePerElapsedPeriod()
    {
        var clock = new ManualClock(Start);
        var at = new List<DateTimeOffset>();
        using ITimer timer = clock.CreateTimer(_ => at.Add(clock.UtcNow), null, TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(5));

        clock.Advance(TimeSpan.FromSeconds(24));

        at.Should().Equal(Start.AddSeconds(10), Start.AddSeconds(15), Start.AddSeconds(20));
        clock.HasPendingTimer.Should().BeTrue("a periodic timer stays armed");
    }

    [TestMethod]
    public void Timer_WithInfiniteDueTime_NeverFires()
    {
        var clock = new ManualClock(Start);
        int fired = 0;
        using ITimer timer = clock.CreateTimer(_ => fired++, null, Timeout.InfiniteTimeSpan, TimeSpan.FromSeconds(1));

        clock.Advance(TimeSpan.FromDays(400));

        fired.Should().Be(0);
        clock.HasPendingTimer.Should().BeFalse();
    }

    [TestMethod]
    public void Timer_Change_RearmsRelativeToNow()
    {
        var clock = new ManualClock(Start);
        int fired = 0;
        using ITimer timer = clock.CreateTimer(_ => fired++, null, TimeSpan.FromSeconds(10), Timeout.InfiniteTimeSpan);
        clock.Advance(TimeSpan.FromSeconds(5));

        timer.Change(TimeSpan.FromSeconds(10), Timeout.InfiniteTimeSpan).Should().BeTrue();
        clock.Advance(TimeSpan.FromSeconds(9));
        fired.Should().Be(0, "the change moved the due time to fifteen seconds after the start");

        clock.Advance(TimeSpan.FromSeconds(1));
        fired.Should().Be(1);
    }

    [TestMethod]
    public void Timer_ChangeToInfinite_Disarms()
    {
        var clock = new ManualClock(Start);
        int fired = 0;
        using ITimer timer = clock.CreateTimer(_ => fired++, null, TimeSpan.FromSeconds(1), Timeout.InfiniteTimeSpan);

        timer.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan).Should().BeTrue();
        clock.Advance(TimeSpan.FromMinutes(1));

        fired.Should().Be(0);
    }

    [TestMethod]
    public void Timer_ChangeWithAnInvalidSpan_ThrowsAndKeepsItsSchedule()
    {
        var clock = new ManualClock(Start);
        int fired = 0;
        using ITimer timer = clock.CreateTimer(_ => fired++, null, TimeSpan.FromSeconds(1), Timeout.InfiniteTimeSpan);

        Action act = () => timer.Change(TimeSpan.FromMilliseconds(-5), Timeout.InfiniteTimeSpan);

        act.Should().Throw<ArgumentOutOfRangeException>().WithParameterName("dueTime");
        clock.Advance(TimeSpan.FromSeconds(1));
        fired.Should().Be(1);
    }

    [TestMethod]
    public async Task Timer_AfterDisposal_NeverFiresAndRefusesChanges()
    {
        var clock = new ManualClock(Start);
        int fired = 0;
        ITimer timer = clock.CreateTimer(_ => fired++, null, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));
        ITimer other = clock.CreateTimer(_ => fired += 100, null, TimeSpan.FromSeconds(1), Timeout.InfiniteTimeSpan);

        timer.Dispose();
        timer.Dispose();
        await other.DisposeAsync();
        clock.Advance(TimeSpan.FromSeconds(10));

        fired.Should().Be(0);
        timer.Change(TimeSpan.Zero, Timeout.InfiniteTimeSpan).Should().BeFalse();
        other.Change(TimeSpan.Zero, Timeout.InfiniteTimeSpan).Should().BeFalse();
        clock.HasPendingTimer.Should().BeFalse();
    }

    [TestMethod]
    public void Timer_DisposedByAnEarlierCallbackOfTheSameAdvance_DoesNotFire()
    {
        var clock = new ManualClock(Start);
        int fired = 0;
        ITimer? victim = null;
        using ITimer killer = clock.CreateTimer(_ => victim?.Dispose(), null, TimeSpan.FromSeconds(1), Timeout.InfiniteTimeSpan);
        victim = clock.CreateTimer(_ => fired++, null, TimeSpan.FromSeconds(2), Timeout.InfiniteTimeSpan);

        clock.Advance(TimeSpan.FromSeconds(3));

        fired.Should().Be(0);
    }

    [TestMethod]
    public void Callback_ArmingATimerDueWithinTheSameAdvance_FiresItInTheSameAdvance()
    {
        // A loop that waits, wakes and waits again: each wait armed from inside the callback is
        // reached by the one Advance that covers it.
        var clock = new ManualClock(Start);
        var at = new List<DateTimeOffset>();
        var timers = new List<ITimer>();
        void Schedule() => timers.Add(clock.CreateTimer(
            _ =>
            {
                at.Add(clock.UtcNow);
                Schedule();
            },
            null,
            TimeSpan.FromMinutes(10),
            Timeout.InfiniteTimeSpan));
        Schedule();

        clock.Advance(TimeSpan.FromMinutes(35));

        at.Should().Equal(Start.AddMinutes(10), Start.AddMinutes(20), Start.AddMinutes(30));
        timers.ForEach(timer => timer.Dispose());
    }

    [TestMethod]
    public void Callback_ThatThrows_PropagatesAndTheNextAdvanceResumes()
    {
        var clock = new ManualClock(Start);
        int laterFired = 0;
        using ITimer failing = clock.CreateTimer(_ => throw new InvalidOperationException("boom"), null, TimeSpan.FromSeconds(1), Timeout.InfiniteTimeSpan);
        using ITimer later = clock.CreateTimer(_ => laterFired++, null, TimeSpan.FromSeconds(2), Timeout.InfiniteTimeSpan);

        Action act = () => clock.Advance(TimeSpan.FromSeconds(5));

        act.Should().Throw<InvalidOperationException>().WithMessage("boom");
        clock.UtcNow.Should().Be(Start.AddSeconds(1), "time stops at the instant of the failing timer");
        laterFired.Should().Be(0);

        clock.Advance(TimeSpan.Zero);

        laterFired.Should().Be(1, "the interrupted advance resumes towards its target");
        clock.UtcNow.Should().Be(Start.AddSeconds(5));
    }

    [TestMethod]
    public void SetUtcNow_AfterAnInterruptedAdvance_MeasuresFromTheInterruptedTarget()
    {
        var clock = new ManualClock(Start);
        using ITimer failing = clock.CreateTimer(_ => throw new InvalidOperationException("boom"), null, TimeSpan.FromSeconds(1), Timeout.InfiniteTimeSpan);
        Action act = () => clock.Advance(TimeSpan.FromSeconds(10));
        act.Should().Throw<InvalidOperationException>();

        clock.SetUtcNow(Start.AddSeconds(20));

        clock.UtcNow.Should().Be(Start.AddSeconds(20));
        clock.GetElapsedTime(0).Should().Be(TimeSpan.FromSeconds(20));
    }

    [TestMethod]
    public void Timer_DueBeyondTheMonotonicRange_IsArmedButNeverReached()
    {
        var clock = new ManualClock(DateTimeOffset.MinValue);
        long fullRange = DateTimeOffset.MaxValue.UtcTicks;
        clock.Advance(TimeSpan.FromTicks(fullRange));
        clock.SetUtcNow(DateTimeOffset.MinValue);
        clock.Advance(TimeSpan.FromTicks(fullRange));
        clock.SetUtcNow(DateTimeOffset.MinValue);
        clock.Advance(TimeSpan.FromTicks(long.MaxValue - 1 - clock.GetTimestamp() - 10));
        int fired = 0;
        using ITimer timer = clock.CreateTimer(_ => fired++, null, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));

        clock.Advance(TimeSpan.FromTicks(10));

        clock.HasPendingTimer.Should().BeTrue();
        fired.Should().Be(0, "a due time past the last timestamp saturates instead of wrapping around");
    }

    [TestMethod]
    public void Callback_ReadingTheClockFromAnotherThread_DoesNotDeadlock()
    {
        // Callbacks never run under the clock's lock: a callback that waits for another thread
        // which reads the clock completes.
        var clock = new ManualClock(Start);
        DateTimeOffset seen = default;
        using ITimer timer = clock.CreateTimer(
            _ => seen = Task.Run(() => clock.GetUtcNow()).GetAwaiter().GetResult(),
            null,
            TimeSpan.FromSeconds(1),
            Timeout.InfiniteTimeSpan);

        clock.Advance(TimeSpan.FromSeconds(1));

        seen.Should().Be(Start.AddSeconds(1));
    }

    [TestMethod]
    public async Task TaskDelay_OnTheClock_CompletesOnlyWhenAdvancedPastIt()
    {
        var clock = new ManualClock(Start);

        Task delay = Task.Delay(TimeSpan.FromSeconds(5), clock, CancellationToken.None);
        clock.Advance(TimeSpan.FromSeconds(4));
        delay.IsCompleted.Should().BeFalse();

        clock.Advance(TimeSpan.FromSeconds(1));

        await delay.WaitAsync(TimeSpan.FromSeconds(10), CancellationToken.None);
        delay.IsCompletedSuccessfully.Should().BeTrue();
    }

    [TestMethod]
    public async Task CancellationTokenSource_OnTheClock_CancelsWhenAdvancedPastItsDelay()
    {
        var clock = new ManualClock(Start);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30), clock);

        clock.Advance(TimeSpan.FromSeconds(29));
        timeout.IsCancellationRequested.Should().BeFalse();
        clock.Advance(TimeSpan.FromSeconds(1));

        timeout.IsCancellationRequested.Should().BeTrue();
        await Task.CompletedTask;
    }

    [TestMethod]
    public async Task PeriodicTimer_OnTheClock_TicksPerAdvancedPeriod()
    {
        var clock = new ManualClock(Start);
        using var periodic = new PeriodicTimer(TimeSpan.FromMinutes(1), clock);

        ValueTask<bool> tick = periodic.WaitForNextTickAsync(CancellationToken.None);
        tick.IsCompleted.Should().BeFalse();
        clock.Advance(TimeSpan.FromMinutes(1));

        (await tick.AsTask().WaitAsync(TimeSpan.FromSeconds(10), CancellationToken.None)).Should().BeTrue();
    }

    [TestMethod]
    public async Task WaitForPendingTimerAsync_WhenATimerIsAlreadyArmed_CompletesAtOnce()
    {
        var clock = new ManualClock(Start);
        using ITimer timer = clock.CreateTimer(_ => { }, null, TimeSpan.FromSeconds(1), Timeout.InfiniteTimeSpan);

        Task wait = clock.WaitForPendingTimerAsync(CancellationToken.None);

        wait.IsCompletedSuccessfully.Should().BeTrue();
        await wait;
    }

    [TestMethod]
    public async Task WaitForPendingTimerAsync_CompletesOnceCodeUnderTestArmsATimer()
    {
        var clock = new ManualClock(Start);
        Task wait = clock.WaitForPendingTimerAsync(CancellationToken.None);
        wait.IsCompleted.Should().BeFalse();

        Task delay = Task.Run(() => Task.Delay(TimeSpan.FromHours(1), clock, CancellationToken.None));

        await wait.WaitAsync(TimeSpan.FromSeconds(10), CancellationToken.None);
        clock.HasPendingTimer.Should().BeTrue();
        clock.Advance(TimeSpan.FromHours(1));
        await delay.WaitAsync(TimeSpan.FromSeconds(10), CancellationToken.None);
    }

    [TestMethod]
    public async Task WaitForPendingTimerAsync_ArmedByAChange_Completes()
    {
        var clock = new ManualClock(Start);
        using ITimer timer = clock.CreateTimer(_ => { }, null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        Task wait = clock.WaitForPendingTimerAsync(CancellationToken.None);
        wait.IsCompleted.Should().BeFalse("an unarmed timer is not pending");

        timer.Change(TimeSpan.FromSeconds(1), Timeout.InfiniteTimeSpan);

        await wait.WaitAsync(TimeSpan.FromSeconds(10), CancellationToken.None);
    }

    [TestMethod]
    public async Task WaitForPendingTimerAsync_WhenCancelled_ThrowsOperationCanceled()
    {
        var clock = new ManualClock(Start);
        using var cancel = new CancellationTokenSource();
        Task wait = clock.WaitForPendingTimerAsync(cancel.Token);

        await cancel.CancelAsync();

        await FluentActions.Awaiting(() => wait).Should().ThrowAsync<OperationCanceledException>();
    }

    [TestMethod]
    public async Task WaitForPendingTimerAsync_WithAnAlreadyCancelledToken_ThrowsEvenWhenATimerIsArmed()
    {
        var clock = new ManualClock(Start);
        using ITimer timer = clock.CreateTimer(_ => { }, null, TimeSpan.FromSeconds(1), Timeout.InfiniteTimeSpan);

        await FluentActions
            .Awaiting(() => clock.WaitForPendingTimerAsync(new CancellationToken(canceled: true)))
            .Should().ThrowAsync<OperationCanceledException>();
    }

    [TestMethod]
    public async Task ConcurrentAdvances_AddUpAndFireEveryTimerExactlyOnce()
    {
        var clock = new ManualClock(Start);
        const int Threads = 8;
        const int StepsPerThread = 500;
        int[] fired = new int[Threads * StepsPerThread];
        var timers = new List<ITimer>();
        for (int i = 0; i < fired.Length; i++)
        {
            int index = i;
            timers.Add(clock.CreateTimer(_ => Interlocked.Increment(ref fired[index]), null, TimeSpan.FromTicks(i + 1), Timeout.InfiniteTimeSpan));
        }

        using var go = new ManualResetEventSlim();
        Task[] advancers = Enumerable.Range(0, Threads)
            .Select(_ => Task.Run(() =>
            {
                go.Wait();
                for (int step = 0; step < StepsPerThread; step++)
                {
                    clock.Advance(TimeSpan.FromTicks(1));
                }
            }))
            .ToArray();
        Task readers = Task.Run(() =>
        {
            go.Wait();
            DateTimeOffset previous = clock.UtcNow;
            for (int read = 0; read < 10_000; read++)
            {
                DateTimeOffset now = clock.UtcNow;
                now.Should().BeOnOrAfter(previous, "time never runs backwards under concurrent advances");
                previous = now;
            }
        });
        go.Set();
        await Task.WhenAll([.. advancers, readers]).WaitAsync(TimeSpan.FromSeconds(30), CancellationToken.None);

        clock.UtcNow.Should().Be(Start.AddTicks(Threads * StepsPerThread));
        fired.Should().OnlyContain(count => count == 1);
        timers.ForEach(timer => timer.Dispose());
    }

    [TestMethod]
    public async Task ConcurrentTimerCreationWhileAdvancing_LosesNoTimer()
    {
        var clock = new ManualClock(Start);
        int fired = 0;
        var timers = new System.Collections.Concurrent.ConcurrentBag<ITimer>();

        Task creators = Task.WhenAll(Enumerable.Range(0, 4).Select(_ => Task.Run(() =>
        {
            for (int i = 0; i < 250; i++)
            {
                timers.Add(clock.CreateTimer(_ => Interlocked.Increment(ref fired), null, TimeSpan.FromSeconds(1), Timeout.InfiniteTimeSpan));
            }
        })));
        Task advancer = Task.Run(() =>
        {
            for (int i = 0; i < 100; i++)
            {
                clock.Advance(TimeSpan.FromMilliseconds(1));
            }
        });
        await Task.WhenAll(creators, advancer).WaitAsync(TimeSpan.FromSeconds(30), CancellationToken.None);

        clock.Advance(TimeSpan.FromSeconds(1));

        fired.Should().Be(1000, "every timer is due at most one second after it was armed");
        foreach (ITimer timer in timers)
        {
            timer.Dispose();
        }
    }
}
