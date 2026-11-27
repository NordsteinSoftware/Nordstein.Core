# Domain Validation

Domain entities are validated by Autofac on activation (`OnActivated` runs
`Validator.ValidateObject`) and again before repository `Add`/`Update` — an invalid entity can
neither be constructed through DI nor persisted. Entities override `Validate(ValidationContext)`
and yield `base.Validate(...)` first, using the helpers from
[`Nordstein.Core.Common/Validation/Validation.cs`](../Nordstein.Core.Common/Validation/Validation.cs):

```csharp
Validation.NotNullOrWhiteSpace(Name, nameof(Name))   // note: capital S in "WhiteSpace"
Validation.NotNull(Account, nameof(Account))
Validation.NotDefault(SomeGuid, nameof(SomeGuid))
Validation.NotNullOrDefault(OptionalId, nameof(OptionalId))   // Guid?: null and Guid.Empty both fail
Validation.Empty(GroupIds, nameof(GroupIds))                 // a collection that must have no element
Validation.NoneDefault(GroupIds, nameof(GroupIds))           // no element equals default(T)
Validation.InPast(CreatedAt, nameof(CreatedAt))
Validation.NotBefore(UpdatedAt, CreatedAt, nameof(UpdatedAt))
Validation.HttpsBaseUrl(ApiUrl, nameof(ApiUrl))   // absolute https, no user information, query or fragment
```

`HttpsBaseUrl` is the rule for an options record's remote base URL (paths are appended to it):
`null`, a relative URL, any other scheme, user information, a query or a fragment (an empty `?` or
`#` too) fail with `{member} must be an absolute https URL without user information, query or
fragment`; the message never contains the URL.

For referenced entities, cascade validation:

```csharp
foreach (var r in Account.Validate(validationContext)) yield return r;
```

A nested value or options object that is not an entity is validated with `Validation.Nested`, which
reports its failures under the parent member:

```csharp
foreach (ValidationResult failure in Validation.Nested(Chunking))   // member name from the argument expression
    yield return failure;
```

A failure `MaxChunkChars must be greater than 0` of the child surfaces as the message
`Chunking:MaxChunkChars must be greater than 0` with `MemberNames` `["Chunking:MaxChunkChars"]`; a
failure that names no member gets `["Chunking"]`. The prefix composes through further levels
(`Outer:Chunking:MaxChunkChars`). A `null` child yields nothing: required-ness is a separate
`Validation.NotNull` rule.

## Member names

Every failure from a `Validation` helper names the member it checked in
`ValidationResult.MemberNames` (exactly one entry: the `memberName` argument, or the argument's
source expression when omitted, so pass `nameof(...)` for a clean name). Consumers map an error
to a form field from there; `Validator.ValidateObject` throws a `ValidationException` whose
`ValidationResult` carries the first failure. An entity-wide rule (one no single member owns)
names no member:

```csharp
if (Cancelled && Shipped)
{
    yield return new ValidationResult("A cancelled order cannot be shipped");   // MemberNames empty
}
```

A hand-written member rule passes the names: `new ValidationResult("...", [nameof(Quantity)])`.

## Writing a new check

A check returns a `ValidationResult` describing the failure, or `Validation.Success` when the
value is fine — **never** `ValidationResult.Success` directly.

That indirection is deliberate. The BCL represents success as a **null** `ValidationResult`, but
declares `IValidatableObject.Validate` to return a non-nullable element type — so every check has
to hand back a value the framework itself defines as null, through a signature we cannot change.
The resulting suppression is concentrated on the single `Validation.Success` member: **the one
sanctioned `!` in the entire Nordstein ecosystem**, documented in place. Returning any genuinely
non-null "success" sentinel instead would be read by `Validator` as a validation *failure*. Do
not add a second exemption anywhere — return `Validation.Success`.

For a value constrained to a closed set that is not an enum, validate membership explicitly:

```csharp
yield return Validation.NotNullOrWhiteSpace(Language);
if (!SupportedLanguages.IsSupported(Language))
    yield return new ValidationResult($"Language '{Language}' is not supported.", [nameof(Language)]);
```

## Bar for new helpers

A helper added here becomes part of every product's validation vocabulary. It must be:
generic (no product concepts), null-safe by construction, covered by tests for the pass case,
the fail case, and every boundary (empty vs whitespace, `default` vs zero, exact-equal
timestamps), and documented with XML docs stating exactly what passes.
