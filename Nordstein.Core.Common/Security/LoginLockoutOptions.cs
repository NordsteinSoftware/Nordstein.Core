using System.ComponentModel.DataAnnotations;
using Rules = Nordstein.Core.Common.Validation.Validation;

namespace Nordstein.Core.Common.Security;

/// <summary>
/// The bounds of the failed-sign-in backoff <see cref="ILoginLockout"/> applies: from
/// <see cref="Threshold"/> consecutive failures the account is locked for
/// <c>min(<see cref="MaxLockDuration"/>, <see cref="InitialLockDuration"/> × 2^(failures − <see cref="Threshold"/>))</c>.
/// </summary>
/// <remarks>
/// <para>
/// The defaults are 5 failures, 1 minute and 15 minutes: 5 failures lock for 1 minute, 6 for 2, 7 for 4,
/// 8 for 8 and 9 or more for 15. <see cref="Module"/> registers these defaults unless the host registered
/// its own instance first; a host that registers one after the module replaces them as the last
/// registration.
/// </para>
/// <para>
/// Immutable and thread-safe. <see cref="Validate"/> runs when <see cref="ILoginLockout"/> is resolved, so
/// an invalid instance fails resolution with a <see cref="ValidationException"/> and never reaches a
/// sign-in.
/// </para>
/// </remarks>
public sealed record LoginLockoutOptions : IValidatableObject
{
    /// <summary>
    /// The number of consecutive failures from which an account is locked: 5 by default, at least 1.
    /// </summary>
    public int Threshold { get; init; } = 5;

    /// <summary>
    /// The lock after exactly <see cref="Threshold"/> failures; each further failure doubles it up to
    /// <see cref="MaxLockDuration"/>. 1 minute by default; must be positive.
    /// </summary>
    public TimeSpan InitialLockDuration { get; init; } = TimeSpan.FromMinutes(1);

    /// <summary>
    /// The longest lock any failure count produces: 15 minutes by default; must not be shorter than
    /// <see cref="InitialLockDuration"/>.
    /// </summary>
    public TimeSpan MaxLockDuration { get; init; } = TimeSpan.FromMinutes(15);

    /// <summary>
    /// Refuses a threshold below 1, a non-positive initial lock, and a maximum shorter than the initial lock.
    /// </summary>
    /// <param name="validationContext">The validation context (unused).</param>
    /// <returns>The broken rules; <see cref="ValidationResult.Success"/> entries for rules that hold.</returns>
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        yield return Rules.GreaterThan(Threshold, 0, nameof(Threshold));
        yield return Rules.Positive(InitialLockDuration, nameof(InitialLockDuration));
        if (MaxLockDuration < InitialLockDuration)
        {
            yield return new ValidationResult(
                $"{nameof(MaxLockDuration)} must not be shorter than {nameof(InitialLockDuration)}",
                [nameof(MaxLockDuration)]);
        }
    }
}
