# CLAUDE.md — Nordstein.Core

## What this repository is — and why the bar is higher here

Nordstein.Core is the **shared foundation of every Nordstein product**. Proxytrace consumes it
today; every future product will consume it too. That changes the economics of quality:

- A bug here does not ship in one product — it ships in **all of them**, silently, at once.
- A sloppy public API is not a local wart — it is a contract consumers build on, and once a
  package version is restored somewhere it **cannot be recalled**. API mistakes here are forever.
- A missing test here is not "we'll catch it in QA" — Core has **no UI, no ops team, no user who
  will notice**. The test suite is the only thing standing between a regression and every product.

Therefore: **be a perfectionist in this repository.** Code that would be "good enough" in a
product is not good enough here. When in doubt between shipping fast and shipping right, ship
right. There is no deadline in Core that justifies debt.

## The Standard of Care (non-negotiable)

Every change to this repository — however small — meets all of the following before it is done:

1. **Rock-solid correctness.** Reason explicitly about edge cases: `null`, empty collections,
   cancellation mid-operation, concurrent callers, disposed lifetimes, clock skew, culture and
   time-zone sensitivity, overflow/precision. If a code path can be reached, it must behave
   deliberately — never "it probably won't happen".
2. **High test coverage, enforced by you.** Every public type and member gets tests; every branch
   of non-trivial logic gets a test; every bug fix starts with a failing regression test
   (test-driven — write the red test first, then the fix). A change that reduces effective
   coverage is incomplete. The whole suite runs in about half a minute — there is no excuse for
   an untested path. CI fails any shipped assembly below **90 % line or 90 % branch coverage**
   (`tools/coverage_gate.py`, floors in `tools/coverage-floors.json`). See
   [`docs/testing.md`](docs/testing.md).
3. **Adversarial review, every time.** Before declaring any change complete, run a genuine
   adversarial review pass — as the `reviewer` subagent
   ([`.claude/agents/reviewer.md`](.claude/agents/reviewer.md)) where subagents are available,
   otherwise as an explicit separate pass — whose stated goal is to **break the change**: hunt
   for race conditions, API misuse a consumer could stumble into, nullability holes, cancellation
   tokens not honored, behavioral changes to the existing public contract, and missing tests.
   Findings are fixed or explicitly justified in writing; "looks fine" is not a review result.
4. **The public surface is a contract.** `internal` by default; make something `public` only when
   a consumer needs it, and design it as if you can never change it again — because in practice
   you can't. Any change to public API shape or behavior must be called out explicitly in the
   PR/commit description, with the consumer migration story. Core versions independently of its
   consumers (`NordsteinCoreVersion` in [`Directory.Build.props`](Directory.Build.props)); see
   [`PUBLISHING.md`](PUBLISHING.md).
5. **XML documentation on every public member.** A package consumer has no source to read — the
   XML docs *are* the API documentation (`GenerateDocumentationFile` is on). Write them for
   someone who cannot see the implementation: contracts, thread-safety, what throws when.
6. **Verified, not assumed.** `dotnet build Nordstein.Core.sln` and `dotnet test Nordstein.Core.sln`
   pass before you claim done. Unlike a product repo, the suite here is fast (about half a
   minute) — **always run the full solution**, never a scoped subset, and state that you did.

## The One Rule

**Core may not reference or know about any product. Ever.** No traces, projects, tiers,
or any other product concept — not in code, not in names, not in doc examples that would only make
sense for one product. The *generic AI vocabulary* — the agent contract, messages, tools,
prompts, completions, the model-client contract — is Core territory and lives exclusively in
`Nordstein.Core.AI` (see [`docs/ai.md`](docs/ai.md)); what stays banned is anything that ties an
agent to one product: tenancy, version history, endpoints/providers/pricing, traces, evaluators,
ingestion, search. The dependency arrow points one way: products depend on Core. When
something genuinely belongs on both sides, Core declares an **interface (a seam)** and the product
implements it — never a reference, never a type that "temporarily" knows a product detail.

This property is enforced mechanically (Core builds and tests standalone; the consuming product's
CI also packs Core and rebuilds against the `.nupkg`s), but the enforcement only catches
references — **you** must catch leaked concepts.

## Repository map

