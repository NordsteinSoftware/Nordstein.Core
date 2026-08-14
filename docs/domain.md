# Nordstein.Core.Domain — the domain foundation

The contracts and bases every product's domain layer is built on: entity/object contracts,
factory delegates, generators, repositories, transactions, archiving, paging, persistence
exceptions, entity events, and the Autofac discovery that wires a product's domain assembly.

## Domain entities vs domain objects

- **`IDomainEntity`** — a persistent root with identity: `Id`, `CreatedAt`, `UpdatedAt` (all
  provided by the base — a product never redeclares them). Has a stored counterpart, a mapper,
  and a repository on the storage side.
- **`IDomainObject`** — a value object with no identity. No storage entity of its own; it is
  embedded in or serialized inside a parent's stored representation. Generators implement
  `IDomainObjectGenerator<T>` and are auto-registered alongside entity generators.

Products implement entities as immutable `internal record` types extending `DomainEntity`, behind
a `public` interface extending `IDomainEntity`.

## The factory-delegate pattern

Domain entities are **never constructed with `new`** by callers — each domain interface declares
exactly two delegates, resolved from DI:

```csharp
public interface ICustomer : IDomainEntity
{
    public delegate ICustomer CreateNew(string name, IAccount account);
    public delegate ICustomer CreateExisting(string name, IAccount account, IDomainEntityData existing);

    string Name { get; }
    IAccount Account { get; }
}
```

`CreateExisting` takes the same positional parameters as `CreateNew` plus a trailing
`IDomainEntityData existing` (the persisted `Id`/`CreatedAt`/`UpdatedAt`/`IsArchived`). The
implementing record mirrors the signatures one-to-one:

```csharp
// New — base ctor assigns fresh Id, CreatedAt, UpdatedAt
public Customer(string name, IAccount account) { Name = name; Account = account; }

// Existing — base(existing) copies the identity data
public Customer(string name, IAccount account, IDomainEntityData existing) : base(existing)
{
    Name = name;
    Account = account;
}
```

`Nordstein.Core.Domain.Module` discovers the delegates and the implementations in the product
assembly it is handed and registers the factories — **with validation on activation**: Autofac's
`OnActivated` runs `Validator.ValidateObject`, so an invalid entity cannot be constructed through
DI at all. Repositories validate again before `Add`/`Update`. See
[`validation.md`](validation.md).

## Generators (test data)

Each entity ships a generator extending `DomainEntityGenerator<I[Entity]>` — the test-data
factory the shared harness builds on:

- `GenerateAsync` — a valid in-memory instance, not persisted.
- `CreateAsync` — a fresh instance, persisted through the real repository.
- `GetOrCreateAsync` — reuse the previously created instance or create one (for FK targets).

A generator must produce an entity that passes validation with no arguments — that is what lets a
test ask for "some valid customer" without caring about the details.

## Repositories and transactions

- `IRepository<T>` is the generic contract (get, list, paged list, add, update, remove, upsert,
  `GetManyAsync`); products add a custom `I[Entity]Repository` only for N:M relationships or
  non-trivial queries.
- Repositories speak **domain interfaces only** — stored entities never cross the boundary.
- Writes run inside `ITransaction.InvokeAsync`; nested calls share one ambient context and change
  notifications fire only after the outermost commit. The EF-side implementation of all of this is
  `Nordstein.Core.Storage` — see [`storage.md`](storage.md).
- Failures surface as the shared persistence exceptions (`EntityNotFoundException`,
  `OptimisticConcurrencyException`, …) so products can handle them uniformly.
- Entity change events (`Events/`) let services react to add/update/remove without coupling to
  the writer.

## Foreign keys: hard-won conventions

The boundary is sharp — **domain holds the entity, storage holds the `Guid`** — and delete
behavior deserves real thought:

- **`Restrict` for references, `Cascade` only for genuinely owned children.** Ask "can this row
  be recreated?" before choosing `Cascade`. A row that cannot be recreated (a hashed credential,
  an audit trail) is never an "owned child" however strong the ownership reads — a cascade does
  not detach it, it destroys it, and the failure surfaces much later as unexplained errors.
- **FK-free denormalized snapshot** — an entity that must *survive deletion of its referent*
  (audit entries, provenance links) holds no FK at all: it stores the referenced id as a plain
  `Guid?` column so the row outlives what it points at.

## Soft delete (archiving)

Config/reference entities that historical data resolves by stored id must not be hard-deleted —
the dependents would either be blocked (FK `Restrict`) or start throwing `EntityNotFoundException`
at map time. The foundation ships an opt-in soft delete instead:

- Domain: the interface extends `IArchivable`; the custom repository interface extends
  `IArchivableRepository<T>` (adds `ArchiveAsync`). `IsArchived` lives on `IDomainEntityData` as
  a default-`false` member.
- Storage: implement `IArchivableEntity`, extend `ArchivableRepository` — details in
  [`storage.md`](storage.md).
- **Filtering rule (critical): never use an EF global query filter.** A global filter also hides
  archived rows from by-id lookups, breaking the very history archiving exists to protect.
  Exclude archived rows **only** in true list/picker queries; leave all by-key lookups —
  `GetAsync`, `GetManyAsync`, and fingerprint/name resolution like `GetOrCreateAsync` —
  unfiltered so history and attribution keep resolving.
- For entities whose history a hard delete would cascade-destroy, make archiving the *only*
  delete path: `ArchivableRepository.SupportsHardDelete => false` refuses `RemoveAsync` in
  application code, **complemented** (not replaced) by a database-level FK `Restrict` as the
  backstop against raw SQL and bulk deletes the repository never sees.
- A by-key `GetOrCreateAsync` that matches an archived row should **un-archive** it
  (`UnarchiveAsync`) rather than leave a live-but-hidden zombie.

## The five-file pattern (what a product writes per entity)

For each domain concept a product adds five files — domain interface, domain record + generator
(`Internal/`), stored entity record (`[StoredDomainEntity(typeof(I…))]`), and an EF
configuration + mapper — all discovered automatically by the domain and storage modules; no
manual wiring. A custom repository pair is added only for N:M or non-trivial queries. The
authoritative walkthrough (with the product's own reference implementations) lives in the
consuming product's docs; the machinery it rides on is all here and in
[`storage.md`](storage.md).
