namespace Nordstein.Core.Common.Hosting;

/// <summary>
/// When an <see cref="IPeriodicJob"/> runs.
/// </summary>
/// <remarks>
/// Immutable and thread-safe. The interval is measured from the <b>end</b> of one pass to the
/// start of the next, so a slow pass delays the next one instead of overlapping it.
/// </remarks>
public sealed record PeriodicJobSchedule
{
    private readonly TimeSpan interval;

    /// <summary>
    /// The time between the end of one pass and the start of the next.
    /// </summary>
    /// <remarks>
    /// The maximum is 4,294,967,294 milliseconds (about 49.7 days), the longest delay
    /// <see cref="Task.Delay(TimeSpan, TimeProvider, CancellationToken)"/> accepts; a longer wait
    /// would fail the loop at its first wait.
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException">
    /// The value is zero or negative, <see cref="Timeout.InfiniteTimeSpan"/> included, or is
    /// longer than 4,294,967,294 milliseconds.
    /// </exception>
    public required TimeSpan Interval
    {
        get => interval;
        init
        {
            ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(value, TimeSpan.Zero, nameof(Interval));
            // The longest delay Task.Delay(TimeSpan, TimeProvider, CancellationToken) accepts.
            ArgumentOutOfRangeException.ThrowIfGreaterThan(
                value, TimeSpan.FromMilliseconds(uint.MaxValue - 1), nameof(Interval));
            interval = value;
        }
    }

    /// <summary>
    /// Whether the first pass runs as soon as the host starts (the default) or only after one
    /// <see cref="Interval"/>.
    /// </summary>
    public bool RunAtStart { get; init; } = true;
}
