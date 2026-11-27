# Nordstein.Core.Domain — the domain foundation

The contracts and bases every product's domain layer is built on: entity/object contracts,
factory delegates, generators, repositories, transactions, archiving, paging, keyset cursors,
persistence exceptions, entity events, and the Autofac discovery that wires a product's domain
assembly.

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
implementing record mirrors the signatures one-to-one and takes its `IRepository<T>` and the
container's `IClock` as trailing constructor parameters, which the factory delegates fill from DI:

```csharp
// New — base ctor assigns a fresh Id and stamps CreatedAt and UpdatedAt from the clock
public Customer(string name, IAccount account, IRepository<ICustomer> repository, IClock clock)
    : base(repository, clock) { Name = name; Account = account; }

// Existing — base(existing, …) copies the identity data
public Customer(string name, IAccount account, IDomainEntityData existing, IRepository<ICustomer> repository, IClock clock)
    : base(existing, repository, clock)
{
    Name = name;
    Account = account;
}
```

The clock is the one source of "now" for an entity: it stamps the timestamps and
`DomainEntity.Validate` checks them against it (`Validation.InPast(value, clock.UtcNow)`), never
against the machine clock. A host that registers its own `IClock` (a demonstration instance whose
"today" is a fixed date) therefore stays consistent end to end.

`Nordstein.Core.Domain.Module` discovers the delegates and the implementations in the product
assembly it is handed and registers the factories — **with validation on activation**: Autofac's
`OnActivated` runs `Validator.ValidateObject`, so an invalid entity cannot be constructed through
DI at all. Repositories validate again before `Add`/`Update`. See
[`validation.md`](validation.md).

The activation validation is the public registration helper `ValidateOnActivation()`
(`Nordstein.Core.Domain.RegistrationBuilderValidationExtensions`), which the module applies to
every discovered entity. A product registration written by hand (a keyed registration that names a
constructor, for instance) chains it too, so it fails exactly like a discovered one: a
`DependencyResolutionException` wrapping the `ValidationException`. Only an `IValidatableObject`
is validated.

Autofac's implicit delegate factories pick a constructor by parameter count and match arguments by
type: two constructors of one arity, two `bool` parameters or an optional parameter whose type is
registered bind wrongly. For a union whose creation paths each need their own constructor,
`builder.RegisterFactory<TDelegate, TImplementation>(params Parameter[] fixedArguments)`
(`ContainerBuilderFactoryExtensions`) binds the delegate to the one constructor whose leading
parameter types are exactly the delegate's, then the fixed `TypedParameter`s' (fewest parameters
win; none or a tie throws when the module loads). Arguments go by position; the rest come from the
container; the implementation sits under a private key with `ValidateOnActivation()`, reachable only
through the delegate:

```csharp
builder.RegisterFactory<IPrincipal.CreateForUser, Principal>();
builder.RegisterFactory<IPrincipal.CreateForAdmin, Principal>(TypedParameter.From(PrincipalKind.Admin));
```

Discovered `CreateNew`/`CreateExisting` delegates are unchanged.

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
  `OptimisticConcurrencyException`, `EntityAlreadyExistsException`, …) so products can handle them
  uniformly. `EntityAlreadyExistsException` has two shapes: `(Guid id, Type)` for an id clash the
  repository found, and `(Type, Exception? inner)` for a unique-key clash the storage engine
  refused, which a product's context raises (with the engine's exception inside) instead of
  leaking its provider exception.
- Entity change events (`Events/`) let services react to add/update/remove without coupling to
  the writer.

## Keyset cursors (`OpaqueCursor`)

`Paging/OpaqueCursor` turns a product's keyset position into an opaque string and back. Core knows
no position shapes and no SQL: the product declares a record per position (for example
`(DateTimeOffset At, Guid Id)`) and writes its own keyset query.

- `Encode<T>(position)` writes unpadded base64url of the position's UTF-8 JSON, at most
  `OpaqueCursor.MaxLength` (4096) characters; longer, or `null`, throws.
- `Decode<T>(cursor)` throws `ArgumentException` (`ParamName == "cursor"`) for anything that
  does not yield a `T`: null/empty, too long, padded or non-canonical base64url, not JSON,
  trailing data, missing/unknown/duplicate members, wrong types, `null` in a non-nullable member.
  It never lets `JsonException` or `FormatException` escape, so a route maps one exception to
  its "bad request" answer.
- The `System.Text.Json` options are fixed and private, so every product writes the same format.
- A cursor is **neither signed nor encrypted**. It is untrusted input: re-apply access control on
  every page and validate the decoded position (for example, a value's type for its key).

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
