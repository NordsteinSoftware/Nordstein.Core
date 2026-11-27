using System.Globalization;

namespace Nordstein.Core.Testing.Performance;

/// <summary>
/// The latencies of one performance scenario's timed operations and the percentiles a budget is
/// written against.
/// </summary>
/// <remarks>
/// <para>
/// Budgets are set on p95 rather than the mean: the regressions a performance suite exists to catch
/// (a lost index, a client-side evaluation, a plan linear in the rows) move the tail long before
/// they move the average. Percentiles are nearest-rank, so a reported p95 is a latency an operation
/// actually took, never an interpolation between two samples.
/// </para>
/// <para>
/// A measurement with no samples has no statistics: reading one throws, so a scenario that timed
/// nothing fails instead of passing every budget with a p95 of zero.
/// </para>
/// <para>
/// <b>Thread-safety.</b> Recording, timing and reading are safe from any thread, concurrently:
/// a contention scenario may have many writers time into one instance.
/// </para>
/// </remarks>
public sealed class Measurement
{
    private readonly Lock gate = new();
    private readonly List<double> samplesMs = [];
    private readonly TimeProvider timeProvider;

    /// <summary>
    /// Creates an empty measurement timed on <see cref="TimeProvider.System"/>.
    /// </summary>
    public Measurement()
        : this(TimeProvider.System)
    {
    }

    /// <summary>
    /// Creates an empty measurement whose <see cref="TimeAsync{T}"/> reads
    /// <paramref name="timeProvider"/>'s timestamps.
    /// </summary>
    /// <param name="timeProvider">The source of timestamps.</param>
    /// <exception cref="ArgumentNullException"><paramref name="timeProvider"/> is null.</exception>
    public Measurement(TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(timeProvider);
        this.timeProvider = timeProvider;
    }

    /// <summary>How many operations were recorded.</summary>
    public int Count
    {
        get
        {
            lock (gate)
            {
                return samplesMs.Count;
            }
        }
    }

    /// <summary>The median latency in milliseconds.</summary>
    /// <exception cref="InvalidOperationException">Nothing was recorded.</exception>
    public double P50Ms => Percentile(0.50);

    /// <summary>The 95th-percentile latency in milliseconds: what budgets are written against.</summary>
    /// <exception cref="InvalidOperationException">Nothing was recorded.</exception>
    public double P95Ms => Percentile(0.95);

    /// <summary>The slowest operation in milliseconds.</summary>
    /// <exception cref="InvalidOperationException">Nothing was recorded.</exception>
    public double MaxMs => Percentile(1.0);

    /// <summary>The mean latency in milliseconds.</summary>
    /// <exception cref="InvalidOperationException">Nothing was recorded.</exception>
    public double MeanMs
    {
        get
        {
            lock (gate)
            {
                ThrowIfEmptyLocked();
                return samplesMs.Average();
            }
        }
    }

    /// <summary>
    /// The nearest-rank percentile: the smallest recorded latency at or above the
    /// <paramref name="quantile"/> rank, in milliseconds.
    /// </summary>
    /// <param name="quantile">The rank as a fraction, greater than 0 and at most 1 (0.95 for p95).</param>
    /// <returns>A recorded latency.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="quantile"/> is not in (0, 1].</exception>
    /// <exception cref="InvalidOperationException">Nothing was recorded.</exception>
    public double Percentile(double quantile)
    {
        if (!(quantile > 0 && quantile <= 1))
        {
            throw new ArgumentOutOfRangeException(nameof(quantile), quantile, "A quantile is greater than 0 and at most 1.");
        }

        lock (gate)
        {
            ThrowIfEmptyLocked();
            double[] sorted = [.. samplesMs.Order()];

            // In decimal, so a quantile such as 0.07 is exact: 0.07 * 100 in binary floating point
            // is 7.000000000000001, whose ceiling would pick the next rank.
            int rank = (int)Math.Ceiling((decimal)quantile * sorted.Length);
            return sorted[Math.Max(rank, 1) - 1];
        }
    }

    /// <summary>
    /// Times one operation and records its latency once it completes. A failing operation records
    /// nothing; its exception propagates.
    /// </summary>
    /// <typeparam name="T">The operation's result.</typeparam>
    /// <param name="operation">The operation to time.</param>
    /// <returns>The operation's result.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="operation"/> is null.</exception>
    public async Task<T> TimeAsync<T>(Func<Task<T>> operation)
    {
        ArgumentNullException.ThrowIfNull(operation);
        long start = timeProvider.GetTimestamp();
        T result = await operation();
        Record(timeProvider.GetElapsedTime(start).TotalMilliseconds);
        return result;
    }

    /// <summary>
    /// Times one operation that produces no value and records its latency once it completes. A
    /// failing operation records nothing; its exception propagates.
    /// </summary>
    /// <param name="operation">The operation to time.</param>
    /// <returns>A task that completes with the operation.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="operation"/> is null.</exception>
    public async Task TimeAsync(Func<Task> operation)
    {
        ArgumentNullException.ThrowIfNull(operation);
        long start = timeProvider.GetTimestamp();
        await operation();
        Record(timeProvider.GetElapsedTime(start).TotalMilliseconds);
    }

    /// <summary>
    /// Records one latency measured elsewhere.
    /// </summary>
    /// <param name="elapsedMs">The latency in milliseconds; zero or positive and finite.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="elapsedMs"/> is negative, NaN or infinite.</exception>
    public void Record(double elapsedMs)
    {
        if (!(elapsedMs >= 0 && double.IsFinite(elapsedMs)))
        {
            throw new ArgumentOutOfRangeException(nameof(elapsedMs), elapsedMs, "A latency is zero or positive and finite.");
        }

        lock (gate)
        {
            samplesMs.Add(elapsedMs);
        }
    }

    /// <summary>
    /// A one-line summary for the test log, formatted invariantly:
    /// <c>n=40 p50=1.20ms p95=3.40ms max=9.10ms mean=1.50ms</c>, or <c>n=0 (no samples)</c>.
    /// </summary>
    /// <returns>The summary.</returns>
    public string Describe()
    {
        lock (gate)
        {
            return samplesMs.Count == 0
                ? "n=0 (no samples)"
                : string.Create(
                    CultureInfo.InvariantCulture,
                    $"n={samplesMs.Count} p50={P50Ms:F2}ms p95={P95Ms:F2}ms max={MaxMs:F2}ms mean={MeanMs:F2}ms");
        }
    }

    private void ThrowIfEmptyLocked()
    {
        if (samplesMs.Count == 0)
        {
            throw new InvalidOperationException("The measurement has no samples: time or record at least one operation first.");
        }
    }
}
