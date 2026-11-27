using System.Collections.Concurrent;

namespace Nordstein.Core.Common.Tests.Hosting.Internal;

/// <summary>
/// A <see cref="TimeProvider"/> whose clock moves only when a test calls <see cref="Advance"/>.
/// Timers created through it fire synchronously, in due order, on the thread that advances time.
/// </summary>
/// <remarks>
/// Thread-safe for creating, changing and disposing timers from any thread. <see cref="Advance"/>
/// must be called from one thread at a time (a test body does this naturally).
/// </remarks>
internal sealed class ManualTimeProvider : TimeProvider
{
    private const long NotArmed = -1;

    private readonly ConcurrentDictionary<ManualTimer, byte> timers = new();

    private long utcTicks = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero).UtcTicks;

    private TaskCompletionSource timerChanged = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <inheritdoc />
    public override long TimestampFrequency => TimeSpan.TicksPerSecond;

    /// <summary>
    /// Whether at least one timer is armed, that is, waiting for <see cref="Advance"/> to reach it.
    /// </summary>
    public bool HasPendingTimer => timers.Keys.Any(timer => timer.DueTicks != NotArmed);

    /// <inheritdoc />
    public override DateTimeOffset GetUtcNow() => new(Volatile.Read(ref utcTicks), TimeSpan.Zero);

    /// <inheritdoc />
    public override long GetTimestamp() => Volatile.Read(ref utcTicks);

    /// <inheritdoc />
    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        ArgumentNullException.ThrowIfNull(callback);
        var timer = new ManualTimer(this, callback, state);
        timers[timer] = 0;
        timer.Change(dueTime, period);
        return timer;
    }

    /// <summary>
    /// Moves the clock forward and fires every timer that falls due, earliest first. A periodic
    /// timer fires once per elapsed period.
    /// </summary>
    /// <param name="delta">How far to move the clock; must not be negative.</param>
    public void Advance(TimeSpan delta)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(delta, TimeSpan.Zero);
        long target = Volatile.Read(ref utcTicks) + delta.Ticks;
        while (true)
        {
            ManualTimer? next = timers.Keys
                .Where(timer => timer.DueTicks != NotArmed && timer.DueTicks <= target)
                .MinBy(timer => timer.DueTicks);
            if (next is null)
            {
                break;
            }

            Volatile.Write(ref utcTicks, Math.Max(Volatile.Read(ref utcTicks), next.DueTicks));
            next.Fire();
        }

        Volatile.Write(ref utcTicks, target);
    }

    /// <summary>
    /// Completes once at least one timer is armed. Use it instead of sleeping to learn that the code
    /// under test has reached its next wait.
    /// </summary>
    /// <param name="cancellationToken">Abandons the wait.</param>
    public async Task WaitForPendingTimerAsync(CancellationToken cancellationToken)
    {
        while (true)
        {
            // Read the signal before the state: a change between the two completes this very signal.
            Task changed = Volatile.Read(ref timerChanged).Task;
            if (HasPendingTimer)
            {
                return;
            }

            await changed.WaitAsync(cancellationToken);
        }
    }

    private void SignalTimerChanged() =>
        Interlocked.Exchange(
            ref timerChanged,
            new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously)).TrySetResult();

    private sealed class ManualTimer : ITimer
    {
        private readonly ManualTimeProvider owner;
        private readonly TimerCallback callback;
        private readonly object? state;

        private long dueTicks = NotArmed;
        private long periodTicks;

        public ManualTimer(ManualTimeProvider owner, TimerCallback callback, object? state)
        {
            this.owner = owner;
            this.callback = callback;
            this.state = state;
        }

        public long DueTicks => Volatile.Read(ref dueTicks);

        public bool Change(TimeSpan dueTime, TimeSpan period)
        {
            if (!owner.timers.ContainsKey(this))
            {
                return false;
            }

            Volatile.Write(ref periodTicks, period == Timeout.InfiniteTimeSpan ? 0 : period.Ticks);
            Volatile.Write(
                ref dueTicks,
                dueTime == Timeout.InfiniteTimeSpan
                    ? NotArmed
                    : Volatile.Read(ref owner.utcTicks) + dueTime.Ticks);
            owner.SignalTimerChanged();
            return true;
        }

        public void Fire()
        {
            long period = Volatile.Read(ref periodTicks);
            Volatile.Write(ref dueTicks, period > 0 ? DueTicks + period : NotArmed);
            callback(state);
        }

        public void Dispose()
        {
            Volatile.Write(ref dueTicks, NotArmed);
            owner.timers.TryRemove(this, out _);
        }

        public ValueTask DisposeAsync()
        {
            Dispose();
            return ValueTask.CompletedTask;
        }
    }
}
