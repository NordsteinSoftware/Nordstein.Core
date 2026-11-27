using Nordstein.Core.Common.Time;

namespace Nordstein.Core.Testing.Time;

/// <summary>
/// A clock that moves only when a test moves it: one instant served through both of Core's time
/// seams, <see cref="IClock"/> ("what time is it" in feature code) and <see cref="TimeProvider"/>
/// (delays, timeouts, timers and timestamps), so a test registers one instance for both and
/// never waits for real time.
/// </summary>
/// <remarks>
/// <para>
/// <b>Two time lines, as on a real machine.</b> The wall clock (<see cref="UtcNow"/>,
/// <see cref="GetUtcNow"/>) is what code reads as "now"; the monotonic line
/// (<see cref="GetTimestamp"/>) is what timers and elapsed-time measurements run on.
/// <see cref="Advance"/> moves both by the same span. <see cref="SetUtcNow"/> forward does the
/// same; <see cref="SetUtcNow"/> backward is a wall-clock correction (an NTP step, clock skew)
/// that leaves timestamps and timers where they are, exactly as <see cref="TimeProvider.System"/>
/// behaves when the system clock is set back.
/// </para>
/// <para>
/// <b>Timers.</b> A timer from <see cref="CreateTimer"/> (which also backs
/// <c>Task.Delay(…, timeProvider)</c>, <c>new CancellationTokenSource(delay, timeProvider)</c>
/// and <see cref="PeriodicTimer"/>) fires only inside <see cref="Advance"/> or a forward
/// <see cref="SetUtcNow"/>, synchronously, on the advancing thread, earliest due first; timers due
/// at the same instant fire in the order they were armed. While a callback runs, the clock reads
/// that timer's due instant. A timer armed with a zero due time fires on the next advance (even
/// <c>Advance(TimeSpan.Zero)</c>), never while it is being created. A periodic timer fires once
/// per elapsed period. A callback may create, change or dispose timers, including itself; a timer
/// it arms that falls due within the same advance fires in that advance. An exception thrown by a
/// callback propagates out of the advancing call with the clock at that timer's due instant; the
/// next advance resumes towards the interrupted target before adding its own span.
/// </para>
/// <para>
/// <b>Thread-safety.</b> Every member is safe to call concurrently from any thread. Callbacks
/// never run under the clock's internal lock, so a callback may read the clock or wait for a
/// thread that does. Concurrent advances add up (two threads each advancing one second move the
/// clock two seconds) and every due timer fires exactly once, on one of the advancing threads; the
/// due order holds per advancing thread.
/// </para>
/// <para>
/// Register one instance as both seams: <c>builder.RegisterInstance(clock).As&lt;IClock&gt;().As&lt;TimeProvider&gt;()</c>.
/// </para>
/// </remarks>
public sealed class ManualClock : TimeProvider, IClock
{
    /// <summary>
    /// The instant a clock created without a start reads: 2000-01-01T00:00:00Z.
    /// </summary>
    public static readonly DateTimeOffset DefaultStart = new(2000, 1, 1, 0, 0, 0, TimeSpan.Zero);

    /// <summary>The longest due time or period the system timer accepts: 4,294,967,294 ms.</summary>
    private static readonly TimeSpan LongestTimerSpan = TimeSpan.FromMilliseconds(uint.MaxValue - 1.0);

    /// <summary>
    /// The furthest the monotonic timestamp moves; one below <see cref="long.MaxValue"/>, which
    /// marks a due time no advance reaches.
    /// </summary>
    private const long LastTimestamp = long.MaxValue - 1;

    private readonly Lock gate = new();
    private readonly List<ManualTimer> timers = [];
    private readonly TimeZoneInfo localTimeZone;

    private long wallTicks;
    private long elapsedTicks;
    private long targetElapsedTicks;
    private long armSequence;
    private TaskCompletionSource? timerArmed;

