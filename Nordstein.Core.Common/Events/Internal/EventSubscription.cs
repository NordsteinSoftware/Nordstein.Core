using System.Threading.Channels;

namespace Nordstein.Core.Common.Events.Internal;

/// <summary>
/// One subscription: a bounded channel and the filter its events pass. The hub writes under its lock; the
/// subscriber reads. See <see cref="IEventSubscription{TEvent}"/>.
/// </summary>
/// <typeparam name="TEvent">The event type.</typeparam>
internal sealed class EventSubscription<TEvent> : IEventSubscription<TEvent>
    where TEvent : notnull
{
    private readonly Channel<PublishedEvent<TEvent>> channel;
    private readonly Action<EventSubscription<TEvent>> remove;
    private readonly int capacity;
    private volatile Func<TEvent, bool> filter;
    private int disposed;

    public EventSubscription(
        Func<TEvent, bool> filter, int capacity, bool replayIncomplete, Action<EventSubscription<TEvent>> remove)
    {
        this.filter = filter;
        this.capacity = capacity;
        this.remove = remove;
        ReplayIncomplete = replayIncomplete;

        // Wait mode makes TryWrite refuse a full buffer instead of dropping an event; continuations stay
        // asynchronous (the default), so no reader code runs under the hub's lock.
        channel = Channel.CreateBounded<PublishedEvent<TEvent>>(new BoundedChannelOptions(capacity)
        {
            FullMode = BoundedChannelFullMode.Wait,
        });
    }

    public ChannelReader<PublishedEvent<TEvent>> Events => channel.Reader;

    public bool ReplayIncomplete { get; }

    public void ReplaceFilter(Func<TEvent, bool> filter)
    {
        ArgumentNullException.ThrowIfNull(filter);
        this.filter = filter;
    }

    /// <summary>
    /// Writes one replayed event; the buffer was sized for the whole replay, so it always fits.
    /// </summary>
    public void Replay(PublishedEvent<TEvent> published) => channel.Writer.TryWrite(published);

    /// <summary>
    /// Offers a live event under the hub's lock. Returns <see langword="false"/> when the subscription ended
    /// here: its filter threw (the channel completes with that exception) or its buffer is full (it completes
    /// with <see cref="EventSubscriptionOverflowException"/> after the buffered events). Never throws.
    /// </summary>
    public bool Offer(PublishedEvent<TEvent> published)
    {
        bool accepted;
        try
        {
            accepted = filter(published.Event);
        }
        catch (Exception exception)
        {
            // WHY catch everything: the filter is the subscriber's code and the publisher must never fail
            // for it; the exception is not swallowed but handed to that subscriber's reader.
            channel.Writer.TryComplete(exception);
            return false;
        }

        if (!accepted || channel.Writer.TryWrite(published))
        {
            return true;
        }

        channel.Writer.TryComplete(new EventSubscriptionOverflowException(capacity));
        return false;
    }

    /// <summary>
    /// Completes the channel normally, without leaving the hub (the hub is disposing).
    /// </summary>
    public void Complete() => channel.Writer.TryComplete();

    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) == 0)
        {
            remove(this);
            Complete();
        }
    }
}
