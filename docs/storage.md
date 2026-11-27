# Nordstein.Core.Storage

The product-agnostic EF Core storage foundation. It provides the pieces every Nordstein product would
otherwise reimplement — a reusable `DbContext` base, generic repositories, the ambient-transaction
seam, a reference-data cache, and assembly-scoped Autofac discovery — while leaving everything
provider- and schema-specific in the consuming product.

The package depends only on `Microsoft.EntityFrameworkCore.Relational` (plus `Autofac` and
`Nordstein.Core.Domain`). **No provider** (Npgsql, in-memory, SQL Server, …) is referenced: the
product chooses one. This is what makes the foundation reusable by a second product that might not use
PostgreSQL.

## What's in the box

| Type | Role |
|------|------|
| `NordsteinDbContext` | Abstract `DbContext` base. Applies every registered `IModelConfiguration` and enforces the `UpdatedAt` optimistic-concurrency-token convention. Each product derives a concrete context from it. |
| `Entity` | Base record for stored entities: `Id`, `CreatedAt`, `UpdatedAt` + validation (set and ordered; the not-in-the-future check needs a clock and lives on the domain entity). |
| `IEntity`, `IArchivableEntity`, `IEntityAdapter`, `StoredDomainEntityAttribute`, `EntityExtensions` | Stored-entity contracts and the stored↔domain association the discovery reads. |
| `IMapper<TDomain,TStored>` | Bidirectional map between a domain entity and its stored form. One per entity, product-supplied. |
| `AbstractRepository<TDomain,TStored>` | Generic CRUD, paging (ordered by `CreatedAt` descending, then `Id` descending — a total order, so offset pages never repeat or skip same-instant rows), upsert, `GetMany`, optimistic concurrency, deferred change notifications, optional caching. |
| `ArchivableRepository<TDomain,TStored>` | Adds soft-delete (`ArchiveAsync`/`UnarchiveAsync`), excludes archived rows from list queries, and can refuse hard deletes. |
| `AmbientDbContext`, `ITransaction` (from Core.Domain) | The ambient-transaction seam: one shared context/connection per logical unit, with post-commit side effects. |
| `IEntityCache<T>`, `EntityCache<T>`, `EntityCacheVersions<T>`, `CacheableAttribute` | In-memory cache for slow-changing reference data, scope-local with process-wide invalidation. |
| `AbstractEntityConfiguration<T>`, `IModelConfiguration` | The per-entity EF configuration seam applied by `NordsteinDbContext`. |
| `LikePattern`, `ArchivableQueryExtensions.ExcludeArchived`, `ConcurrencyTokenExtensions` | Query/token helpers. |
| `IRequestTimingSource`, `RequestTimingOptionsExtensions.AddRequestTimings` | Opt-in command timing into the unit of work's `IRequestTimings` — see [Request timings](#request-timings). |
| `StorageFoundationModule<TContext>` | Autofac module that wires the seam above and discovers a product assembly's entities, configurations, repositories and caches. |

## Architecture

```
        product assembly                         Nordstein.Core.Storage
  ┌────────────────────────────┐         ┌──────────────────────────────────────┐
  │ StorageDbContext           │──────▶  │ NordsteinDbContext (model + UpdatedAt  │
  │   : NordsteinDbContext     │         │   concurrency-token convention)        │
  │ DbContextOptions<…> (Npgsql│         │                                        │
  │   / in-memory) — product    │        │ StorageFoundationModule<TContext>      │
  │ AgentEntity : Entity        │  scan  │   discovers entities/configs/repos/    │
  │ AgentConfig : Abstract…Conf │◀──────▶│   caches; registers AmbientDbContext,  │
  │ AgentRepository : Abstract… │        │   ITransaction, Func<DbContext>        │
  │ AgentMapper : IMapper<…>    │        │                                        │
  └────────────────────────────┘        │ AbstractRepository<,> / Archivable…    │
                                         │ EntityCache<> / EntityCacheVersions<>  │
                                         └──────────────────────────────────────┘
```

The repositories are threaded off `DbContext` (the base), not the product's concrete context, so the
foundation never names a product type. The concrete context exists only to key
`DbContextOptions<TContext>` and the migrations assembly.

### The one rule

Core may not reference the product. When something belongs on both sides, Core declares the interface
and the product implements it. `StorageFoundationModule<TContext>` follows this: it takes the product
assembly and the product's concrete context type as inputs rather than reaching for them.

## Consuming it from a product

**1. A thin concrete context** — carries identity and migrations, nothing else:

```csharp
internal sealed class StorageDbContext : NordsteinDbContext
{
    public StorageDbContext(IEnumerable<IModelConfiguration> configurations,
        DbContextOptions<StorageDbContext> options) : base(configurations, options) { }
}
```

**2. Register the foundation and your provider** in the product's Autofac module:

```csharp
// Discovery + the ambient-transaction seam + generic repositories/caches.
builder.RegisterModule(new StorageFoundationModule<StorageDbContext>(
    typeof(Module).Assembly,
    // storage-only join entities that carry no Id (and so do not implement IEntity):
    typeof(SomeJoinEntity)));

// The product owns the provider choice — register DbContextOptions<TContext> yourself.
builder.Register<DbContextOptions<StorageDbContext>>(ct =>
{
    var options = new DbContextOptionsBuilder<StorageDbContext>();
    options.UseNpgsql(connectionString,
        npgsql => npgsql.MigrationsAssembly(typeof(StorageDbContext).Assembly.GetName().Name));
    return options.Options;
}).SingleInstance();
```

