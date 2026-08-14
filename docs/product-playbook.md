# Product Playbook — practices proven in the consuming products

Nordstein.Core carries the *code* every product shares; this page carries the *practices*. They
were established (and repeatedly debugged) in Proxytrace, the first consumer — a new Nordstein
product should adopt them from day one rather than rediscovering the failure modes that produced
them. Nothing here is Core API; it is how a product around Core is built, tested, documented, and
shipped.

## Documentation is part of every change

- **AI/developer docs (`docs/`) are treated like tests.** Each area has a page; the repo's
  `CLAUDE.md` holds an index table mapping "read this page before touching that area". When a
  change alters anything a page describes, the page is updated **in the same change** — a change
  is not complete until its docs match the code.
- **A user & operator manual ships with the product** — a VitePress project (`manual/`, markdown
  source, built to searchable static HTML, served by the app itself at `/docs`). A user-facing
  change is not complete until `manual/guide/` (end users) or `manual/admin/` (operators)
  matches; new top-level features get a page wired into the VitePress config. Verify with
  `npm run docs:build`; CI builds the manual on manual-only diffs too, because every other job is
  path-gated away from `manual/**` and a broken build would otherwise surface only at release
  packaging.
- **Screenshots by default.** Most user-guide pages benefit from them. Proxytrace automates this:
  a self-seeded, login-free **kiosk** Docker stack plus a Playwright capture script embeds
  reproducible screenshots into the manual — pages needing a real login or a second user stay
  text-only. Budget for the same automation early; hand-taken screenshots rot.
- **A `CHANGELOG.md` in Keep-a-Changelog format**, updated in the same change as every
  user-facing edit; the `[Unreleased]` section becomes the GitHub release notes verbatim at tag
  time.

## Testing strategy (the pyramid, and running only what can break)

- **Unit/integration tests** ride on `Nordstein.Core.Testing` — per-test DI containers, no shared
  state, real in-memory storage, NSubstitute for infrastructure. See [`testing.md`](testing.md).
- **Run only the affected tests locally.** A product suite grows to thousands of tests; CI runs
  the full suite on every push, so a local full run buys nothing. Map "what changed" → "which
  test project" in the repo's CLAUDE.md (layer → `<Layer>.Tests`; an entity or mapping → domain
  **and** storage tests; narrow further with `--filter "FullyQualifiedName~<Name>"`). Always
  report *which scope* ran — a scoped green run is never "all tests pass". Note the frontend
  suite (Vitest) is usually cheap enough that a full run is the final check anyway.
- **Core's tests are separate**: `dotnet test <ProductSln>` does not run
  `Nordstein.Core.sln` — a cross-cutting change needs both.

## End-to-end tests (Playwright over the real stack)

The e2e suite lives at the repo root (`e2e/`) and exercises complete user journeys across every
process boundary — browser → API → database, and any side channels (proxy → queue → worker → UI):

- **The stack under test is the real one**: `docker-compose.e2e.yml` boots the same images/
  compose topology that ships, not a test double of it. **Docker is a hard prerequisite** —
  check `docker info` first and *say* the suite was skipped rather than attempting it without.
- e2e is a **flow test, not a routine check**: it takes minutes and boots a stack, so it runs on
  changes to the flow (and in CI), never after every edit.
- The product API gets a small typed client for test setup (Proxytrace: `ProxytraceApiClient`)
  and the UI gets stable `data-testid` hooks — specs never scrape markup.
- **Triage must survive the stack dying.** On CI failure, upload the Playwright report *and* the
  per-service stack logs, container states and `docker inspect` output, captured **before**
  teardown — and also echo each service's last ~200 log lines into the job log itself (collapsed
  groups), because the artifact is not always reachable from where triage happens. A container
  that "vanished mid-run" is undebuggable without this.
- Related lesson baked into Core: a crashed `BackgroundService` must not take the host down with
  exit code 0 — that is exactly how a healthy e2e container disappears. Use
  `AddResilientBackgroundServices()`; see [`architecture.md`](architecture.md).

## Internationalization

