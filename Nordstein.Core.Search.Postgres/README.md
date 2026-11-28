# Nordstein.Core.Search.Postgres

The PostgreSQL implementation of `Nordstein.Core.Search`: a pgvector HNSW leg and a `tsvector`
keyword leg per index, fused by reciprocal rank in one statement.

```csharp
builder.RegisterModule(new StorageFoundationModule<AppDbContext>(typeof(Module).Assembly));
builder.RegisterModule<Nordstein.Core.Search.Postgres.Module>();
```

- Opt-in: no other Core package references it.
- No driver reference: plain SQL through ADO.NET on the ambient transaction's connection. The
  product brings its PostgreSQL provider (Npgsql).
- Needs pgvector 0.8.0 or newer. Vectors up to 2,000 dimensions are stored at full precision,
  2,001 to 4,000 at half precision (`halfvec`).

See [`docs/search.md`](../docs/search.md).