`StorageFoundationModule` registers `AmbientDbContext`, `ITransaction`, an ambient-aware
`Func<DbContext>`, and — per discovered entity — the entity, its `AbstractEntityConfiguration<>` (as
`IModelConfiguration`), its `AbstractRepository<,>` (as every interface it implements) and, when the
entity is `[Cacheable]`, its `IEntityCache<>` and singleton `EntityCacheVersions<>`. A product service
that needs the concrete context registers its own `Func<StorageDbContext>` the same way.

**3. Per entity, five product-side pieces** (discovered automatically): a domain interface + entity, a
stored `… : Entity` record with `[StoredDomainEntity(typeof(IThing))]`, an
`AbstractEntityConfiguration<ThingEntity>`, an `IMapper<IThing, ThingEntity>`, and an
`AbstractRepository<IThing, ThingEntity>`.

## Conventions worth knowing

- **`UpdatedAt` from the container's `IClock`.** `AbstractRepository.UpdateAsync` and
  `ArchivableRepository`'s archive and unarchive stamp `UpdatedAt` from the injected `IClock`, never
  from the machine clock; a repository constructor takes the clock after `AmbientDbContext`.
- **Optimistic concurrency on `UpdatedAt`.** `NordsteinDbContext` marks every entity's `UpdatedAt`
  `DateTimeOffset` as an EF concurrency token, so `UPDATE/DELETE … WHERE UpdatedAt = @original` guards
  against lost updates. The repository pre-checks in memory and the database enforces it. Because a
  relational store persists microsecond precision but an in-memory token keeps 100-ns precision,
  comparisons happen at microsecond granularity (`ConcurrencyTokenExtensions.MatchesConcurrencyToken`) and
  realignment truncates with `Nordstein.Core.Common.Time.MicrosecondPrecisionExtensions.TruncateToMicroseconds`.
- **Soft delete.** Derive from `ArchivableRepository<,>` and implement `IArchivableEntity`. Archived
  rows are excluded from list/paged queries but still resolve by id, so history keeps loading.
  `ArchiveAsync` calls `LockForArchiveAsync`, then reads the row (a tracked copy is reloaded), then
  `ArchiveRelationsAsync`. The hook is a no-op; override it with the provider's row lock (PostgreSQL:
  `SELECT 1 … FOR NO KEY UPDATE`) so a concurrent archive of the same row returns `false` before any
  relation clean-up, not after it through the concurrency token.
- **Reference-data cache.** Mark a slow-changing entity `[Cacheable]`. Entries are per-lifetime-scope
  (a cached domain object holds the repository it came from), but invalidation is process-wide via the
  singleton `EntityCacheVersions<>`. Never cache high-volume entities. The cache is bypassed while a
  transaction is active.
- **The transaction seam.** Repository writes run inside `ITransaction.InvokeAsync`. Nested calls share
  the one ambient context/connection (never promoting to a two-phase transaction), and change
  notifications fire only after the outermost commit. The caller's cancellation token is honored when
  the transaction begins and inside the operation, never during commit or rollback: a commit cancelled
  mid-flight could report failure for a transaction the database did commit.
- **Fencing writes.** `AddAsync`, `AddRangeAsync`, `UpsertAsync`, `RemoveAsync`, `RemoveAllAsync` and the
  `UpdateRelationsAsync` hook (called inside `UpdateAsync`, before the save) are virtual. A repository of
  an immutable or guarded entity overrides them to throw. `UpdateAsync` itself is not virtual: fence it
  in `UpdateRelationsAsync`.
- **Search patterns.** Build `LIKE` right-hand sides with `LikePattern.Contains` (it lowercases and
  escapes wildcards); lower the column in the query too.

## Request timings

A product opts its context in to command timing, so the database time of a unit of work lands in that unit's
`IRequestTimings` ([diagnostics.md](diagnostics.md)) under `RequestTimingNames.Database` (`db`):

```csharp
internal sealed class StorageDbContext : NordsteinDbContext, IRequestTimingSource
{
    public StorageDbContext(IEnumerable<IModelConfiguration> configurations,
        DbContextOptions<StorageDbContext> options, IRequestTimings? timings = null)
        : base(configurations, options)
    {
        RequestTimings = timings;
    }

    public IRequestTimings? RequestTimings { get; }
}

options.UseNpgsql(connectionString).AddRequestTimings();
```

- `AddRequestTimings()` (`Nordstein.Core.Storage.Diagnostics`) adds one stateless `DbCommandInterceptor` to the
  options; calling it again adds nothing. Each executed, failed or cancelled command adds its duration and one
  count; each data reader adds its reading time (from creation until disposal), so a large result counts in
  full. No command text or parameter is recorded.
- The context hands over the accumulator through `IRequestTimingSource`, resolved with the context from the
  same lifetime scope; a context without the interface, or with no accumulator (design time, a migration
  tool), is skipped. `NordsteinDbContext` itself is unchanged, so opting in breaks no existing context.
- Hand-written ADO.NET commands on the context's connection bypass EF Core and are not timed; a product that
  runs them times them itself under the same name. The in-memory provider runs no command, so in-memory tests
  record no database time; the interceptor's own tests call its overrides directly.

## Tests

`Nordstein.Core.Storage.Tests` exercises the foundation against an in-memory `NordsteinDbContext` with
a self-contained test domain: discovery wiring, CRUD round-trips, the concurrency-token behaviour, the
model convention, the cache, and the query helpers. The in-memory provider lives in the **test** project
only — the library itself stays provider-neutral.
