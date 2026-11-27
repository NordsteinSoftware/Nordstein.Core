namespace Nordstein.Core.Common.Events;

/// <summary>
/// In-process publish and subscribe for one event type, with a bounded history a reconnecting subscriber can
/// replay from the last id it saw. The hub knows no transport and nothing about who subscribes: every
/// subscriber passes the filter that decides which events it may see.
/// </summary>
/// <typeparam name="TEvent">
/// The event type; one hub, and one id sequence, per type. Events are shared by reference between subscribers
/// and kept in the history, so make them immutable.
/// </typeparam>
/// <remarks>
/// <para>
/// <b>Ids.</b> Every published event gets the next id: the first is the clock's Unix milliseconds × 1000 + 1,
/// then +1 per event, so ids rise within a hub and, while a process publishes fewer than 1,000 events per
/// millisecond on average and the clock does not step back, across a restart too. Ids are the replay cursor.
/// </para>
/// <para>
/// <b>History.</b> The hub keeps the newest <see cref="EventHubOptions.ReplayCapacity"/> events published within
/// <see cref="EventHubOptions.ReplayWindow"/> (by the container's <see cref="Time.IClock"/>), and replays the
/// kept events after a given id, filtered, oldest first, atomically with the start of live delivery: no event
/// is delivered twice, reordered or skipped between the replay and the live events.
/// </para>
/// <para>
/// <b>Slow subscribers.</b> <see cref="Publish"/> never blocks and never throws for a subscriber. A subscription
/// holds its replay plus <see cref="EventHubOptions.SubscriptionCapacity"/> undelivered events; the event that
/// would overflow it ends that subscription instead: its reader receives every event buffered so far, in order,
/// and then <see cref="EventSubscriptionOverflowException"/>. Nothing is dropped silently; the subscriber
/// subscribes again with the last id it processed and the history fills the gap (or
/// <see cref="IEventSubscription{TEvent}.ReplayIncomplete"/> says it could not).
/// </para>
/// <para>
/// <b>Filters.</b> A filter runs on the publisher's thread under the hub's lock, for every event and every
/// subscription: it must be fast, must not block, and must not call the hub or dispose a subscription (such a
/// call throws <see cref="InvalidOperationException"/> instead of deadlocking). A filter that throws ends its
/// own subscription with that exception, and only that subscription.
/// </para>
/// <para>
/// Thread-safe. <see cref="Module"/> registers one hub per event type for the container's lifetime; disposing
/// the container completes every subscription, after which <see cref="Publish"/> does nothing and
/// <see cref="Subscribe"/> throws <see cref="ObjectDisposedException"/>.
/// </para>
/// </remarks>
public interface IEventHub<TEvent>
    where TEvent : notnull
{
    /// <summary>
    /// Gives <paramref name="value"/> the next id, keeps it for replay and offers it to every subscription whose
    /// current filter accepts it. Never blocks and never throws for a slow, full, failing or gone subscription;
    /// after the hub is disposed it does nothing.
    /// </summary>
    /// <param name="value">The event.</param>
    /// <exception cref="ArgumentNullException"><paramref name="value"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">A filter of this hub called it.</exception>
    void Publish(TEvent value);

    /// <summary>
    /// Opens a subscription. With <paramref name="afterEventId"/> it first replays the kept events with a
    /// greater id that <paramref name="filter"/> accepts, oldest first, then continues with live events.
    /// </summary>
    /// <param name="filter">
    /// Which events this subscriber may see, for example the events visible to the signed-in principal it
    /// serves. See the remarks on the type for its rules.
    /// </param>
    /// <param name="afterEventId">The last id the subscriber processed, or <see langword="null"/> for live events only.</param>
    /// <returns>The subscription; the caller disposes it.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="filter"/> is <see langword="null"/>.</exception>
    /// <exception cref="ObjectDisposedException">The hub is disposed.</exception>
    /// <exception cref="InvalidOperationException">A filter of this hub called it.</exception>
    /// <remarks>An exception <paramref name="filter"/> throws during the replay propagates, and no subscription is opened.</remarks>
    IEventSubscription<TEvent> Subscribe(Func<TEvent, bool> filter, long? afterEventId = null);
}
