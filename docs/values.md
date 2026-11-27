# Values — culture-aware parsing and canonical JSON

Two pure helpers in `Nordstein.Core.Common` for values a product reads out of documents and
stores: `Culture.CulturalValueParser` re-reads a number or date under a declared format, and
`Serialization.CanonicalJson` gives a JSON document one spelling so a hash over it is stable.
Both are static, stateless and thread-safe.

## `CulturalValueParser`

The caller declares the format (typically detected per document); the parser confirms that the
text is written in it, or refuses. It never guesses, so a successful parse is evidence that the
text and the declared format agree — the reason to use it for verifying a value a model or a user
normalized.

| Member | Accepts | Refuses |
|--------|---------|---------|
| `TryParseDecimal(raw, decimalSeparator, groupingSeparator, out value)` | an optional sign (`-`, `+`, U+2212); ASCII digits; grouping only between groups (first 1–3 digits, then exactly 3); one decimal separator with at least one digit after it; one currency mark before or after (a `Sc` symbol such as `€`, or three uppercase ASCII letters such as `EUR`); surrounding whitespace | the other culture's separators (`1.234,50` under `.`/`,`), malformed groups (`1.2345`, `1234.567,00`), a missing integer or fraction digit (`.5`, `5.`), two currency marks, lowercase codes, words, ranges, exponents, parentheses, trailing signs, non-ASCII digits, overflow |
| `TryParseDecimal(…, out value, out currency)` | the same; `currency` is the mark as written, `null` without one | |
| `TryParseDate(raw, DateOrder, out value)` | ISO `yyyy-MM-dd` under every order; else three digit parts in the declared order joined by one separator used twice (`.`, `/` or `-`), each optionally followed by whitespace (`31. 1. 2027`); day and month 1–2 digits; a 4-digit year, or 2 digits under `Dmy`/`Mdy` with the fixed pivot 2049 | month names, weekdays, times, mixed separators, impossible dates (`29.02.2027`), a year-first date under `Dmy`/`Mdy` unless ISO |

- Any three uppercase ASCII letters count as a currency code: `2 STK` parses as `2` with `currency`
  `STK`. The parser checks no list of real currencies; a caller that needs one checks the code itself.
- A grouping separator that is a space separator (`Zs`: space, no-break space, thin space) accepts
  any space separator in its place; every other separator matches exactly.
- Separators are validated as preconditions: each exactly one character, not a letter, digit, sign
  or currency symbol, the decimal one not whitespace, and the two different. A violation throws
  `ArgumentException` (`ArgumentNullException` for `null`); an undefined `DateOrder` throws
  `ArgumentOutOfRangeException`. Text that does not parse returns `false`, never throws.
- Fraction digits beyond `decimal` precision round as `decimal.Parse` does.
- The two-digit-year pivot is a constant, never the machine's calendar setting: the result is the
  same on every host.

## `CanonicalJson`

`Serialize(JsonNode?)` sorts every object's properties ordinally at every depth, keeps array order,
writes no whitespace and escapes with `System.Text.Json`'s default encoder. A number parsed from
text keeps its spelling (`1.50` stays `1.50`): the form is canonical for the document as written,
not for its numeric values (it is not RFC 8785). `null` serializes as `null`; the input node is
never changed and may have a parent.

Use it before hashing a document whose producer may reorder or re-space members, such as a model
reply: `SHA256.HashData(Encoding.UTF8.GetBytes(CanonicalJson.Serialize(node)))`. Store the hash of
the canonical text you computed, not of what a database returns: a JSON column may re-order or
re-space the document on output.
