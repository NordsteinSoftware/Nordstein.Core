# ADR 0007: Opt-in provider packages, vector search and the embedding client

- Status: accepted (the owner asked for vector search in Core and approved the package shape,
  2026-11-28)
- Date: 2026-11-28
- Source design: `NordsteinSoftware/nordstein-hivemind`, `docs/design.md` (decisions 3 and 4)
- Settles: ADR 0001 decision 7 (the provider-package rule and three of its four waiting items)

## Context

Core has the embedding contract (`Nordstein.Core.AI.Embeddings.IEmbeddingClient`) and no way to
store or search vectors. nordstein-dms solved it product-side: hand-written SQL on pgvector, an
HNSW cosine leg and a `tsvector` leg fused by reciprocal rank, and a bound schema identity that
fails the boot when the model changes. A second product (the hivemind, a message board for coding
agents) now needs the same search, and nordstein-dms may be discontinued.

Three written rules stood in the way:

- `CLAUDE.md`: provider- and framework-specific packages stay in the products.
- `docs/ai.md`: provider implementations stay in the products.
- `docs/dependencies.md` item 2: no provider coupling in a shipped project.

ADR 0001 decision 7 parked four items behind a rule the owner had to approve:
`Core.Testing.Postgres`, `Core.Storage.Postgres`, the HTTP-only wire clients and
`Core.AI.Testing`.

## Decision

1. **The provider-package rule is adopted.** A provider-specific package is allowed in Core when
   all of these hold:
   - it is opt-in: no other Core package references it, so a product that does not want the
     provider never restores it;
   - it has its own project, named `Nordstein.Core.<Area>.<Provider>`;
   - it adds at most one provider dependency, and none where plain BCL calls do the job;
   - its contract lives in a provider-neutral package, so a product's inner rings never see the
     provider.

   Every other package stays provider-neutral, as before.
2. **`Nordstein.Core.Search`** holds the search contract: `SearchIndexDefinition`,
   `ISearchSchema`, `ISearchIndex` with its `Open` delegate, and the value types. It references
   `Nordstein.Core.Common` only and registers nothing.
3. **`Nordstein.Core.Search.Postgres`** implements it on pgvector and `tsvector`. It references
   `Nordstein.Core.Search` and `Nordstein.Core.Storage` and **no driver**: statements go through
   ADO.NET's `DbCommand` on the ambient transaction's connection, vectors travel as text cast to
   `vector` or `halfvec`, and rows travel as one JSON parameter. Zero provider dependencies.
4. **`Nordstein.Core.Testing.Postgres`** is the PostgreSQL test harness: one container per test
   process, one database per test. Its one provider dependency is `Testcontainers.PostgreSql`
   (MIT, .NET Foundation project). Statements on whole databases run through `psql` inside the
   container, so it needs no driver either.
5. **`Nordstein.Core.AI.OpenAiCompatible`** is the embedding client for the OpenAI embeddings
   wire format. It is HTTP and `System.Text.Json` only. `Nordstein.Core.AI` itself stays free of
   transport code; `docs/ai.md` now says "no provider SDKs" instead of "no provider
   implementations".
6. **Core's own tests of these packages run on a real PostgreSQL** with the pinned
   `pgvector/pgvector:0.8.2-pg17` image. They are inconclusive without a container runtime and
   required in CI (`NORDSTEIN_CORE_REQUIRE_DOCKER_TESTS=1`), which is the policy `docs/testing.md`
   already states for container-backed tests. The test projects reference `Npgsql`; nothing
   shipped does.
7. **The search index is generic.** Facets are opaque strings; the index knows no post, document,
   tenant or access rule. The One Rule's ban on "search" in `Nordstein.Core.AI` meant a product's
   own search over its own entities; that stays banned there.

## What was ported, and what changed

Source: `NordsteinSoftware/nordstein-dms`, `Nordstein.Dms.Storage/Search/Internal`
(`SearchIndex.cs`, `SearchSchema.cs`, `VectorLiteral.cs`), `Nordstein.Dms.Testing/Postgres` and
`Nordstein.Dms.Inference/Embeddings/Internal/OpenAiCompatibleEmbeddingClient.cs`, ported under
ADR 0001 decisions 1 and 2 (port, do not move; the Standard of Care applies as if new).

| Product shape | Core shape | Why |
|---|---|---|
| One index per database, chunks joined to documents | Any number of named indexes, each one table | A product may index two kinds of text. |
| Visibility through audience columns | Facet filter in conjunctive form | Core knows no access model; a consumer encodes its dimensions as strings. |
| Vector row written with the chunk | `UpsertAsync`, then `SetEmbeddingsAsync` guarded by the text | A write must not wait for, or fail with, the embedding endpoint. |
| `vector(N)`, at most 2,000 dimensions | `vector(N)` up to 2,000, `halfvec(N)` up to 4,000 | pgvector's HNSW limit; larger models stay usable at half precision. |
| DDL guarded by "one host boots at a time" | A transaction-scoped advisory lock | Two hosts of a product may start at once. |
| Database statements through Npgsql | `psql` in the container | The harness keeps to one provider dependency. |
| `HttpClient` with its own time-out | `HttpMessageInvoker` and a deadline on the container's `TimeProvider` | Tests drive the time-out without waiting. |

Not ported: the port-collision retry of the product's harness (Docker assigns the host port
itself) and the product's migrated-template logic (`PostgresTemplate` takes a callback instead).

## Alternatives considered

- **Contracts in Core, SQL in each product.** Core stays provider-neutral, but the second
  consumer copies about 500 lines of SQL and its tests, and the two copies drift. Rejected: that
  is the duplication Core exists to remove.
- **Everything in `Nordstein.Core.Storage`.** One package fewer, but every product would carry
  search and a PostgreSQL assumption. Rejected: breaks the provider-neutral promise for all.
- **Reference Npgsql and the pgvector plugin.** A typed `Vector` parameter and binary transfer.
  Rejected: two provider dependencies for what a text literal does; the product chooses its
  driver version.
- **A provider SDK for embeddings.** Rejected by `docs/dependencies.md` item 2, and the wire
  format is one request shape.

## Consequences

- Core ships eleven packages. A product that uses none of the four new ones restores none of
  their dependencies.
- CI needs Docker for two test projects. `ubuntu-latest` has it; the suite grows by about 25
  seconds.
- `Core.Storage.Postgres` and `Core.AI.Testing` (the remaining two items of ADR 0001 decision 7)
  may now be proposed under decision 1; neither is part of this change.
- nordstein-dms is not changed. If it lives on, moving it onto these packages is its own task.
