# Text — normalization with offset maps

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
