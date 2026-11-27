# Text — normalization, chunking and rendering with exact offsets

`Nordstein.Core.Common.Text` holds the text helpers that keep offsets exact: `CodePointText` (a
string indexed in code points), `OffsetMappedText` and its builder (a transformed text that maps
each code point back to its original) and the services below. Offsets are code points unless a
member says otherwise; a lone surrogate reads as one U+FFFD everywhere.

## `ITextNormalizer` — normalization that maps every raw position

Injected (`Module` registers a singleton; pure, thread-safe, linear in the input).
`Normalize(raw)` runs every step, `Normalize(raw, options)` the steps `TextNormalizationOptions`
switches on. The result is a `NormalizedText`: `Text`, `Length` (code points), `RawLength` (UTF-16
units of the input) and the map.

| Step (option, default on) | What it does |
|------|------|
| `RemoveInvisible` | Removes U+00AD soft hyphen, U+200B–U+200D zero-width space, non-joiner, joiner, U+2060 word joiner, U+FEFF byte order mark. |
| `RemoveControls` | Removes U+0000–U+001F and U+007F–U+009F unless `Rune.IsWhiteSpace` (tab, LF, VT, FF, CR, U+0085 stay). |
| `FoldLigatures` | U+FB00–U+FB04 become `ff` `fi` `fl` `ffi` `ffl`. |
| `Compose` | NFC, one grapheme cluster at a time (NFC never composes across clusters, so the result is NFC of the whole). |
| `CollapseWhitespace` | Leading and trailing whitespace goes; every other run becomes one space, or `\n\n` when it holds two or more line breaks. |

- The order is fixed: lone surrogates replaced, removal and folding, then composition, then
  whitespace. Removal first lets `e`, U+200D, U+0301 compose to `é`; folding first lets `ﬁ` + U+0301
  compose to `fí`. With the defaults the output is NFC and normalizing it again changes nothing.
- Line breaks: LF, CR, CRLF (one), U+0085, U+2028 count one each; U+2029 counts two. VT and FF are
  whitespace without a break. So `\r\r` (two old-style breaks) is a paragraph, `\r\n` is not.
- Every step off: the identity, except that a lone surrogate still becomes U+FFFD.

### The map

Raw positions are UTF-16 indices `0..RawLength` (between characters); normalized offsets are code
points. Two answers per position, because removed characters and collapsed whitespace belong to
neither neighbour:

- `StartOffsetOf(i)`: where a range beginning at `i` begins: the first normalized character from
  the raw text at or after `i`, past any separator; `Length` if none.
- `EndOffsetOf(i)`: where a range ending at `i` ends: just after the last normalized character from
  the raw text before `i`, before any separator; `0` if none.
- `ToNormalizedRange(start, end)`: `[StartOffsetOf(start), EndOffsetOf(end))`, never reversed: a raw
  range of only whitespace or removed characters, or an empty one, is empty at
  `StartOffsetOf(start)`.

A position inside one character widens outward: between the halves of a surrogate pair, or inside
a grapheme cluster that composition changed (`e` + U+0301 → `é`), a range starting there starts
before the character and a range ending there ends after it. So a mapped range keeps every
character its raw range touched and never starts or ends on a separator. Both maps never decrease;
`StartOffsetOf(0)` is 0 and `EndOffsetOf(RawLength)` is `Length`. Positions out of range throw
`ArgumentOutOfRangeException`.

A product that stores normalized text keeps its own normalization version and bumps it when it
changes the options it passes or when this output changes.

## `StructureMarker` — the structure both chunker and renderer read

`StructureMarker(Kind, Start, End, Level = 0)`: a half-open code-point range of the text it is
passed with. `StructureKind` values are stable (stored by number): `Heading` 1 (level = depth),
`Paragraph` 2, `ListItem` 3 (level = nesting), `TableCell` 4 (level = 1-based column, 0 = next),
`PageBreak` 5 (zero-length, level = the page that starts there), `Table` 6, `TableRow` 7. Both
consumers take the markers in any order and throw `ArgumentOutOfRangeException` (parameter
`markers`, the message names the index, never the text) for a marker that starts before 0, ends
before its start or ends after the text. Markers of other kinds, and zero-length markers other
than page breaks, are ignored.

