---
name: csharp-coding
description: >-
  You must use this skill whenever writing c# code.
---

# Backend C# Coding

The normative source is [`csharp_code_style.md`](./csharp_code_style.md).
**Read it before writing or reviewing any backend C#.** 

If `csharp_code_style.md` does not exist in the repo, stop and ask the user for it — do not invent
style rules or fall back on generic .NET advice.

When an explicit user instruction conflicts with a rule, follow the instruction for that change but
say which rule is being deviated from and why — silent drift is how a style guide dies.

If you think something is missing from the guide, and should be added, ask the user. This guide is steadily evolving.

## Workflow

1. **Orient.** Decide which layer the code belongs to (Domain / Application / Infrastructure /
   Storage / host) and what already exists. Extend an existing pattern instead of inventing a new
   one — but treat existing violations as debt, not precedent (see *Review mode*).
2. **Read the guide.** At minimum re-read the sections that apply: §3 layering, §4 interfaces and
   records, §5 DI, §6 immutability/statelessness, §7 async and cancellation, §9 expression syntax,
   §16 forbidden list, §17 review checklist.
3. **Write to the guide.** Match surrounding naming, file placement, and idioms — the guide defines
   the floor, not a reason to reformat untouched code.
4. **Self-check.** Walk §17 before claiming done; run the mechanical scan below over exactly the
   files you touched.
5. **Verify.** `dotnet build` must be clean (warnings are errors). Run the affected test
   project(s), scoped — not the whole suite.

## Non-negotiables (if you remember nothing else)

- **Public API is interfaces plus records.** `IThing` in public, `Thing` implementation
  `internal sealed`. POCOs/DTOs/events are concrete records — never give data an interface.
- **DI everywhere.** Constructor injection of interfaces only; no service locator, no
  `IServiceProvider` injection, no `new` on services, no static mutable state.
- **Immutability and statelessness first.** Records with `init`/`required`, `with` for changes,
  `IReadOnly*` out the door, defensive copies in. State belongs in storage or an explicitly
  thread-safe store; a stateful service needs a lifetime justification.
- **Async all the way.** No `.Result`, `.Wait()`, `GetAwaiter().GetResult()`, no `async void`.
  `Async` suffix outside controllers. `CancellationToken` is the last parameter, named
  `cancellationToken`, `= default` on interfaces/domain, required in controllers — and it must
  actually flow into every I/O call. Let cancellation propagate; never log it as an error.
- **Expression syntax by default.** Expression bodies for one-liners, `switch` expressions and
  patterns over `if` chains, target-typed `new()`, collection expressions, method-syntax LINQ,
  `is not null` (`!= null` only inside expression trees).
- **No `!` null-forgiving, anywhere.** Fix the flow with guards, `required`, `?? throw`, or
  `is not null`. There is one documented BCL edge case in Core; do not add a second.
- **No primary constructors in product code**; private fields are camelCase with no `_`, assigned
  with `this.` in the constructor. (Positional records are different and fine.)
- **Structured logging only** — templates, never interpolation; no secrets/PII; custom exceptions
  live next to their feature and are mapped to transport errors in one central place; never
  swallow an exception.
- **`DateTimeOffset` for every timestamp.** `DateTime` is a bug report waiting to happen.
- **Document public members** in the 3-line `<summary>` form; comments explain *why*, and cite the
  issue or docs page for non-obvious decisions.

## Mechanical pre-flight scan

The compiler only catches part of the guide. Before handing off, scan the changed `.cs` files for
the mechanically detectable violations. Every hit is a question, not a verdict — strings and
comments can false-positive — but each one needs an answer.

```bash
git diff --name-only --diff-filter=ACMR -- '*.cs' | xargs -r rg -n \
  -e 'async\s+void' \
  -e '\.(Result|Wait)\(' \
  -e 'GetAwaiter\(\)\.GetResult\(\)' \
  -e 'Log(Information|Warning|Error|Debug|Trace|Critical)\(\s*\$"' \
  -e 'catch\s*\{\s*\}' \
  -e 'private\s+.*\s_[a-z][A-Za-z0-9_]*\s*[;=]' \
  -e '\bIServiceProvider\b' \
  -e '\bDateTime\b'
```

Null-forgiving on added lines needs a diff because a bare `!` search is unusable:

```bash
git diff -U0 -- '*.cs' | rg '^\+' | rg '[A-Za-z0-9_\)\]]!(\.|\s|,|;|\)|$)'
```

Notes: `IServiceProvider` is legitimate in the composition root and the two documented framework
seams — check the context. `DateTime` legitimately appears in migrations and a few
conversion/serialization spots; `DateTimeOffset` does not match the pattern. A clean scan means
"nothing obvious", not "conforms" — the review checklist is still owed.

## Review mode

When asked to review a diff or PR, or when self-reviewing before hand-off:

- Evaluate against `csharp_code_style.md`, not against "what the rest of the repo does". A
  violation that predates this change is not a licence to repeat it; call it out as debt (and file
  an issue via the `file-issue` skill if it is out of scope) rather than copying it.
- Report findings as `file:line — rule (guide §N) — why it matters — suggested fix`. Rank them:
  **blocking** (forbidden list, nullability, DI/lifetime bugs, missing cancellation, swallowed
  errors) versus **nit** (naming, expression style, docs).
- Distinguish "the compiler will catch this" from "only review will". `TreatWarningsAsErrors` is
  on, so anything that builds is already warning-free — the remaining risks are structural:
  lifetimes, statelessness, cancellation flow, leaked entities, logging shape.
- For new code, verify tests exist at the right layer (then the `test` skill governs their shape).

## Common failure modes to look for

- A `CancellationToken` parameter that is accepted but not passed to one I/O call in the chain, or
  a new endpoint/action without a token at all.
- A singleton that captures a scoped `DbContext`, repository, `HttpContext`, or current-user
  accessor — or a service registered `SingleInstance` "because it is stateless" while holding a
  cache that is not thread-safe.
- Controllers growing business logic, or returning domain/storage entities instead of DTO records.
- `!` added to silence a nullable warning instead of restructuring the flow.
- `DateTimeOffset.UtcNow`/`Random.Shared` used directly where an injected clock/random seam exists
  (and where it does not, consider introducing one).
- `Task.Run` or fire-and-forget in a request path; concurrent operations on one `DbContext`.
- Interpolated log messages (`logger.LogInformation($"...")`), or logging a cancellation as error.
- New public surface that is not an interface or record; implementation classes left unsealed.
- `lock`/`SemaphoreSlim` held across `await` instead of the shared async lock abstraction.