| Path | Holds |
|------|-------|
| `Nordstein.Core.<Package>/` | One of the seven shipped packages: Common, Domain, Storage, Licensing, AI, Workflows, Testing. Each has its own package `README.md` and a sibling `Nordstein.Core.<Package>.Tests/` project. Layering and dependency rules: [`docs/architecture.md`](docs/architecture.md) |
| [`docs/`](docs/README.md) | One page per area (index below) and the decision records in [`docs/adr/`](docs/adr/) |
| [`tools/`](tools/) | The CI checks, Python standard library only: `coverage_gate.py` with its settings and floors, `docs_check.py`, and in `tools/tests/` the tests of all of it and of `.claude/hooks/` |
| [`.claude/`](.claude/) | `agents/reviewer.md` (the adversarial reviewer), `hooks/guards.py` (the edit guard, see Hard Rules), `skills/csharp-coding` (the C# style skill) |
| [`.github/`](.github/) | `workflows/ci.yml` (secret scan, build, tests with the coverage gate, tooling tests and docs check, workflow lint, pack), `workflows/codeql.yml`, `dependabot.yml` |

This is the only `CLAUDE.md` in the repository; guidance for one area lives in its `docs/` page.

## AI Assistant Docs

Detailed guidance lives in [`docs/`](docs/). Read the relevant page **before** working in that
area — do not rely on this file alone:

| Doc | Read before… |
|-----|--------------|
| [`docs/architecture.md`](docs/architecture.md) | Touching package structure, layering, Autofac modules/discovery, or hosting helpers |
| [`docs/code-style.md`](docs/code-style.md) | Writing any C# — style rules + key conventions |
| [`docs/cryptography.md`](docs/cryptography.md) | Touching `Common/Cryptography` or `Common/Security` — AEAD, key wrap, password hashing, CSPRNG rule |
| [`docs/security.md`](docs/security.md) | Touching the credential primitives or the master key ring in `Common/Security` — login lockout, secret token format, password policy, dummy verify, password generator, key file custody, canary, fail-closed load |
| [`docs/io.md`](docs/io.md) | Touching `Common/Io` — durable publishing, secret-file custody |
| [`docs/events.md`](docs/events.md) | Touching `Nordstein.Core.Common.Events` — the in-process event hub, its filters, bounded replay and slow-subscriber policy |
| [`docs/diagnostics.md`](docs/diagnostics.md) | Touching `Nordstein.Core.Common.Diagnostics` — the request-timing accumulator and its names; its EF Core feed is in `docs/storage.md` |
| [`docs/values.md`](docs/values.md) | Touching `CulturalValueParser` or `CanonicalJson` in `Nordstein.Core.Common`, or parsing culture-formatted values or hashing JSON anywhere |
| [`docs/tabular.md`](docs/tabular.md) | Touching `Nordstein.Core.Common.Tabular` — CSV reading, dialect detection, the RFC 4180 row writer, spreadsheet number formats |
| [`docs/text.md`](docs/text.md) | Touching `Nordstein.Core.Common.Text` — the text normalizer and its offset maps, structure markers, the chunker and the Markdown renderer, quote folding and exact-once matching, storable text, the strict UTF-8 encoding |
| [`docs/settings.md`](docs/settings.md) | Touching `Nordstein.Core.Common.Settings` — setting attributes, the registry, naming, value checks, template resolution |
| [`docs/domain.md`](docs/domain.md) | Touching the domain foundation (`Nordstein.Core.Domain`) — entity/object contracts, factory delegates, generators, repositories, archiving |
| [`docs/storage.md`](docs/storage.md) | Touching `Nordstein.Core.Storage` — `NordsteinDbContext`, generic repositories, the transaction seam, the cache |
| [`docs/validation.md`](docs/validation.md) | Touching validation helpers, or adding validation to anything |
| [`docs/licensing.md`](docs/licensing.md) | Touching `Nordstein.Core.Licensing` — the generic license engine and its product seams |
| [`docs/ai.md`](docs/ai.md) | Touching `Nordstein.Core.AI` — the AI/agent foundation (messages, tools, prompts, completions, the agent/model-client contracts, the agent tool-call loop, output parsing) and its product seams |
| [`docs/workflows.md`](docs/workflows.md) | Touching `Nordstein.Core.Workflows` — the workflow kernel: graph, node contract, data references, validator, interpreter, step journal, suspend and resume |
| [`docs/dependencies.md`](docs/dependencies.md) | Adding a package reference or bumping one's major version, Dependabot, or a CI action — the package bar, the update cooldown, actions pinned by commit SHA |
| [`docs/adr/`](docs/adr/) | Making or revisiting an architecture decision — one numbered record per decision: context, decision, alternatives, consequences |
| [`docs/testing.md`](docs/testing.md) | Writing or modifying **any** test, or touching `Nordstein.Core.Testing` (the harness itself lives here) |
| [`docs/product-playbook.md`](docs/product-playbook.md) | Advising on or building anything in a **consuming product** — e2e testing, user manual, i18n, CI, perf budgets: the practices proven in Proxytrace |
| [`PUBLISHING.md`](PUBLISHING.md) | Touching packaging, versioning, or anything publish-related |

## Hard Rules (apply everywhere)

- **`TreatWarningsAsErrors=true`** solution-wide. Leave no warnings of any kind.
- **Nullable suppression with `!` is strictly forbidden.** There is exactly **one** sanctioned
  exception in the entire ecosystem, and it lives in this repository:
  [`Nordstein.Core.Common/Validation/Validation.cs`](Nordstein.Core.Common/Validation/Validation.cs)
  (`Validation.Success`) — the BCL defines validation success as a `null` `ValidationResult`
  behind a non-nullable signature we cannot change. It is documented in place. Do not add a
  second exemption; return `Validation.Success` instead.
- **No unexplained suppression.** `#pragma warning disable`, `#nullable disable`,
  `[SuppressMessage]` and `[ExcludeFromCodeCoverage]` need a `// WHY:` comment on their line or
  directly above; fix the cause instead wherever that is possible. The edit guard
  ([`.claude/hooks/guards.py`](.claude/hooks/guards.py)) denies an edit that adds one without it,
  or that adds a `!`; it catches accidents, review and CI remain the backstop.
- **Every `internal` type of a shipped package lives in an `Internal/` folder** with the matching
  `.Internal` namespace; a concept folder's root holds only its public surface.
  `InternalNamespaceFenceTests` fails the suite otherwise
  ([`docs/testing.md`](docs/testing.md#cores-own-fence-internal-types-live-in-internal)).
- **Keep the docs current.** When a change alters anything a `docs/` page (or this file, or a
  package `README.md`) describes, update the doc **in the same change** and add an index row for
  any new page. A change is not complete until its docs match the code. The root
  [`README.md`](README.md) is the one-screen entry for someone new: hero, quick start, the
  package table, develop, license, about 80 lines. Detail goes to `docs/` and is linked; a new
  package gets its table row, and the demo GIF (`docs/assets/demo.gif`) shows real output.
- **No product knowledge** (see The One Rule). Doc and test examples use neutral placeholder
  domains, never a real product's.
- **Every dependency is a liability.** Core's package references become every product's
  transitive dependencies. Adding one requires explicit justification; prefer the BCL. Provider-
  and framework-specific packages (EF providers, ASP.NET, JSON libraries beyond the BCL) stay in
  the products. A new package reference, or a major-version bump of one, meets the package bar in
  [`docs/dependencies.md`](docs/dependencies.md) or is not taken.
- **Breaking changes are announced, never smuggled.** If a test had to change to keep passing,
  the behavior changed — say so and treat it as a contract change, not a test fix.
- **File issues for stumbles.** When you hit an out-of-scope bug or debt, capture it as a GitHub
  issue on this repository rather than silently working around it.

## Team Composition

Implement each task using a team of experts. In this repository the **Reviewer is never
optional**: every non-trivial change gets an adversarial review pass (see Standard of Care #3).

- **Architect**: guards the package layering, the one rule, and API design ("can we never change
  this again and be happy?").
- **Engineer**: implements with the perfectionism this repo demands.
- **Tester**: drives the coverage requirement — enumerates the edge-case matrix before signing off.
- **Reviewer (mandatory)**: adversarial — actively tries to break the change and the API around it.
- **Documenter**: XML docs, `docs/` pages, package READMEs.

Spawn them as subagents when scope warrants; for small changes, perform each role as an explicit
separate pass rather than skipping it.

## Building & Testing

```bash
dotnet build Nordstein.Core.sln    # must be warning-free
dotnet test  Nordstein.Core.sln    # always the full suite — it is fast
```

CI also gates on coverage, the tooling tests and the docs check; run the ones your change can
affect before you push
([`docs/testing.md`](docs/testing.md#the-coverage-gate-ci)):

```bash
# Coverage: every shipped assembly at or above 90 % line and 90 % branch
dotnet test Nordstein.Core.sln --settings tools/coverage.runsettings --collect "XPlat Code Coverage" --results-directory artifacts/coverage
python3 tools/coverage_gate.py --reports artifacts/coverage --floors tools/coverage-floors.json

python3 -m unittest discover --start-directory tools/tests    # the tooling's own tests
python3 tools/docs_check.py --root .                           # links resolve, every docs/ page is indexed
```

Core builds and tests **standalone** — that property is the extraction guarantee and must never
regress. When mounted as a submodule inside a consuming product, the product's CI additionally
builds this solution standalone and rebuilds the product against packed `.nupkg`s
(package mode) — a consumer-facing API break will surface there even if Core's own suite is green.
