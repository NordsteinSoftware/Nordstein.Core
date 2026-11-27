# Events

`Nordstein.Core.Common.Events` is an in-process publish and subscribe hub with a bounded replay history:
`IEventHub<TEvent>`, one singleton per event type, registered as an open generic in
`Nordstein.Core.Common.Module`. It knows no transport (a product puts Server-Sent Events, a WebSocket or a
background consumer on top) and nothing about who subscribes.

## The one rule this area lives by

Core never decides who may see an event. Every subscriber passes a filter, `Func<TEvent, bool>`, that
answers that question for the principal it serves; the product builds it from its own access model (the
principal's groups or roles) and replaces it with `ReplaceFilter` when that model changes. The hub only
evaluates it, live and in replay.

```csharp
IEventSubscription<StatusChanged> subscription = hub.Subscribe(
    change => visibility.MaySee(principal, change.Scope),
    afterEventId: lastEventIdFromTheClient);
await foreach (PublishedEvent<StatusChanged> published in subscription.Events.ReadAllAsync(cancellationToken))
{
    await sink.WriteAsync(published.Id, published.Event, cancellationToken);
}
```

## Ids and the history

- Each hub numbers its events: the first id is the clock's Unix milliseconds × 1000 + 1 (read when the hub
  is first resolved), then +1 per event. Ids rise within a process and, while it publishes fewer than 1,000
  events per millisecond on average and the clock does not step back, across a restart, so a client's last
  id stays a usable cursor.
- `EventHubOptions` (registered with defaults unless the host registered its own first; all positive, checked
  at resolution): `ReplayWindow` 5 minutes, `ReplayCapacity` 10,000 kept events, `SubscriptionCapacity` 256
  undelivered live events per subscription. The history drops events older than the window (by `IClock`)
  and the oldest beyond the capacity, on every publish and subscribe. A clock that steps back keeps events
  longer; it never drops a younger one first.
- `Subscribe(filter, afterEventId)` replays the kept events with a greater id that the filter accepts,
  oldest first, under the same lock as `Publish`, then delivers live: no duplicate, no reorder, no gap
  between replay and live. The buffer holds the whole replay plus `SubscriptionCapacity`.
- `ReplayIncomplete` is `true` when a replay was asked for and events after its id may be missing: one
  left the history after that id, or the id is not one this hub issued (from before it started, as after
  a restart, or beyond its last id). The subscriber then reloads from its source of truth.

## Slow subscribers: end, never drop

`Publish` never blocks and never throws for a subscriber. The event that would overflow a subscription's
buffer ends that subscription: its reader gets every buffered event in order, then
`EventSubscriptionOverflowException` (`ReadAllAsync` and `WaitToReadAsync` rethrow it, `ReadAsync` wraps it
in `ChannelClosedException`). The subscriber resubscribes with the last id it processed and the history
supplies the rest, or `ReplayIncomplete` says it cannot. Nothing is ever lost without a signal. Dropping the
oldest event instead was rejected because a subscriber could not tell.

## Filters

A filter runs on the publisher's thread under the hub's lock, once per event and subscription: keep it fast
and non-blocking. It must not publish, subscribe or dispose: the hub detects such a call on the lock-holding
thread and throws `InvalidOperationException` instead of deadlocking. A filter that throws (that exception
included) ends its own subscription, whose reader receives the exception; the publisher and every other
subscription carry on. A filter that throws during the replay of `Subscribe` propagates from `Subscribe`.

## Lifetimes

- Dispose a subscription to leave; its `Events` completes normally. Disposing twice is harmless. A
  subscription that is never disposed stays until it overflows.
- Disposing the container disposes the hub: every subscription completes normally, later `Publish` calls
  do nothing (a publisher racing shutdown never fails) and `Subscribe` throws `ObjectDisposedException`.
- A cancelled read throws `OperationCanceledException` and leaves the subscription intact.
- Events are shared by reference with every subscriber and kept in the history: make them immutable, and
  put in them only what every accepted subscriber may see.

## Review focus (Standard of Care #3)

For the hub the adversarial pass hunts for: a path where `Publish` blocks, throws or runs reader code
inline; a replay that can duplicate, reorder or skip around the switch to live; an event dropped without
`ReplayIncomplete` or an overflow exception telling the subscriber; a filter called outside the lock or able
to deadlock it; state touched outside the lock; and a subscription that survives its own disposal.
