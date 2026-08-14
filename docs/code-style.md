# Code Style

> **`TreatWarningsAsErrors=true`** is set solution-wide in `Directory.Build.props` — the build
> fails on *any* compiler warning. Leave no unused usings/variables, no obsolete-API calls, no
> nullable warnings. This is why suppressing nullable warnings with `!` is both forbidden and
> pointless (it would only move the failure). Run `dotnet build Nordstein.Core.sln` before
> claiming done.

These rules are shared across all Nordstein backends; Core is where they are held most strictly.

- Dependency injection everywhere. Avoid the `static` keyword and service locators; injecting
  `IServiceProvider` is strongly discouraged.
- Do not use primary constructors. Use constructor injection with DI and `this(...)` chaining for
  domain entities.
- Use `record` types for all domain entities and storage entities (even if mutable).
- **`internal` by default; only interfaces or POCO types should be `public`.** In Core this is
  doubly important: every `public` member is package API a consumer can build on and we can never
  cleanly remove — see the Standard of Care in [`../CLAUDE.md`](../CLAUDE.md).
- Prefer immutability and statelessness; storage entities may be mutable where EF Core needs it,
  using `required` properties with `init` accessors.
- Use `var` when the type is obvious from the right-hand side, otherwise be explicit.
- Expression-bodied members for simple one-liners; otherwise block bodies with braces.
- `nameof(...)` for all parameter names in exceptions and validation.
- Prefer collection expressions.
- **Suppressing nullable warnings with `!` is strictly forbidden.** The single sanctioned
  exception in the entire ecosystem is `Validation.Success`
  ([`../Nordstein.Core.Common/Validation/Validation.cs`](../Nordstein.Core.Common/Validation/Validation.cs));
  see [`validation.md`](validation.md) for why. Never add a second.
- Static members shall be avoided (except extension methods and constants).
- Docstrings: newline after `<summary>` and before `</summary>` (minimum 3-line blocks):
  ```csharp
  /// <summary>
  /// Does the thing.
  /// </summary>
  ```
  In Core, **every public member carries XML docs** — a package consumer has no source to read.
  Document the contract: thread-safety, cancellation behavior, what throws when, ownership of
  disposables.

## Key Conventions

- All timestamps are `DateTimeOffset`, never `DateTime`.
- Domain entities are immutable `internal record` types — no setters on domain-layer properties.
- Domain interfaces are `public`; implementations and storage entities are `internal`.
- Repositories return domain entities (`I[Entity]`), never storage entities.
- Always pass a `CancellationToken` to every async method — and **honor it**: a Core API that
  swallows cancellation is a bug.
- Domain references hold the related entity; storage entities hold the `Guid` foreign key.
- Decorate reflection-discovered types (custom repositories etc.) with `[UsedImplicitly]`.

## Concurrency

- **Always use `IAsyncLock` for in-process concurrency control.** Inject it via DI (`IAsyncLock`
  from `Nordstein.Core.Common.Async`, registered in `Nordstein.Core.Common.Module`). Never use
  `lock`/`Monitor`, `SemaphoreSlim`, `Mutex`, or other raw synchronization primitives in feature
  code — they are not safe to hold across `await`, and a hand-rolled lock bypasses the shared,
  keyed implementation.
- `IAsyncLock` is **keyed**: `LockAsync(key, ct)` serializes only callers sharing the same key,
  so use the narrowest natural key (an entity `Id`, a fingerprint) to avoid serializing unrelated
  work. Pass the `CancellationToken` through.
- Always `await` the acquire and scope the handle with `using` so it releases on every path:
  ```csharp
  private readonly IAsyncLock asyncLock; // injected via constructor

  using IDisposable sync = await asyncLock.LockAsync(entity.Id, cancellationToken);
  // critical section — safe across awaits
  ```
- Prefer async `LockAsync`; use synchronous `Lock(key)` only when no `await` is possible in the
  critical section.
- Consuming products may sanction narrow, documented exceptions (a purely-synchronous critical
  section around non-DI infrastructure; a `SemaphoreSlim` used as a *concurrency limit* rather
  than mutual exclusion). **Core itself sanctions none** — Core code that seems to need one needs
  a design conversation first.
- Anything in Core that can be called concurrently must be *designed* for it and its
  thread-safety contract stated in the XML docs — "callers probably won't race" does not exist at
  foundation level.
