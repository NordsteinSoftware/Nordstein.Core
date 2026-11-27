# Settings — attributes, the registry, naming

`Nordstein.Core.Common.Settings` describes a product's settings: which configuration keys exist,
who they apply to, what they hold, and which are secret or name where data goes. It holds no
values. Reading and storing values is the product's; Core gives it one catalogue to list, find and
name settings by, the check of a typed value's form, and the expansion of templates against the
configuration's keys.

## Marking settings

| Attribute | On | Means |
|---|---|---|
| `[Setting(scope, description)]` | a property of a configuration record | the property is a setting (a leaf) |
| `[Protected]` | a `[Setting]` property | write-only: the value is a secret, never shown (`IsProtected`) |
| `[Sink]` | a `[Setting]` property | the value names where data goes or who may reach it (`IsSink`); a product asks for stronger proof before it changes one |

- `SettingScope`: `Global` (one value for the installation, part of the host configuration),
  `User` (one per user), `Local` (one per client installation, such as a browser).
- `description`: one sentence: what the setting controls, its unit, default and range. Never blank.
- `ValueType = typeof(…)`: the configured value's type when it differs from the property's (a
  record resolved by name is configured as `string`; a list of parsed values as
  `IReadOnlyList<string>`).
- Supported value types (`ValueType`, else the property type, `Nullable<T>` unwrapped): `string`,
  `bool`, `int`, `long`, `double`, `TimeSpan`, `Uri`, any enum, `IReadOnlyList<string>`.
- All three attributes are inherited: an override of a marked property stays marked.

## Roots and the walk

A product registers one `SettingRoot(path, recordType, readOnly)` per configuration section and
exactly one `SettingNaming` in its container; `Nordstein.Core.Common.Module` registers
`ISettingRegistry` (`SingleInstance`), built from all of them.

- Root path: `:`-joined segments of ASCII letters and digits, or the wildcard `*` at most once
  (`Mailer`, `Feeds:*`).
- The record is read by reflection: a public instance property with `[Setting]` is a leaf; a
  public instance property without it whose type (a class or struct, `Nullable<T>` unwrapped,
  never an interface) carries a setting attribute on a property, itself or through the types its
  own properties hold, is a nested section named by the property; everything else is ignored.
- So a grouping record without settings of its own is a section, and an unmarked property that
  returns its own record type is refused as a record graph that contains itself.
- An override keeps its base property's place and is judged by the base declaration's type; a
  property hidden with `new` is a second property (both marked: their paths collide).
- A setting's path is the root path, the section names and the property name, joined by `:`
  (`Mailer:Retry:Attempts`). Property names are ASCII letters and digits.
- Order of `All`: roots in registration order; within a record, base type first, then declaration
  order; a nested section's settings at the section's place; an override at its base property's
  place.

## Templates

- Under a wildcard root every setting is a template: `Path` holds `*` (`Feeds:*:Url`) and
  `IsTemplate` is `true`. `All` lists templates, never instances.
- `Find` matches a template and returns the instance with the concrete segment as given
  (`$APP__Feeds__News__Url` → `Feeds:News:Url`); `Instantiate(template, segment)` does the same
  for a segment the caller knows. Every member but `Name` and `Path` is the template's.
- A concrete segment is non-empty, is not `*`, holds no `:` and no `__`, and neither starts nor
  ends with `_`. `Instantiate` throws `ArgumentException` for a segment that breaks the rule and
  for a descriptor that is not one of the registry's templates; `Find` returns `null`.

## Names

- Name = the scope's prefix + the path with `:` written as `__` (`$APP__Mailer__Retry__Attempts`).
  `NameOf(scope, path)` spells it without checking that a setting exists.
- Names, prefixes and paths compare ignoring case (ordinal). `Find` returns the canonical name and
  path whatever casing it was asked for.
- `Find` returns `null` for a name of the wrong scope's prefix, with a `:`, an empty segment, or
  naming a section, an unmarked property or nothing at all.
- No prefix is blank and none starts with another, ignoring case: a name selects one scope.

## Defaults and read-only members

- `DefaultValue` is read from one instance of each root record, made by its public parameterless
  constructor (a struct: its default); a nested section's from the parent's property value.
- `null` when: no such constructor, a `null` section or value, a `required` member, a `[Protected]`
  setting, or a value not of `Type`. Otherwise the compiled-in text as it stands, not redacted (a
  `Uri` default keeps its userinfo).
- Invariant text: `true`/`false`; numbers with `CultureInfo.InvariantCulture`; `TimeSpan` in format
  `c` (`00:01:30`); `Uri.OriginalString`; the enum member's name; a list as compact JSON (`["*"]`,
  `System.Text.Json` default escaping).
