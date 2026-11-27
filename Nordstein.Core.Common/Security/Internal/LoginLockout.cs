using Nordstein.Core.Common.Time;
using Nordstein.Core.Common.Validation;

namespace Nordstein.Core.Common.Security.Internal;

/// <summary>
/// The failed-sign-in backoff. See <see cref="ILoginLockout"/>.
/// </summary>
/// <remarks>
/// Stateless apart from the validated options and the clock, both read-only, so one instance serves
/// concurrent callers.
/// </remarks>
internal sealed class LoginLockout : ILoginLockout
{
    private readonly LoginLockoutOptions options;
    private readonly IClock clock;

    public LoginLockout(LoginLockoutOptions options, IClock clock)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(clock);
        options.Validate();
        this.options = options;
        this.clock = clock;
    }

    public TimeSpan? LockFor(int consecutiveFailures)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(consecutiveFailures);
        if (consecutiveFailures < options.Threshold)
        {
            return null;
        }

        // Doubling stops as soon as the next step would pass the maximum, so neither the loop length (at
        // most 63 steps for a one-tick start) nor the TimeSpan arithmetic depends on the failure count.
        TimeSpan delay = options.InitialLockDuration;
        for (long step = 0; step < (long)consecutiveFailures - options.Threshold; step++)
        {
            if (delay > options.MaxLockDuration - delay)
            {
                return options.MaxLockDuration;
            }

            delay += delay;
        }

        return delay;
    }

    public DateTimeOffset? LockedUntilAfter(int consecutiveFailures)
    {
        if (LockFor(consecutiveFailures) is not { } span)
        {
            return null;
        }

        // In UTC: an instant near the end of time with a positive offset would overflow its local clock time
        // even where the UTC sum fits.
        DateTimeOffset now = clock.UtcNow.ToUniversalTime();
        return span > DateTimeOffset.MaxValue - now ? DateTimeOffset.MaxValue : now + span;
    }

    public bool IsLocked(DateTimeOffset? lockedUntil) => lockedUntil is { } until && until > clock.UtcNow;
}
