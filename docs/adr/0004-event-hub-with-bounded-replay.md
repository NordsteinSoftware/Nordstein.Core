# ADR 0004: In-process event hub with bounded replay in Core.Common

- Status: proposed
- Date: 2026-11-28
- Issue: [#75](https://github.com/NordsteinSoftware/Nordstein.Core/issues/75) (part of #54, ADR 0001 decision 6)

## Context

ADR 0001 decision 6 asks for a design before the event hub moves from nordstein-dms
(`Application/Events/`, source at `cf146a85`). The issue names three open questions:

- the shape of the visibility filter seam,
- the replay bound, the cursor type, and what a subscriber gets when it falls behind,
- the policy for back-pressure and slow subscribers.

In dms the hub is not generic. `IEventHub.Subscribe(IAccessContext, long?)` takes the product's
access context. Each `IServerEvent` carries an `IAudience`, and the hub matches the two.
`IEventSubscription.Refresh(IAccessContext)` swaps the context. A full subscription drops its
oldest event without telling anyone.

The port merged in PR #82 (`ab4047c`) before this ADR was written. It is described in
`docs/events.md` and in the XML docs of `IEventHub<TEvent>`. This ADR records the decisions
that commit made, so that later changes have a reference. The answers did not need new design
work. Core has published no release yet, so the public shape can still change without a major
version.

## Decision

1. **The hub is generic, with one singleton per event type.** It is `IEventHub<TEvent>` (with
   `TEvent : notnull`), registered as an open generic in `Nordstein.Core.Common.Module`. Core
   defines no event base type, no `Type` string and no payload envelope. A product that wants
   one envelope (as dms's `IServerEvent`) uses it as `TEvent`.
2. **Visibility is a filter per subscriber, `Func<TEvent, bool>`.** Core never decides who may
   see an event. The product builds the filter from its own access model when it subscribes,
   and calls `ReplaceFilter` when that model changes (this replaces dms's `Refresh`). The hub
   runs the filter on live events and on replayed events. An audience on the event is the
   product's business: its filter reads it.
3. **The cursor is a `long` event id, which rises across restarts.** The first id is the
   clock's Unix milliseconds × 1000 + 1, then each event adds 1. A client's last id stays a
   usable cursor after a restart, provided the process publishes fewer than 1,000 events per
   millisecond on average and the clock does not step back. `PublishedEvent<TEvent>(Id, Event)`
   carries it.
4. **The history is bounded by time and by count.** `EventHubOptions` sets `ReplayWindow`
   (default 5 minutes), `ReplayCapacity` (default 10,000 events) and `SubscriptionCapacity`
   (default 256 undelivered live events). All three must be positive, and the options are
   checked when the hub is resolved. `Subscribe(filter, afterEventId)` replays the kept events
   after that id under the publish lock, then delivers live events. There is no duplicate, no
   reordering and no gap between replay and live.
5. **A subscriber that falls behind the bound is told, and is never served silently.**
   `ReplayIncomplete` is `true` when events after the requested id may be missing. That
   happens when one of them has left the history, or when the hub did not issue the id. The
   subscriber then reloads from its source of truth.
6. **A slow subscriber is ended, and no event is dropped.** `Publish` never blocks and never
   throws for a subscriber. The event that would overflow a subscription's buffer ends that
   subscription. Its reader gets every buffered event, then
   `EventSubscriptionOverflowException`, and it resubscribes with its last id. A filter that
   throws ends only its own subscription. A filter that calls back into the hub gets
   `InvalidOperationException` instead of a deadlock.
7. **The transport stays in the product.** Core has no SSE endpoint, no WebSocket and no
   serialisation of events. The product reads `Events` and writes to its own sink.

## Alternatives considered

- **Keep `IAccessContext` and an audience on each event.** Rejected: it would bring the dms
  access model (users, groups, audiences) into Core.Common. ADR 0001 keeps product concepts out
  of Core.
- **Filter per event audience, with a Core `IAudience` type.** Rejected for the same reason.
  A product's filter can still read an audience from its own event type.
- **Drop the oldest event when a buffer is full, as dms does.** Rejected: the subscriber could
  not tell that it missed an event, and its view would drift without any signal.
- **Block `Publish` until a slow subscriber catches up.** Rejected: one stalled browser tab
  would stall every publisher, and publishers are often request handlers.
- **A GUID or opaque string cursor.** Rejected: ids must be ordered to decide what to replay.
  A `long` that starts from the clock survives a restart without being stored.
- **A durable history (database or outbox).** Rejected: the hub only bridges short
  disconnects. A client that is gone longer reloads from the source of truth, which
  `ReplayIncomplete` asks for.

## Consequences

- dms can drop its own hub. It registers `IEventHub<IServerEvent>` (or one hub per event type),
  builds a filter from `IAccessContext` and the event's audience, and replaces `Refresh` with
  `ReplaceFilter`. Core needs no change for this.
- The behaviour changes for dms. A slow SSE client is disconnected with
  `EventSubscriptionOverflowException` instead of losing events silently. The client must
  reconnect with `Last-Event-ID` and reload when `ReplayIncomplete` is `true`.
- Events are shared by reference and kept in the history. They must be immutable and must hold
  only what every accepted subscriber may see.
- Filters run under the hub's lock on the publisher's thread, so a slow filter slows every
  publisher. `docs/events.md` states this.
- No follow-up tasks: the code, tests and `docs/events.md` are on `main`.
