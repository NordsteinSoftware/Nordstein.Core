# Search: `Nordstein.Core.Search` and `Nordstein.Core.Search.Postgres`

A named index of text entries with string facets, searched by meaning (embedding vectors) and by
keyword, fused by reciprocal rank. `Nordstein.Core.Search` holds the contract;
`Nordstein.Core.Search.Postgres` implements it on pgvector. The decision record is
[ADR 0007](adr/0007-opt-in-provider-packages-and-vector-search.md).

## The contract

| Type | Role |
|---|---|
| `SearchIndexDefinition` | Name, embedding model id, dimension (1 to 4,000) and text-search configuration. Validated on construction: both names are plain identifiers, because a provider interpolates them into DDL. |
| `ISearchSchema` | `EnsureAsync` creates or verifies an index; `FindAsync` reads the bound definition; `RemoveAsync` drops an index. |
| `ISearchIndex` | Obtained through the `ISearchIndex.Open` delegate. Upsert, list entries without a vector, store vectors, replace facets, remove owners, search. |
| `SearchEntry` | One unit of text: `Id`, `OwnerId` (the consumer's object the text was cut from), `Text`, `Facets`. |
| `SearchEmbedding` | A vector with the text it was computed from. |
| `SearchQuery`, `SearchFilter`, `SearchParameters` | Text, optional embedding, the facet filter, the tuning. |
| `SearchHit` | Entry, owner, text, fused score, and the rank in each leg. |
| `SearchSchemaMismatchException` | The index exists with another model, dimension or configuration. |

`Nordstein.Core.Search` registers nothing and has no module: it holds only contracts and values.

### Rules a consumer relies on

- **One model per index.** Vectors of two models are not comparable. `EnsureAsync` binds the
  definition on first creation and throws `SearchSchemaMismatchException` for a different one.
  Moving to another model is `RemoveAsync`, `EnsureAsync`, and upserting everything again.
- **Write, then embed.** `UpsertAsync` makes a text findable by keyword at once.
  `ListWithoutEmbeddingAsync` feeds a background job (an `IPeriodicJob`) that calls the embedding
  endpoint and stores the result with `SetEmbeddingsAsync`. An endpoint outage delays the vector
  leg and never fails a write. The list is ordered by id and takes an `afterId`, so a job moves
  past an entry the endpoint keeps refusing instead of reading it again in the same pass.
- **A vector belongs to a text.** `SetEmbeddingsAsync` stores a vector only while the entry still
  holds the text it was computed from. `UpsertAsync` with a changed text drops the old vector.
- **Search degrades, it does not fail.** A `SearchQuery` without an embedding runs the keyword
  leg alone. A consumer catches `InferenceUnavailableException` around its embed call and
  searches without the vector.
- **Facets are opaque.** A consumer encodes its dimensions as strings (`kind:note`,
  `state:open`). `SearchFilter.Require` is a list of groups: an entry passes when it carries at
  least one facet of every group and none of `Exclude`.
- **Everything joins the ambient transaction.** A consumer writes its own rows and the entries in
  one `ITransaction.InvokeAsync`.

## The PostgreSQL implementation

Register `Nordstein.Core.Search.Postgres.Module` next to `StorageFoundationModule<TContext>`
with a PostgreSQL provider. The package references no driver: it sends plain SQL through
ADO.NET on the transaction's connection. Array parameters (`string[]`, `Guid[]`) rely on the
driver mapping CLR arrays to PostgreSQL arrays, which Npgsql does.

```csharp
builder.RegisterModule(new StorageFoundationModule<AppDbContext>(typeof(Module).Assembly));
builder.RegisterModule<Nordstein.Core.Search.Postgres.Module>();

// At startup, after the product's migrations:
var notes = new SearchIndexDefinition("notes", embeddingClient.ModelId, embeddingClient.Dimension, "english");
await schema.EnsureAsync(notes, cancellationToken);
ISearchIndex index = open(notes);
```

### Objects

Per index `<name>`, in the connection's default schema, outside any EF model:

- one row in `search_identity (name, embedding_model, dimension, text_search_config, created_at)`;
- `search_<name>_entries (id, owner_id, text, facets text[], embedding, tsv)`, where `tsv` is a
  generated column over the index's configuration and `simple`;
- an HNSW index on `embedding` (cosine), GIN indexes on `tsv` and `facets`, a B-tree on
  `owner_id`, and a partial index on the rows without a vector.

`embedding` is `vector(N)` up to 2,000 dimensions and `halfvec(N)` (half precision) from 2,001 to
4,000: pgvector's HNSW index takes no larger `vector`.

`EnsureAsync` needs pgvector 0.8.0 or newer (iterative index scans) and the right to create the
`vector` extension or find it installed. It runs in one transaction under an advisory lock, so
two hosts starting at once do not collide. A bound index whose objects all exist runs no DDL:
even `CREATE INDEX IF NOT EXISTS` locks its table against writes.

### The search statement

One statement, two legs, each limited to `CandidatesPerLeg`:

- **Vector leg:** `embedding <=> query` ordered through the HNSW index, with
  `hnsw.iterative_scan = relaxed_order` and `hnsw.ef_search = max(40, CandidatesPerLeg)` set for
  the transaction, so a narrow facet filter still yields enough candidates.
- **Keyword leg:** `websearch_to_tsquery` in the index's configuration or in `simple`, matched
  on the GIN index and ranked by `ts_rank_cd`.

Fusion sums `1 / (RrfK + rank)` over the legs; ties break on the entry id. Only the fused top is
joined back for its text. There is no trigram leg: similarity over prose rechecks a large part of
the table on every query.

### Limits

| Limit | Value |
|---|---|
| Entry text | 32,768 UTF-16 code units, storable (no U+0000, no lone surrogate) |
| Facets per entry; facet length | 64; 128 |
| Filter groups; facets per group | 16; 64 |
| Query text | 4,000 code units |
| `CandidatesPerLeg`, `RrfK`, `MaxResults` | at most 1,000, 1,000 and 200 |

## Tests

`Nordstein.Core.Search.Postgres.Tests` runs on a real PostgreSQL with pgvector through
[`Nordstein.Core.Testing.Postgres`](testing.md#the-postgresql-harness-nordsteincoretestingpostgres).
A plan test turns sequential scans off and asserts that each leg is answered from an index: on a
small table the planner would pick a sequential scan, so this proves the operators match the
index classes, not that a plan is fast. A product with a large index adds its own perf budget
([`testing.md`](testing.md#performance-budgets)).
