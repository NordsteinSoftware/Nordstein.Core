namespace Nordstein.Core.Common.Diagnostics;

/// <summary>
/// The timing accumulator of one unit of work (one lifetime scope; on a server, one request): writers on every
/// layer add what they measured under a name, and the end of the unit reads one consistent
/// <see cref="Snapshot"/>. Durations and counts add up, values keep the last one set.
/// </summary>
/// <remarks>
/// <para>
/// Names are short constants of the writer (<see cref="RequestTimingNames"/> holds Core's; a product adds its
/// own), compared ordinally. Record measurements and short labels only: never a secret, a query or command
/// text, a path or user content, since a snapshot is meant to be logged or shown to developers.
/// </para>
/// <para>
/// Sums saturate: a duration sum stops at <see cref="TimeSpan.MaxValue"/> and a count at
/// <see cref="long.MaxValue"/>, so a diagnostic never fails the unit of work it measures.
/// </para>
/// <para>
/// Thread-safe: the writers of one unit may run concurrently, and a snapshot never sees half of another call.
/// <see cref="Module"/> registers one instance per lifetime scope; resolve it from the unit's scope, never into a
/// singleton, which would share the root scope's instance across every unit.
/// </para>
/// </remarks>
public interface IRequestTimings
{
    /// <summary>
    /// Adds <paramref name="duration"/> to the duration under <paramref name="name"/>.
    /// </summary>
    /// <param name="name">The bucket's name.</param>
    /// <param name="duration">The measured time; zero or more.</param>
    /// <exception cref="ArgumentNullException"><paramref name="name"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="name"/> is empty or white space.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="duration"/> is negative.</exception>
    void Mark(string name, TimeSpan duration);

    /// <summary>
    /// Adds <paramref name="by"/> to the count under <paramref name="name"/>.
    /// </summary>
    /// <param name="name">The bucket's name.</param>
    /// <param name="by">How much to add; zero or more, 1 by default.</param>
    /// <exception cref="ArgumentNullException"><paramref name="name"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="name"/> is empty or white space.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="by"/> is negative.</exception>
    void Count(string name, long by = 1);

    /// <summary>
    /// Sets the value under <paramref name="name"/>; a later call replaces it.
    /// </summary>
    /// <param name="name">The bucket's name.</param>
    /// <param name="value">A short label (the name of the operation that ran, for example); may be empty.</param>
    /// <exception cref="ArgumentNullException"><paramref name="name"/> or <paramref name="value"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="name"/> is empty or white space.</exception>
    void Set(string name, string value);

    /// <summary>
    /// A copy of everything recorded so far; later records leave it unchanged.
    /// </summary>
    /// <returns>The durations, counts and values by name.</returns>
    RequestTimingSnapshot Snapshot();
}