The UI is multilingual from the start (English as source). Every user-facing string goes through
the i18n macro layer (Proxytrace: Lingui — `<Trans>`, the `t` template literal, `Plural`,
`msg`), never a hardcoded literal; glossary/technical terms stay English. Extraction and machine translation are
scripted (`i18n:extract` / `i18n:translate`) and the generated catalogs are committed with the
change that adds the strings. Per-user language is a validated field on the user entity.

## Frontend discipline

- The design system and code-architecture rules live in checked-in docs
  (`frontend/docs/DESIGN.md`, `BEST_PRACTICES.md`) that are **mandatory reading before frontend
  work** — and they override tool defaults.
- Raw HTML controls (`<button>`, `<input>`, …) are **ESLint-blocked**; everything renders through
  the product's UI primitive components. Lint-enforced conventions beat review-enforced ones.

## Performance at scale

Correctness tests on a few in-memory rows cannot catch client-side evaluation, bad query plans,
or O(rows) blow-ups. Any product with an unboundedly-growing table keeps an opt-in perf suite
(`perf/`) that seeds realistic volume (Proxytrace: ~1M rows) into the real database and measures
the hot paths against **absolute budgets** in a checked-in `perf-budgets.json`:

- Touching a query, repository, EF mapping, or index on a high-volume entity **requires** adding
  or extending a perf test for the changed path in the same change.
- Sanity-check queries with `ToQueryString()` (is the work happening server-side?) and
  `EXPLAIN (ANALYZE)` (estimated vs actual rows).
- Budgets are recalibrated to real measured p95 so the suite runs all-green — any red is a
  regression to chase, not noise to shrug at. The suite is manual-trigger only in CI; it never
  gates a push.

## CI shape

Patterns that transfer to any product repo:

- **Path-gated jobs, computed by one composite action** that must **fail open**: when the diff
  range cannot be resolved (tag push, new branch) every area reports "changed", and a change
  under `.github/` forces everything — a commit must not be able to rewrite the gates and skip
  its own verification.
- **e2e is barely gated on purpose** — only a purely-prose diff skips it; wrongly skipping it is
  far more expensive than running it.
- **The release path re-runs everything**: reusable workflows take a `full: true` input rather
  than sniffing `github.event_name` (inside a reusable workflow that reports the *caller's*
  event, which silently skips jobs during a tag push).
- **Build Core standalone, then rebuild the product against packed Core.** The product CI builds
  `Nordstein.Core.sln` on its own (the moment Core only compiles inside the product solution, it
  is no longer extractable) and a `core-package` job packs Core and rebuilds the product with
  `-p:UseLocalCore=false` — catching types missing from the package surface and undeclared
  dependencies that source mode resolves through the product's graph.
- **Container-backed tests skip without Docker locally but must not skip silently in CI** — CI
  sets the require-Docker flag so lost coverage is a red job, not a quiet gap.
- **Keep Docker layer caches out of the Actions cache.** The Actions cache (10 GB, LRU) is for
  package restores only; Docker layer caching goes to a registry ref with `ignore-error=true` so
  fork PRs degrade to slow builds instead of failures. Clean up a PR's caches when it closes.

## Releasing

Releases are tag-triggered (`v*.*.*`): the workflow re-runs the full gate set, rolls the
changelog into the release notes, publishes images/artifacts, and the product ships an update
check against the published releases. Cutting a release is scripted/skill-driven end-to-end so
"is master releasable?" always has a mechanical answer.

## Working habits that keep a repo healthy

- **File issues for stumbles**: an out-of-scope bug, debt, or doc/code contradiction found
  mid-task becomes a well-formed GitHub issue immediately — not a silent workaround, not a mental
  note.
- **Warnings are errors everywhere**, and the no-`!` nullable rule from
  [`code-style.md`](code-style.md) applies to products exactly as to Core.
- **Prompts are code that a diff cannot review.** Where a product embeds LLM agents, changes to
  system prompts/tool descriptions are verified by firing scenarios at the live model and A/B-ing
  against the committed version — never by eyeballing the diff.
