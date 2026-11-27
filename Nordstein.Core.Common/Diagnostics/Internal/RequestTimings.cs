using Nordstein.Core.Common.Async;

namespace Nordstein.Core.Common.Diagnostics.Internal;

/// <summary>
/// The scoped accumulator behind <see cref="IRequestTimings"/>: three dictionaries under one
/// <see cref="IAsyncLock"/> key, held only for a dictionary update or a copy, so a snapshot is consistent.
/// </summary>
internal sealed class RequestTimings : IRequestTimings
{
    private readonly object gate = new();
    private readonly IAsyncLock asyncLock;
    private readonly Dictionary<string, TimeSpan> durations = new(StringComparer.Ordinal);
    private readonly Dictionary<string, long> counts = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> values = new(StringComparer.Ordinal);

    public RequestTimings(IAsyncLock asyncLock)
    {
        ArgumentNullException.ThrowIfNull(asyncLock);
        this.asyncLock = asyncLock;
    }

    public void Mark(string name, TimeSpan duration)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentOutOfRangeException.ThrowIfLessThan(duration, TimeSpan.Zero);
        using IDisposable held = asyncLock.Lock(gate);
        TimeSpan sum = durations.GetValueOrDefault(name);
        durations[name] = duration > TimeSpan.MaxValue - sum ? TimeSpan.MaxValue : sum + duration;
    }

    public void Count(string name, long by = 1)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentOutOfRangeException.ThrowIfNegative(by);
        using IDisposable held = asyncLock.Lock(gate);
        long sum = counts.GetValueOrDefault(name);
        counts[name] = by > long.MaxValue - sum ? long.MaxValue : sum + by;
    }

    public void Set(string name, string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(value);
        using IDisposable held = asyncLock.Lock(gate);
        values[name] = value;
    }

    public RequestTimingSnapshot Snapshot()
    {
        using IDisposable held = asyncLock.Lock(gate);
        return new RequestTimingSnapshot(
            new Dictionary<string, TimeSpan>(durations, StringComparer.Ordinal),
            new Dictionary<string, long>(counts, StringComparer.Ordinal),
            new Dictionary<string, string>(values, StringComparer.Ordinal));
    }
}
