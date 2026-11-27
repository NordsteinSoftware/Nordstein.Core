using Nordstein.Core.Common.Async;
using Nordstein.Core.Common.Time;
using Nordstein.Core.Common.Validation;

namespace Nordstein.Core.Common.Events.Internal;

/// <summary>
/// The in-process hub of one event type. See <see cref="IEventHub{TEvent}"/>.
/// </summary>
/// <remarks>
/// <para>
/// A stateful singleton by design: its state (the kept events, the subscriptions, the last id, the replay
/// floor) is private and touched only under one <see cref="IAsyncLock"/> key, which publish, subscribe, release
/// and dispose all take, so a replay never duplicates or reorders a live event. Nothing under the lock waits:
/// channel writes never block (a full subscription ends instead), and channel continuations run asynchronously,
/// so no reader code runs on the publisher's thread.
/// </para>
/// <para>
/// The only foreign code under the lock is a subscriber's filter. A filter that calls back into the hub would
/// wait on the lock its own thread holds; <see cref="Acquire"/> detects that thread and throws instead.
/// </para>
/// </remarks>
/// <typeparam name="TEvent">The event type.</typeparam>
internal sealed class EventHub<TEvent> : IEventHub<TEvent>, IDisposable
    where TEvent : notnull
{
    private readonly object gate = new();
    private readonly IAsyncLock asyncLock;
    private readonly IClock clock;
    private readonly TimeSpan replayWindow;
    private readonly int replayCapacity;
    private readonly int subscriptionCapacity;
    private readonly Queue<Kept> kept = new();
    private readonly List<EventSubscription<TEvent>> subscriptions = [];
    private readonly List<EventSubscription<TEvent>> ended = [];
    private long lastId;

    // The highest id the history cannot replay: before the first event, every id up to the seed (a previous
    // process's); later, the last event pruned. A replay after an id below it may miss events.
    private long floor;
    private bool disposed;
    private int lockHolder;

    public EventHub(EventHubOptions options, IClock clock, IAsyncLock asyncLock)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(asyncLock);
        options.Validate();
        this.clock = clock;
        this.asyncLock = asyncLock;
        replayWindow = options.ReplayWindow;
        replayCapacity = options.ReplayCapacity;
        subscriptionCapacity = options.SubscriptionCapacity;

        // Milliseconds times 1000: ids keep rising across a restart while fewer than 1000 events share a
        // millisecond on average and the clock does not step back.
        lastId = clock.UtcNow.ToUnixTimeMilliseconds() * 1000;
        floor = lastId;
    }

    public void Publish(TEvent value)
    {
        ArgumentNullException.ThrowIfNull(value);
        IDisposable handle = Acquire();
        try
        {
            if (disposed)
            {
                return;
            }

            var published = new PublishedEvent<TEvent>(++lastId, value);
            kept.Enqueue(new Kept(published, clock.UtcNow));
            Prune();
            foreach (EventSubscription<TEvent> subscription in subscriptions)
            {
                if (!subscription.Offer(published))
                {
                    ended.Add(subscription);
                }
            }

            foreach (EventSubscription<TEvent> subscription in ended)
            {
                subscriptions.Remove(subscription);
            }

            ended.Clear();
        }
        finally
        {
            Release(handle);
        }
    }

    public IEventSubscription<TEvent> Subscribe(Func<TEvent, bool> filter, long? afterEventId = null)
    {
        ArgumentNullException.ThrowIfNull(filter);
        IDisposable handle = Acquire();
        try
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            Prune();
            List<PublishedEvent<TEvent>> replay = [];
            bool incomplete = false;
            if (afterEventId is { } after)
            {
                incomplete = after < floor || after > lastId;
                foreach (Kept candidate in kept)
                {
                    if (candidate.Published.Id > after && filter(candidate.Published.Event))
                    {
                        replay.Add(candidate.Published);
                    }
                }
            }

            // The buffer holds the whole replay plus the usual room for live events: a long replay is never cut.
            var subscription = new EventSubscription<TEvent>(
                filter, subscriptionCapacity + replay.Count, incomplete, Remove);
            foreach (PublishedEvent<TEvent> replayed in replay)
            {
                subscription.Replay(replayed);
            }

            subscriptions.Add(subscription);
            return subscription;
        }
        finally
        {
            Release(handle);
        }
    }

    /// <summary>
    /// Completes every subscription normally; later publishes do nothing and later subscribes throw.
    /// Idempotent.
    /// </summary>
    public void Dispose()
    {
        IDisposable handle = Acquire();
        try
        {
            disposed = true;
            foreach (EventSubscription<TEvent> subscription in subscriptions)
            {
                subscription.Complete();
            }

            subscriptions.Clear();
            kept.Clear();
        }
        finally
        {
            Release(handle);
        }
    }

    private void Remove(EventSubscription<TEvent> subscription)
    {
        IDisposable handle = Acquire();
        try
        {
            subscriptions.Remove(subscription);
        }
        finally
        {
            Release(handle);
        }
    }

    private IDisposable Acquire()
    {
        // Only the thread that holds the lock can read its own id here; any other thread reads 0 or another id.
        if (Volatile.Read(ref lockHolder) == Environment.CurrentManagedThreadId)
        {
            throw new InvalidOperationException(
                "An event hub filter called the hub: a filter must not publish, subscribe or dispose.");
        }

        IDisposable handle = asyncLock.Lock(gate);
        Volatile.Write(ref lockHolder, Environment.CurrentManagedThreadId);
        return handle;
    }

    private void Release(IDisposable handle)
    {
        Volatile.Write(ref lockHolder, 0);
        handle.Dispose();
    }

    // Under the lock: events older than the window go, then the oldest over the capacity. The queue is in id
    // order, so the floor only rises.
    private void Prune()
    {
        DateTimeOffset cutoff = clock.UtcNow - replayWindow;
        while (kept.Count > 0 && (kept.Count > replayCapacity || kept.Peek().At < cutoff))
        {
            floor = kept.Dequeue().Published.Id;
        }
    }

    private readonly record struct Kept(PublishedEvent<TEvent> Published, DateTimeOffset At);
}
