# Nordstein.Core.Storage

Product-agnostic EF Core storage foundation for Nordstein applications.

- `NordsteinDbContext` — a reusable context base that applies discovered model configurations and
  enforces the `UpdatedAt` optimistic-concurrency-token convention
- `Entity` stored-entity base, `IEntity` / `IArchivableEntity` contracts, and `IMapper<,>`
- `AbstractRepository<,>` and `ArchivableRepository<,>` — generic CRUD, paging, soft-delete,
  optimistic concurrency, deferred change notifications and an optional reference-data cache
- The ambient-transaction seam: `AmbientDbContext` and `ITransaction`
- `EntityCache<>` / `EntityCacheVersions<>` — a scope-local cache with process-wide invalidation
- `AbstractEntityConfiguration<>`, and helpers (`LikePattern`, `ExcludeArchived`, the concurrency-token
  extensions)
- Autofac discovery of a consuming assembly's entities, configurations and repositories

Provider-neutral: it depends only on `Microsoft.EntityFrameworkCore.Relational`. Each product supplies
its own provider (Npgsql, in-memory, …), its concrete `DbContext` and its migrations assembly.

Register a product's storage assembly explicitly, and register its `DbContextOptions<TContext>` yourself
(that is where the provider is chosen):

```csharp
builder.RegisterModule(new StorageFoundationModule<StorageDbContext>(
    typeof(Product.Storage.Module).Assembly));
```

Core never scans all loaded assemblies and never assumes product types live beside the Core module.
