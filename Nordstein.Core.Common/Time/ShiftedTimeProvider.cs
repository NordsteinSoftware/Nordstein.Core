namespace Nordstein.Core.Common.Time;

/// <summary>
/// A <see cref="TimeProvider"/> that reads an inner provider plus a fixed offset: <see cref="GetUtcNow"/>
/// is the inner instant shifted by <c>offset</c>, so a host's "today" can be a chosen date while
/// time still passes at the inner provider's pace. Timestamps, the local time zone and timers come
/// from the inner provider unchanged, so delays and timeouts keep their real length. Also an
/// <see cref="IClock"/> reading the same shifted instant. Every process that shares the offset
/// (a server and the CLI beside it) reads the same shifted time, which a shift anchored at each
/// process's start would not give. For a demonstration or test host; never for production time.
/// </summary>
/// <remarks>
/// Immutable after construction and safe for concurrent readers: every call reads the inner
/// provider and adds the constant offset. The result always carries a zero offset (UTC).
/// </remarks>
public sealed class ShiftedTimeProvider : TimeProvider, IClock
{
    private readonly TimeProvider inner;
    private readonly TimeSpan offset;

    /// <summary>
    /// Creates a provider reading <paramref name="inner"/>'s UTC instant plus <paramref name="offset"/>.
    /// </summary>
    /// <param name="inner">The provider that supplies the instant, pace, timestamps, time zone and timers.</param>
    /// <param name="offset">The shift, positive into the future or negative into the past.</param>
    /// <exception cref="ArgumentNullException"><paramref name="inner"/> is null.</exception>
    public ShiftedTimeProvider(TimeProvider inner, TimeSpan offset)
    {
        ArgumentNullException.ThrowIfNull(inner);
        this.inner = inner;
        this.offset = offset;
    }

    /// <summary>The shift this provider adds to the inner provider's instant.</summary>
    public TimeSpan Offset => offset;

    /// <inheritdoc cref="IClock.UtcNow" />
    public DateTimeOffset UtcNow => GetUtcNow();

    /// <inheritdoc />
    public override TimeZoneInfo LocalTimeZone => inner.LocalTimeZone;

    /// <inheritdoc />
    public override long TimestampFrequency => inner.TimestampFrequency;

    /// <summary>The inner provider's UTC instant plus <see cref="Offset"/>, at zero offset.</summary>
    public override DateTimeOffset GetUtcNow() => inner.GetUtcNow().ToUniversalTime() + offset;

    /// <inheritdoc />
    public override long GetTimestamp() => inner.GetTimestamp();

    /// <inheritdoc />
    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        => inner.CreateTimer(callback, state, dueTime, period);
}
