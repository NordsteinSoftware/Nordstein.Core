# Architecture

Nordstein.Core is a small family of product-agnostic packages with a strict, one-way dependency
flow. Each package may only depend on packages below it:

```
Nordstein.Core.Storage ──► Nordstein.Core.Domain ──► Nordstein.Core.Common
Nordstein.Core.Licensing ─────────────────────────► Nordstein.Core.Common
Nordstein.Core.Testing ───────────────────────────► Nordstein.Core.Common
```

| Package | Role | Notable external deps |
|---------|------|-----------------------|
| `Nordstein.Core.Common` | Utilities every layer needs: clock (`Time/`) and randomness (`Random/`) seams, async primitives incl. the keyed `IAsyncLock` (`Async/`), validation helpers (`Validation/`), type conversion (`Conversion/`), secret-protection contracts (`Security/` — `ISecretProtector`, `ISecretHasher`, `Sha256`), AEAD cryptography (`Cryptography/` — `IAeadStreamCodec`, `IAeadKeyWrap`), durable file IO and secret-file custody (`Io/` — `IDurableFilePublisher`, `ISecretFileLoader`), serialization + text helpers, DI extensions (`DependencyInjection/`), lifecycle, hosting defaults (`Hosting/` — `AddResilientBackgroundServices`) | Autofac, MS.Extensions Hosting/DI abstractions |
| `Nordstein.Core.Domain` | Domain foundation: `IDomainObject`/`IDomainEntity` contracts and bases, factory-delegate conventions, generators, `IRepository`/`ITransaction`, archive contracts, paging, persistence exceptions, entity events, consuming-assembly discovery (`Module`) | Autofac |
| `Nordstein.Core.Storage` | EF Core storage foundation: `NordsteinDbContext`, generic repositories, the ambient-transaction seam, reference-data cache, `StorageFoundationModule<TContext>`. **Provider-neutral** — see [`storage.md`](storage.md) | EF Core **Relational only** (no provider), Autofac |
| `Nordstein.Core.Licensing` | Generic license engine: JWT verification, activation, snapshot/status, server check — see [`licensing.md`](licensing.md) | System.IdentityModel.Tokens.Jwt, MS.Extensions Http |
| `Nordstein.Core.Testing` | The shared test harness: `BaseTest<TModule>` + the MSTest / AwesomeAssertions / NSubstitute baseline — see [`testing.md`](testing.md) | MSTest, AwesomeAssertions, NSubstitute, Autofac |

Each package has a sibling `*.Tests` project; the whole solution builds and tests standalone
(`dotnet build/test Nordstein.Core.sln`) — that standalone property is the extraction guarantee.

## The one rule (repeated because it shapes everything)

**Core may not reference any product.** When a capability needs product knowledge, Core declares
the seam and the product implements it. Recurring shapes of that seam:

- **Interface + product implementation** — e.g. `ISecretProtector`/`ISecretHasher` live in
  `Common.Security`; each product supplies the Data Protection purpose, key-ring, and persisted
  scheme.
- **Assembly handed in, never assumed** — discovery modules take the product assembly as a
  constructor argument (`Nordstein.Core.Domain.Module`, `StorageFoundationModule<TContext>`)
  rather than scanning "known" locations.
- **Generic type parameter** — `StorageFoundationModule<TContext>` is keyed by the product's
  concrete `DbContext`; the foundation itself only ever handles the `DbContext` base.
- **Configuration object** — `LicensingConfiguration` carries the product's issuer/audience/keys
  into the generic engine.

Pick the same shapes for new seams; do not invent a fifth without a reason.

## Dependency Injection (Autofac)

Every package ships one `Module : Autofac.Module` (`Nordstein.Core.Common.Module`,
`Nordstein.Core.Domain.Module`, …). Conventions, proven across the consuming products:

- **One module per assembly**, registered by the consumer's composition root. A module registers
  its own assembly's services and (where it is a foundation) discovers types in the *consumer's*
  assembly that was passed in.
- **Discovery over manual registration** for pattern-shaped types: `Nordstein.Core.Domain.Module`
  discovers a product's entities, factory delegates and generators from the passed assembly;
  `StorageFoundationModule<TContext>` discovers stored entities, EF configurations, repositories
  and caches. This keeps a product's per-entity plumbing at five files with zero wiring edits —
  see [`domain.md`](domain.md).
- **Domain entities are validated on activation** — the domain module wires `OnActivated` to run
  `Validator.ValidateObject`, so an invalid entity cannot even be constructed through DI. Keep
  that guarantee intact.
- **Bridging to `IServiceCollection`**: modules that need Microsoft-DI extension methods
  (`AddHttpClient`, `AddMemoryCache`, …) go through the `RegisterServiceCollection` helper, which
  populates a fresh `ServiceCollection` into Autofac and **dedupes identical type-based
  descriptors across modules**. That dedup is load-bearing: MS extension methods share plumbing
  via `TryAdd`, which only dedupes within one collection — without the guard, four modules calling
  `AddHttpClient` registered four logging handlers and every outgoing HTTP request was logged four
  times. Don't bypass the helper.

## Hosting: what may kill the host

`AddResilientBackgroundServices()` (`Common/Hosting`) sets
`HostOptions.BackgroundServiceExceptionBehavior = Ignore`. .NET's default is `StopHost`: one
throwing `BackgroundService` stops the whole host with exit code **0** — a "clean" shutdown no
container restart policy acts on, with the explanatory log line lost because the process is
already dying. With `Ignore`, the faulted loop stops, everything else keeps serving, and the
fault is logged at `Error`.

This splits hosted services into two kinds, and consumers rely on the split:

| Kind | Base | On throw |
|---|---|---|
| Long-running loop (workers, schedulers, cleanups) | `BackgroundService` (work in `ExecuteAsync`) | Loop stops, logged, host keeps running |
| Startup-critical work (migrations, backfills, seeders) | `IHostedService` (work in `StartAsync`) | **Startup aborts** — unaffected by the option |

Rule of thumb for products: anything the app must not serve traffic without goes in `StartAsync`
on a plain `IHostedService`; anything whose failure should degrade one feature goes in
`ExecuteAsync` on a `BackgroundService`. `Ignore` keeps the *host* alive — a loop that wants to
survive its own transient failures still catches them itself.

## How consumers reference Core

A consuming product embeds this repository (today: git submodule) and declares an item, not a
reference:

```xml
<ItemGroup>
  <NordsteinCoreReference Include="Nordstein.Core.Common" />
</ItemGroup>
```

The product's `Directory.Build.targets` expands that into a `ProjectReference` (**source mode**,
the default with the submodule present) or a `PackageReference` at `$(NordsteinCoreVersion)`
(**package mode**). Both modes run on every consuming-product CI push: source mode as the everyday
build, package mode via a job that packs Core and rebuilds the product against the `.nupkg`s —
which is what catches a type public in source but missing from the package surface, or a
dependency Core forgot to declare. Full mechanics live in the consuming product's
`docs/code-reuse.md`; packaging decisions in [`../PUBLISHING.md`](../PUBLISHING.md).

`Directory.Build.props` here conditionally imports a parent props file so the same tree builds
identically standalone and inside a product checkout — read the comments in that file before
touching it.
