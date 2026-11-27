using System.ComponentModel.DataAnnotations;
using Rules = Nordstein.Core.Common.Validation.Validation;

namespace Nordstein.Core.Common.Events;

/// <summary>
/// The bounds of every <see cref="IEventHub{TEvent}"/> in a container. All must be positive.
/// </summary>
/// <remarks>
/// <see cref="Module"/> registers the defaults unless the host registered its own instance first; a host that
/// registers one after the module replaces them as the last registration. Immutable and thread-safe.
/// <see cref="Validate"/> runs when a hub is resolved, so an invalid instance fails resolution with a
/// <see cref="ValidationException"/>.
/// </remarks>
public sealed record EventHubOptions : IValidatableObject
{
    /// <summary>
    /// How long a published event stays available for replay: 5 minutes.
    /// </summary>
    public TimeSpan ReplayWindow { get; init; } = TimeSpan.FromMinutes(5);

    /// <summary>
    /// The most events kept for replay; the oldest go first: 10,000.
    /// </summary>
    public int ReplayCapacity { get; init; } = 10_000;

    /// <summary>
    /// The most undelivered live events one subscription buffers before it ends with
    /// <see cref="EventSubscriptionOverflowException"/>: 256. A replay gets room on top of it.
    /// </summary>
    public int SubscriptionCapacity { get; init; } = 256;

    /// <summary>
    /// Refuses a non-positive window or capacity.
    /// </summary>
    /// <param name="validationContext">The validation context (unused).</param>
    /// <returns>The broken rules; <see cref="ValidationResult.Success"/> entries for rules that hold.</returns>
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        yield return Rules.Positive(ReplayWindow, nameof(ReplayWindow));
        yield return Rules.GreaterThan(ReplayCapacity, 0, nameof(ReplayCapacity));
        yield return Rules.GreaterThan(SubscriptionCapacity, 0, nameof(SubscriptionCapacity));
    }
}
