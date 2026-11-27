namespace Nordstein.Core.Common.Diagnostics;

/// <summary>
/// What one unit of work's <see cref="IRequestTimings"/> recorded, by name (ordinal keys). Each dictionary is a
/// copy owned by the snapshot.
/// </summary>
/// <param name="Durations">The summed durations.</param>
/// <param name="Counts">The summed counts.</param>
/// <param name="Values">The last value set per name.</param>
/// <remarks>Record equality compares the dictionaries by reference, not by content.</remarks>
public sealed record RequestTimingSnapshot(
    IReadOnlyDictionary<string, TimeSpan> Durations,
    IReadOnlyDictionary<string, long> Counts,
    IReadOnlyDictionary<string, string> Values);
