# Nordstein.Core.Search

Product-agnostic search contracts: a named index of text entries with string facets, searched by
meaning and by keyword, fused by reciprocal rank.

| Type | Role |
|---|---|
| `SearchIndexDefinition` | Name, embedding model, dimension and text-search configuration of one index. |
| `ISearchSchema` | Creates, verifies and removes an index; refuses a definition that differs from the bound one. |
| `ISearchIndex` (`ISearchIndex.Open`) | Upsert entries, store their vectors, replace facets, remove owners, search. |
| `SearchEntry`, `SearchEmbedding` | One unit of text with its owner and facets; its vector. |
| `SearchQuery`, `SearchFilter`, `SearchParameters`, `SearchHit` | A search and its results. |

This package holds contracts and value types only and registers nothing. A provider package
implements it: `Nordstein.Core.Search.Postgres`.

See [`docs/search.md`](../docs/search.md).
