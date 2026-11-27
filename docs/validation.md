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
```

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

Every check sets `MemberNames` to the member it was given, including the integer `InRange` and
`GreaterThan`, `Positive(TimeSpan)` and `True`.

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
