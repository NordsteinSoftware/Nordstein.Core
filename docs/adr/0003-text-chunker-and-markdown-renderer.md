# ADR 0003: Structure markers, text chunker and Markdown renderer in Core.Common

- Status: proposed
- Date: 2026-11-27
- Issue: [#74](https://github.com/NordsteinSoftware/Nordstein.Core/issues/74) (part of #54, ADR 0001 decision 6)

## Context

ADR 0001 decision 6 asks for a design before the chunker and the Markdown renderer move from
nordstein-dms (`Application/Analysis/Internal/Chunker.cs`,
`Application/StructuredText/Internal/MarkdownRenderer.cs`, source at `cf146a85`). The issue
names three open questions:

- the public shape of `StructureMarker` and `StructureKind`,
- the unit of the chunk sizes and the overlap,
- whether sentence ends stay fixed or become pluggable.

The port merged in PR #82 (`354fdb2`, `e20ed4a`) before this ADR was written. It is described in
`docs/text.md` and in the XML docs of `ITextChunker` and `IMarkdownRenderer`. This ADR records the
decisions that PR made, so that later changes have a reference. The answers did not need new
design work.

In dms, the two classes read product types: `RenditionDraft` (code points) and `StructuredPage`
(UTF-16, one object per page). dms's `StructureMarker` also has `RowSpan`, `ColSpan` and
`IsHeader`, which its extractors, storage and wire use. Neither class reads those fields.
Core has published no release yet, so a public shape can still change without a major version.

## Decision

1. **Input is `(string text, IReadOnlyList<StructureMarker> markers)`, offsets in code points.**
   Code points are what `CodePointText` counts. Both consumers check every marker against the
   text and throw `ArgumentOutOfRangeException`, which names the marker's index. They sort the
   markers themselves. The renderer moves from UTF-16 to code points, so one set of markers
   serves both consumers.
2. **`StructureMarker(Kind, Start, End, Level = 0)` is a readonly record struct and has no more
   fields.** `RowSpan`, `ColSpan` and `IsHeader` stay in dms, because no Core consumer reads
   them. A product that stores richer markers maps them to Core markers at the call site. If a
   Core feature later needs merged cells or header rows, add the fields before the first
   publish, or in a major version after it.
3. **`StructureKind` keeps the dms numeric values 1–7 and must never be renumbered.** A product
   can store the values. A page boundary is a zero-length `PageBreak` marker inside one text. It
   replaces dms's list of pages.
4. **Sizes are code points, with no token seam.** `TextChunkingOptions` has these defaults:
   `MaxChunkLength` 2048 (about 512 tokens at four characters each), `OverlapLength` 300 (made
   of whole units), `MaxSentenceLength` 2048 (at most the chunk length) and
   `MaxBreadcrumbLength` 1024 (UTF-16 units, because it is sized for a column). The options are
   validated on every call. A product that needs exact token budgets can choose smaller
   lengths.
5. **Sentence ends are fixed and the limit is documented.** A sentence ends at `.`, `!`, `?` or
   `…` followed by whitespace. Abbreviations count as ends. `。` and a terminator before a
   closing quote do not count. Sentence ends only matter when a single unit is longer than the
   chunk length. That case is rare, so a seam is not worth its cost now.
6. **Fixes over the dms code are part of the contract.** The next chunk never starts inside the
   previous one, and chunk starts and ends strictly increase. Units are trimmed, and blank
   units are skipped. Overlapping markers are clipped by the chunker and dropped by the
   renderer. The renderer's page label is configurable (`MarkdownRenderingOptions.PageLabel`,
   with no label by default) instead of always "Page". Heading, list and column levels are
   bounded (`IMarkdownRenderer.MaxTableColumn` = 16384).

## Alternatives considered

- **Keep UTF-16 offsets as the dms renderer does.** Rejected: the chunker already uses code
  points. Two offset units for one marker type would invite off-by-surrogate bugs.
- **Carry `RowSpan`, `ColSpan` and `IsHeader` in Core now.** Rejected: no consumer in Core uses
  them, and every field on a public struct is a promise. Adding them later is cheap until the
  first publish.
- **Size chunks in tokens through a tokenizer seam.** Rejected: it would give Core.Common a
  dependency on a model-specific tokenizer, or an extra interface every product must fill. The
  rule of about four characters per token is good enough to pack chunks.
- **Pluggable sentence splitter.** Rejected for now: it adds an interface for a rare path. It
  can be added later as an option that does not break callers.

## Consequences

- dms can drop its own chunker and renderer. It must map its markers to Core markers, and it
  must convert UTF-16 page offsets to code points before it calls the renderer. Core needs no
  change for this.
- The renderer's output changes for dms. It has no "Page N" label unless dms sets
  `PageLabel = "Page"`. Overlapping rows and cells are dropped.
- Adding a field to `StructureMarker` after the first publish breaks binary compatibility.
  Decision 2 is the place to look before anyone asks for one.
- No follow-up tasks: the code, tests and `docs/text.md` are on `main`.