    /// <summary>
    /// Creates a clock reading <see cref="DefaultStart"/>, in the UTC time zone.
    /// </summary>
    public ManualClock()
        : this(DefaultStart)
    {
    }

    /// <summary>
    /// Creates a clock reading <paramref name="start"/>, in the UTC time zone.
    /// </summary>
    /// <param name="start">The first instant the clock reads; any offset, kept as the same instant at offset zero.</param>
    public ManualClock(DateTimeOffset start)
        : this(start, TimeZoneInfo.Utc)
    {
    }

    /// <summary>
    /// Creates a clock reading <paramref name="start"/>, whose <see cref="LocalTimeZone"/> is
    /// <paramref name="localTimeZone"/>.
    /// </summary>
    /// <param name="start">The first instant the clock reads; any offset, kept as the same instant at offset zero.</param>
    /// <param name="localTimeZone">The zone <see cref="TimeProvider.GetLocalNow"/> converts to.</param>
    /// <exception cref="ArgumentNullException"><paramref name="localTimeZone"/> is null.</exception>
    public ManualClock(DateTimeOffset start, TimeZoneInfo localTimeZone)
    {
        ArgumentNullException.ThrowIfNull(localTimeZone);
        this.localTimeZone = localTimeZone;
        wallTicks = start.UtcTicks;
    }

    /// <summary>
    /// The current instant, at offset zero; the same as <see cref="GetUtcNow"/>.
    /// </summary>
    public DateTimeOffset UtcNow => GetUtcNow();

    /// <summary>
    /// The zone given at construction; UTC unless one was given.
    /// </summary>
    public override TimeZoneInfo LocalTimeZone => localTimeZone;

    /// <summary>
    /// <see cref="TimeSpan.TicksPerSecond"/>: a timestamp is a count of 100-nanosecond ticks.
    /// </summary>
    public override long TimestampFrequency => TimeSpan.TicksPerSecond;

    /// <summary>
    /// Whether at least one timer is armed, that is, waiting for an advance to reach its due time.
    /// </summary>
    public bool HasPendingTimer
    {
        get
        {
            lock (gate)
            {
                return timers.Exists(timer => timer.IsArmed);
            }
        }
    }

    /// <summary>
    /// The current instant, at offset zero.
    /// </summary>
    public override DateTimeOffset GetUtcNow()
    {
        lock (gate)
        {
            return new DateTimeOffset(wallTicks, TimeSpan.Zero);
        }
    }

    /// <summary>
    /// The monotonic position in ticks of <see cref="TimestampFrequency"/>: it starts at zero and
    /// moves only with <see cref="Advance"/> and a forward <see cref="SetUtcNow"/>.
    /// </summary>
    public override long GetTimestamp()
    {
        lock (gate)
        {
            return elapsedTicks;
        }
    }

    /// <summary>
    /// Creates a timer that fires when an advance reaches <paramref name="dueTime"/> from now and
    /// then every <paramref name="period"/>; see the type's remarks for when callbacks run.
    /// </summary>
    /// <param name="callback">Invoked with <paramref name="state"/> each time the timer fires.</param>
    /// <param name="state">Handed to <paramref name="callback"/>; may be null.</param>
    /// <param name="dueTime">Time until the first firing; <see cref="Timeout.InfiniteTimeSpan"/> leaves the timer unarmed.</param>
    /// <param name="period">Time between firings; <see cref="TimeSpan.Zero"/> or <see cref="Timeout.InfiniteTimeSpan"/> fires once.</param>
    /// <returns>The timer; dispose it to stop it. Disposal is idempotent.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="callback"/> is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="dueTime"/> or <paramref name="period"/> is negative (other than
    /// <see cref="Timeout.InfiniteTimeSpan"/>) or longer than 4,294,967,294 milliseconds, the
    /// spans the system timer rejects too.
    /// </exception>
    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        ArgumentNullException.ThrowIfNull(callback);
        ValidateTimerSpans(dueTime, period);

