namespace Nordstein.Core.Common.Security;

/// <summary>
/// The failed-sign-in backoff: how long an account stays locked after a number of consecutive failed
/// sign-ins, when such a lock ends, and whether it still runs. The bounds come from
/// <see cref="LoginLockoutOptions"/>; "now" comes from the container's <see cref="Time.IClock"/>.
/// </summary>
/// <remarks>
/// <para>
/// The lockout holds no state. The caller keeps the consecutive-failure count and the lock's end beside
/// the credential, and must count atomically in its store (one statement that adds 1 and returns the new
/// count): a read-modify-write lets concurrent failures overwrite each other's increment and so evade the
/// lock. A typical sign-in: refuse while <see cref="IsLocked"/> holds for the stored end; on a wrong
/// password, store the new count and <see cref="LockedUntilAfter"/> of it; on a correct one, reset the count
/// to 0 and clear the end.
/// </para>
/// <para>
/// Ends are absolute instants from the clock. A clock that steps back keeps a running lock longer, one that
/// steps forward ends it sooner; neither unlocks before the stored instant passes on the clock.
/// </para>
/// <para>Stateless and thread-safe; <see cref="Module"/> registers one instance per container.</para>
/// </remarks>
public interface ILoginLockout
{
    /// <summary>
    /// The lock that follows <paramref name="consecutiveFailures"/> consecutive failures:
    /// <see langword="null"/> below <see cref="LoginLockoutOptions.Threshold"/>, else
    /// <c>min(MaxLockDuration, InitialLockDuration × 2^(consecutiveFailures − Threshold))</c>. The cost does not
    /// grow with the count, and no count overflows.
    /// </summary>
    /// <param name="consecutiveFailures">The failures since the last success, this one included; 0 or more.</param>
    /// <returns>The lock's length, or <see langword="null"/> for no lock.</returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="consecutiveFailures"/> is negative: a corrupted count refuses rather than unlocks.
    /// </exception>
    TimeSpan? LockFor(int consecutiveFailures);

    /// <summary>
    /// The end of the lock that a failure counted now, the <paramref name="consecutiveFailures"/>-th in a row,
    /// starts: the clock's now plus <see cref="LockFor"/>. A lock that would end after
    /// <see cref="DateTimeOffset.MaxValue"/> ends there.
    /// </summary>
    /// <param name="consecutiveFailures">The failures since the last success, this one included; 0 or more.</param>
    /// <returns>The instant the lock ends, or <see langword="null"/> below the threshold.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="consecutiveFailures"/> is negative.</exception>
    DateTimeOffset? LockedUntilAfter(int consecutiveFailures);

    /// <summary>
    /// Whether a lock that ends at <paramref name="lockedUntil"/> still runs: the end lies after the clock's
    /// now. A lock ends exactly at its instant.
    /// </summary>
    /// <param name="lockedUntil">The stored end, or <see langword="null"/> when no lock was ever set.</param>
    /// <returns><see langword="true"/> while the lock runs; <see langword="false"/> for none or an ended one.</returns>
    bool IsLocked(DateTimeOffset? lockedUntil);
}
