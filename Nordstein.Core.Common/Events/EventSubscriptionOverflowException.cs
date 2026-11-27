using System.Globalization;

namespace Nordstein.Core.Common.Events;

/// <summary>
/// Ends a subscription whose subscriber fell behind by more undelivered events than
/// <see cref="EventHubOptions.SubscriptionCapacity"/>. Every event buffered before it was delivered; subscribe
/// again with the last id processed to replay what followed from the hub's history.
/// </summary>
public sealed class EventSubscriptionOverflowException : Exception
{
    /// <summary>
    /// The overflow of a subscription that held <paramref name="capacity"/> undelivered events.
    /// </summary>
    /// <param name="capacity">The subscription's buffer size.</param>
    public EventSubscriptionOverflowException(int capacity)
        : base(string.Create(
            CultureInfo.InvariantCulture,
            $"The subscriber fell behind by more than {capacity} events; subscribe again with the last id it processed."))
    {
        Capacity = capacity;
    }

    /// <summary>
    /// The subscription's buffer size: its replay plus <see cref="EventHubOptions.SubscriptionCapacity"/>.
    /// </summary>
    public int Capacity { get; }
}