## `ITextChunker` — structure-aware chunks with overlap

Injected (`Module` registers a singleton; pure, thread-safe). `Chunk(text, markers)` or
`Chunk(text, markers, options)` → `TextChunk(Ordinal, Start, End, Breadcrumb, Text)`, `Text` the
exact slice.

| `TextChunkingOptions` | Default | Rule |
|---|---|---|
| `MaxChunkLength` | 2048 | > 0, code points |
| `OverlapLength` | 300 | ≥ 0, code points, realized in whole units |
| `MaxSentenceLength` | 2048 | > 0 and ≤ `MaxChunkLength` |
| `MaxBreadcrumbLength` | 1024 | > 0, UTF-16 units; longer: cut on a code point, ends in `…` |

Invalid options throw `ValidationException` naming the member, on every call.

- Units: `Heading`, `Paragraph`, `ListItem`, `TableCell`, in start order (longer first on a tie),
  trimmed of whitespace. A marker starting inside an earlier unit keeps only its part after it;
  nothing when it ends inside. Blank units are none. Text outside every unit is in no chunk; no unit
  markers, no chunks.
- A unit over `MaxChunkLength` becomes pieces: the longest run ending at a sentence end that fits;
  the rest whole once it fits; a stretch without a sentence end at its last whitespace within
  `MaxSentenceLength`, else hard at that length (on a code point; it may part a letter from its
  mark). Pieces never start or end with whitespace.
- **Known limit:** a sentence ends only at `.` `!` `?` `…` followed by whitespace. Abbreviations
  (`e.g. this`) end one; `。`, `؟` and a terminator before a closing quote do not.
- A chunk is the longest run of units whose span fits. The next starts at the earliest unit that
  keeps the overlap within `OverlapLength` and can still reach past the previous end. So starts and
  ends strictly increase, no chunk lies inside another, every chunk starts and ends on a unit, and
  every non-whitespace code point of every unit is in a chunk.
- Breadcrumb: the trimmed texts of the heading units in effect at the chunk's start (a heading of a
  level ends every heading of that level or deeper), joined by ` > `.

## `IMarkdownRenderer` — Markdown over the same markers

Injected (`Module` registers a singleton; pure, thread-safe). `Render(text, markers)` or
`Render(text, markers, options)`; `MarkdownRenderingOptions.PageLabel` (default `null`; set: not
blank, one line) labels page breaks.

- Units in start order: `Heading`, `Paragraph`, `ListItem`, `Table`, `TableCell`, `PageBreak` (at one
  offset page breaks first, in the order given, then the longer marker). A unit starting inside a
  rendered one drops; its text outside that unit renders as loose text. A page break inside a unit
  drops. Loose text that is not only whitespace renders trimmed, as a paragraph.
- Heading: `#` × level clamped 1–6, a space, the text. List item: two spaces × (level clamped 1–9,
  minus 1), `- `, the text. Headings, list items and cells write CRLF, CR, LF as one space, trimmed;
  cells write `|` as `\|`. Paragraph and a cell outside every table: verbatim. Blank units render
  nothing. Page break: `---`, then `{PageLabel} {Level}` when set, also on an empty page.
- Table: rows are the `TableRow` markers inside it, cells the `TableCell` markers inside a row; a
  row inside the previous row and a cell inside the previous cell drop. Column: the level when after
  the previous cell's column, else the next. As wide as the widest row; `| --- |` after the first
  row; no row with a cell: the trimmed text as a paragraph. A cell level above
  `IMarkdownRenderer.MaxTableColumn` (16384) throws `ArgumentOutOfRangeException`.
- Blocks are separated by a blank line; the output ends with `\n`, or is empty.

A product whose markers count UTF-16 units (raw extractor output) maps them to code points first,
for instance through `ITextNormalizer`'s map or by counting with `CodePointText`.