        var timer = new ManualTimer(this, callback, state);
        lock (gate)
        {
            timers.Add(timer);
            ArmLocked(timer, dueTime, period);
        }

        return timer;
    }

    /// <summary>
    /// Moves both time lines forward by <paramref name="delta"/> and fires every timer that falls
    /// due on the way, earliest first.
    /// </summary>
    /// <param name="delta">How far to move; zero fires the timers already due.</param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="delta"/> is negative, or moving by it would pass
    /// <see cref="DateTimeOffset.MaxValue"/>; the clock does not move.
    /// </exception>
    public void Advance(TimeSpan delta)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(delta, TimeSpan.Zero);
        lock (gate)
        {
            long pending = targetElapsedTicks - elapsedTicks;
            if (delta.Ticks > DateTimeOffset.MaxValue.UtcTicks - wallTicks - pending
                || delta.Ticks > LastTimestamp - targetElapsedTicks)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(delta),
                    delta,
                    "Advancing by this span would move the clock past DateTimeOffset.MaxValue or overflow its monotonic timestamp.");
            }

            targetElapsedTicks += delta.Ticks;
        }

        FireDueTimers();
    }

    /// <summary>
    /// Sets the wall clock to <paramref name="value"/>. Forward, this is
    /// <see cref="Advance"/> by the difference, timers included; backward, it moves only the wall
    /// clock, leaving <see cref="GetTimestamp"/> and every timer's due time unchanged. The
    /// difference is measured from the instant the clock reaches once an advance in progress (or
    /// one a throwing callback interrupted) completes.
    /// </summary>
    /// <param name="value">The instant to read from now on; any offset, kept at offset zero.</param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Moving forward to <paramref name="value"/> would overflow the monotonic timestamp (only
    /// after the wall clock was set back across most of the representable range more than once);
    /// the clock does not move.
    /// </exception>
    public void SetUtcNow(DateTimeOffset value)
    {
        lock (gate)
        {
            long pending = targetElapsedTicks - elapsedTicks;
            long difference = value.UtcTicks - wallTicks - pending;
            if (difference < 0)
            {
                wallTicks += difference;
                return;
            }

            if (difference > LastTimestamp - targetElapsedTicks)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(value),
                    value,
                    "Moving forward to this instant would overflow the clock's monotonic timestamp.");
            }

            targetElapsedTicks += difference;
        }

        FireDueTimers();
    }

    /// <summary>
    /// Completes once at least one timer is armed; at once if one already is. Use it instead of
    /// sleeping to learn that code under test has reached its next wait.
    /// </summary>
    /// <param name="cancellationToken">Abandons the wait.</param>
    /// <returns>A task that completes when a timer is armed.</returns>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled first.</exception>
    public Task WaitForPendingTimerAsync(CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return Task.FromCanceled(cancellationToken);
        }

        Task armed;
        lock (gate)
        {
            if (timers.Exists(timer => timer.IsArmed))
            {
                armed = Task.CompletedTask;
            }
            else
            {
                timerArmed ??= new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                armed = timerArmed.Task;
            }
        }

        return armed.WaitAsync(cancellationToken);
    }

    /// <summary>
    /// <paramref name="ticks"/> plus <paramref name="span"/> (both non-negative), or
    /// <see cref="long.MaxValue"/> where the sum would pass <see cref="LastTimestamp"/>: a due time
    /// no advance can reach, so it never fires sooner than it should and never fires in a loop.
    /// </summary>
    private static long SaturatingAdd(long ticks, long span)
        => span > LastTimestamp - ticks ? long.MaxValue : ticks + span;

    private static void ValidateTimerSpans(TimeSpan dueTime, TimeSpan period)
    {
        if ((dueTime < TimeSpan.Zero && dueTime != Timeout.InfiniteTimeSpan) || dueTime > LongestTimerSpan)
        {
            throw new ArgumentOutOfRangeException(
                nameof(dueTime), dueTime, "A due time is zero or positive up to 4,294,967,294 ms, or Timeout.InfiniteTimeSpan.");
        }

        if ((period < TimeSpan.Zero && period != Timeout.InfiniteTimeSpan) || period > LongestTimerSpan)
        {
            throw new ArgumentOutOfRangeException(
                nameof(period), period, "A period is zero or positive up to 4,294,967,294 ms, or Timeout.InfiniteTimeSpan.");
        }
    }

    /// <summary>
    /// Fires due timers one at a time until none is due at or before the target, then moves the
    /// clock to the target. The lock is held only to pick the next timer, never across a callback.
    /// </summary>
    private void FireDueTimers()
    {
        while (true)
        {
            TimerCallback callback;
            object? state;
            lock (gate)
            {
                ManualTimer? next = null;
                foreach (ManualTimer timer in timers)
                {
                    if (timer.IsArmed
                        && timer.DueTicks <= targetElapsedTicks
                        && (next is null
                            || timer.DueTicks < next.DueTicks
                            || (timer.DueTicks == next.DueTicks && timer.Sequence < next.Sequence)))
                    {
                        next = timer;
                    }
                }

                if (next is null)
                {
                    MoveLocked(targetElapsedTicks);
                    return;
                }

                MoveLocked(Math.Max(elapsedTicks, next.DueTicks));
                if (next.PeriodTicks > 0)
                {
                    next.DueTicks = SaturatingAdd(next.DueTicks, next.PeriodTicks);
                }
                else
                {
                    next.Disarm();
                }

                callback = next.Callback;
                state = next.State;
            }

            callback(state);
        }
    }

    private void MoveLocked(long elapsed)
    {
        wallTicks += elapsed - elapsedTicks;
        elapsedTicks = elapsed;
    }

    private void ArmLocked(ManualTimer timer, TimeSpan dueTime, TimeSpan period)
    {
        timer.PeriodTicks = period == Timeout.InfiniteTimeSpan ? 0 : period.Ticks;
        if (dueTime == Timeout.InfiniteTimeSpan)
        {
            timer.Disarm();
            return;
        }

        timer.Arm(SaturatingAdd(elapsedTicks, dueTime.Ticks), ++armSequence);
        if (timerArmed is { } signal)
        {
            timerArmed = null;
            signal.TrySetResult();
        }
    }

    private bool ChangeTimer(ManualTimer timer, TimeSpan dueTime, TimeSpan period)
    {
        ValidateTimerSpans(dueTime, period);
        lock (gate)
        {
            if (!timers.Contains(timer))
            {
                return false;
            }

            ArmLocked(timer, dueTime, period);
            return true;
        }
    }

    private void RemoveTimer(ManualTimer timer)
    {
        lock (gate)
        {
            timers.Remove(timer);
            timer.Disarm();
        }
    }

    /// <summary>
    /// A timer of this clock. Its schedule fields are read and written only under the clock's lock.
    /// </summary>
    private sealed class ManualTimer : ITimer
    {
        private const long Unarmed = -1;

        private readonly ManualClock owner;

        public ManualTimer(ManualClock owner, TimerCallback callback, object? state)
        {
            this.owner = owner;
            Callback = callback;
            State = state;
        }

        public TimerCallback Callback { get; }

        public object? State { get; }

        public long DueTicks { get; set; } = Unarmed;

        public long PeriodTicks { get; set; }

        public long Sequence { get; private set; }

        public bool IsArmed => DueTicks != Unarmed;

        public void Arm(long dueTicks, long sequence)
        {
            DueTicks = dueTicks;
            Sequence = sequence;
        }

        public void Disarm() => DueTicks = Unarmed;

        public bool Change(TimeSpan dueTime, TimeSpan period) => owner.ChangeTimer(this, dueTime, period);

        public void Dispose() => owner.RemoveTimer(this);

        public ValueTask DisposeAsync()
        {
            Dispose();
            return ValueTask.CompletedTask;
        }
    }
}
