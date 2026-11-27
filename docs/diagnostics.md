# Diagnostics

`Nordstein.Core.Common.Diagnostics` holds `IRequestTimings`, the timing accumulator of one unit of work: on a
server one request, in a worker one job pass. Writers on every layer add what they measured under a name;
whoever owns the unit (a request middleware, a development logger) reads one `Snapshot()` at its end. Core
writes one bucket itself, the database time of `Nordstein.Core.Storage` ([storage.md](storage.md#request-timings));
everything else is the product's.

## The accumulator

| Member | Effect |
|---|---|
| `Mark(name, duration)` | adds a duration (zero or more) to the sum under `name` |
| `Count(name, by = 1)` | adds a count (zero or more) to the sum under `name` |
| `Set(name, value)` | keeps the last value set under `name` (a short label) |
| `Snapshot()` | a `RequestTimingSnapshot` of three ordinal dictionaries, copies owned by the snapshot |

- **One per lifetime scope.** `Nordstein.Core.Common.Module` registers it `InstancePerLifetimeScope`. Resolve
  it from the unit's scope; a singleton that takes it would share the root scope's instance across every unit.
- **Thread-safe and consistent.** The writers of one unit may run concurrently; every call holds one
  `IAsyncLock` key briefly, so a snapshot never sees half of another call (a command's duration without its
  count).
- **Never fails the unit.** Sums saturate at `TimeSpan.MaxValue` and `long.MaxValue`. A blank name, a negative
  duration or count, or a `null` value is a programming error and throws `ArgumentException`.
- **Names.** Ordinal, short and constant. Core's are in `RequestTimingNames` (`Database` = `db`); a product keeps
  its own names (an authentication step, an upstream call, a tool) in its own constants.
- **Data.** Names, durations, counts and short labels only: a snapshot is made to be logged and shown to
  developers, so never record a secret, a query or command text, a path or user content.

## Review focus (Standard of Care #3)

For the accumulator the adversarial pass hunts for: a lifetime that shares one instance across units of work;
a sum that can overflow or throw; a snapshot that aliases live state or tears between buckets; and a writer
that records content instead of a measurement.