- `SettingRoot.ReadOnly`: paths relative to the root (`Retry:Attempts`), ignoring case; those
  settings get `IsWritable` `false`. Every entry must name a setting of its root.

## What throws

Construction throws `InvalidOperationException` naming the type and property, root or prefix at
fault. Resolved through Autofac it arrives inside `DependencyResolutionException`s; peel them
with `UnwrapResolutionFailures`. The `IServiceProvider` facade (`GetRequiredService`) wraps those
once more in an `InvalidOperationException` carrying Autofac's text, which
`UnwrapResolutionFailures` does not peel. Construction throws for:

- a blank or overlapping prefix; an invalid root path; a root without a record type, or with an
  interface or open generic one; a root without settings;
- two settings whose paths can name the same key: equal ignoring case, or equal but for a
  wildcard (scopes are not considered); or one path running through the other, which makes one
  key both a setting and a section;
- a property name that is not ASCII letters and digits; an unsupported value type; an undefined
  scope; a blank description;
- `[Protected]` or `[Sink]` without `[Setting]`; any of them on a static, non-public, write-only or
  indexed property;
- a `ReadOnly` entry that names no setting of its root; a section that nests its own type;
- a constructor or getter that throws while defaults are read.

`Find(null)`, `NameOf(…, null)` and null `Instantiate` arguments throw `ArgumentNullException`;
`NameOf` with an undefined scope throws `ArgumentOutOfRangeException`.

## Value kinds and checks

`SettingValueExtensions` (pure extension methods on `SettingDescriptor`):

- `ValueKind()` → `SettingValueKind`: `Text` (`string`), `Choice` (an enum), `Boolean`, `Integer`
  (`int`, `long`), `Number` (`double`), `Duration` (`TimeSpan`), `Url` (`Uri`), `List`
  (`IReadOnlyList<string>`). Stable numeric values; a product maps them to its wire spelling.
- `CheckValue(value)` → `ValidationResult?`: `null` when the type can hold the text, read
  culture-invariantly as configuration binds it: `bool.TryParse`; `NumberStyles.Integer` within the
  type's range (no hex, no separators); `NumberStyles.Float` (`NaN`, `Infinity` pass);
  `TimeSpan.TryParse` invariant; a `Uri` absolute and starting with its own scheme (`/var/data`, an
  implicit file URI on Unix, and surrounding whitespace fail); an enum member's name, ignoring case
  (no numbers, no flag combinations); a list as a JSON array of strings or `null`s, the
  `DefaultValue` form. Every `DefaultValue` the registry produces passes.
- A failure reads `{Path} must be {form}.` with `MemberNames` `[Path]`, never the value (it may be a
  secret typed into the wrong field). Lengths, `U+0000`, ranges and cross-setting rules are the
  product's (`RequireStorable` in `Nordstein.Core.Common.Text` refuses what a text column cannot
  store).
- An unsupported `Type` (a descriptor built outside the registry) throws `InvalidOperationException`.

## Resolving templates against the configuration

`ISettingResolver` (registered per dependency; resolves only where the product registers its one
`ISettingCollections`) asks the configuration which elements exist:

- `ISettingCollections.KeysOf(collectionPath)`: the product's seam; the keys configured under the
  path in front of a template's wildcard (`Feeds` for `Feeds:*:Url`; `""` for a root `*`). Null, or a
  null key, throws `InvalidOperationException`.
- `Resolve(name)`: a concrete setting as `Find` returns it, never asking the configuration; a
  template's instance only when `KeysOf` holds its segment, ignoring case, then spelled as configured
  (`$APP__Feeds__news__Url` → `Feeds:News:Url`); otherwise `null`.
- `Expand(scopes)`: every concrete setting of the scopes in `All` order; a run of templates of one
  collection is listed once per key, all of one element's settings together. Only the requested
  scopes' collections are asked, once per run. Keys that break the segment rule are left out; keys
  equal ignoring case are listed once (first spelling). An undefined scope throws
  `ArgumentOutOfRangeException`.
- Check permissions between `Find` and `Resolve`: a caller without the permission for a scope then
  learns nothing about which elements are configured.

## Thread-safety

The registry is immutable after construction; every member is safe for concurrent callers.
Descriptors are immutable records. The value checks are pure; the resolver is stateless and as
thread-safe as the product's `ISettingCollections`.

## Related

- `Net/UriDisplayExtensions.ToDisplayString(uri)`: scheme, host, port and path only (no userinfo,
  query or fragment), `(relative)` for a relative URI. Use it for a URL-valued setting in a
  message, log line or audit record. It keeps the path: a secret in the path stays, and so does
  the whole of a URI without an authority (`data:`, `urn:`).
