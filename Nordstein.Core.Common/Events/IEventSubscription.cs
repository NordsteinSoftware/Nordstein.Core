using System.Threading.Channels;

namespace Nordstein.Core.Common.Events;

/// <summary>
/// One subscriber's view of an <see cref="IEventHub{TEvent}"/>: the events its filter accepts, in id order.
/// Disposing ends the subscription and completes <see cref="Events"/>.
/// </summary>
/// <typeparam name="TEvent">The event type.</typeparam>
/// <remarks>Thread-safe; <see cref="IDisposable.Dispose"/> may be called more than once and from any thread, except from a filter.</remarks>
public interface IEventSubscription<TEvent> : IDisposable
    where TEvent : notnull
{
    /// <summary>
    /// The events for this subscriber, oldest first: the replay, then live events. Reading honours the reader's
    /// cancellation token and leaves the subscription intact when cancelled.
    /// </summary>
    /// <remarks>
    /// Completes normally when the subscription or the hub is disposed. Completes with
    /// <see cref="EventSubscriptionOverflowException"/> after the buffered events when the subscriber fell behind
    /// by more than <see cref="EventHubOptions.SubscriptionCapacity"/> events, and with the filter's exception
    /// when the filter threw. <see cref="ChannelReader{T}.ReadAllAsync"/> and
    /// <see cref="ChannelReader{T}.WaitToReadAsync"/> rethrow that exception;
    /// <see cref="ChannelReader{T}.ReadAsync"/> throws <see cref="ChannelClosedException"/> with it as the inner
    /// exception.
    /// </remarks>
    ChannelReader<PublishedEvent<TEvent>> Events { get; }

    /// <summary>
    /// Whether a replay was asked for and events published after its id may be missing from it: they left the
    /// history (older than the window, or beyond its capacity), or the id is not one this hub issued (from before
    /// it started, as after a restart, or greater than its last id). A subscriber that sees
    /// <see langword="true"/> reloads its state from the source of truth. Always <see langword="false"/> without a
    /// replay id. Fixed when the subscription opens.
    /// </summary>
    bool ReplayIncomplete { get; }

    /// <summary>
    /// Replaces the filter (the principal's permissions changed). It applies to every event whose publication
    /// starts after this call returns; events already buffered stay.
    /// </summary>
    /// <param name="filter">The new filter, under the same rules as the first.</param>
    /// <exception cref="ArgumentNullException"><paramref name="filter"/> is <see langword="null"/>.</exception>
    void ReplaceFilter(Func<TEvent, bool> filter);
}
