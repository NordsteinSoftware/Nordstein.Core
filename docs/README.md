# Nordstein.Core docs

Developer documentation for the shared, product-agnostic Nordstein.Core packages.

| Doc | Covers |
|-----|--------|
| [`storage.md`](storage.md) | `Nordstein.Core.Storage` — the EF Core storage foundation: `NordsteinDbContext`, the generic repositories, the ambient-transaction seam, the reference-data cache, and how a product consumes them. |

See also [`../PUBLISHING.md`](../PUBLISHING.md) for how Core is packaged and published, and — in the
consuming product — `docs/code-reuse.md` for how a product references Core (source vs package mode) and
what remains to be extracted.
