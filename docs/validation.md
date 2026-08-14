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
Validation.InPast(CreatedAt, nameof(CreatedAt))
Validation.NotBefore(UpdatedAt, CreatedAt, nameof(UpdatedAt))
```

For referenced entities, cascade validation:

```csharp
foreach (var r in Account.Validate(validationContext)) yield return r;
```

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
