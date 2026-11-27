# ADR 0001: Port generic code from nordstein-dms into Core

- Status: proposed
- Date: 2026-11-27
- Issue: [#54](https://github.com/NordsteinSoftware/Nordstein.Core/issues/54)

## Context

nordstein-dms may be discontinued. Its `core/` folder is already back in Core (#53). Epic #54
lists more code there that is generic: test fakes, text tools, credential and key primitives,
workflow nodes, CI and docs tooling. It sorts each item as take, owner decision, deferred or
rejected. All source paths refer to `NordsteinSoftware/nordstein-dms` at `cf146a85`.

Three questions must be settled before anyone ports code:

1. How code moves: history, names, visibility, tests.
2. Where each item lands, within the package graph in `docs/architecture.md`.
3. What happens to the items that break a written Core rule (`CLAUDE.md`: provider- and
   framework-specific packages stay in products; `docs/ai.md`: no provider implementations).

## Decision

1. **Port, do not move.** Each item is one Core PR that copies the code from `cf146a85` and
   adapts it. No git history is carried over and nordstein-dms is not changed (it is switched
   off for agents; it keeps its own copy until it is retired). The PR names the source path and
   commit in its description.
2. **The Standard of Care applies as if the code were new.** Tests come first: port the dms
   tests, then add direct tests for every public member and every branch the dms tests only
   reached through a use case. Types are `internal` by default and internal types live in an
   `Internal/` folder. Every public member gets XML docs. Every product word (document, mail,
   kiosk, tenant, invoice) is removed from names, messages and doc examples.
3. **Existing packages only, for the take list.** No new package and no new external
   dependency comes with it. Placement follows the epic, with one change: the EF command
   interceptor of the request-timing item goes to `Nordstein.Core.Storage` (EF Core Relational),
   and only the accumulator goes to `Nordstein.Core.Common`. Common does not reference EF.
4. **Reuse before port.** Where Core already has the primitive (`OffsetMappedText`,
   `IDirectorySync`, `ISecretFileLoader`, `IClock`, `Validation`), the port builds on it and
   removes the dms copy of it. The shared fakes replace Core's own copies (`MutableClock` in
   the Domain, Storage and Licensing test projects, and the stub HTTP handler) in the same PR.
5. **Order.** The shared test fakes go first, because later ports use them in their tests.
   After that, items are independent and any order works. A task that needs another names it
   under "Depends on".
6. **Items marked `architect` in the epic need their own design.** These are the fence kit,
   the chunker with the Markdown renderer, the event hub, and the workflow graph JSON reader.
   Each one adds a public contract or a wire shape that cannot be recalled. Each gets its own
   `needs-design` issue and ADR before code.
7. **Rule-changing items wait for Daniel.** The four "recommend yes" items (`Core.Testing.Postgres`,
   `Core.Storage.Postgres`, the HTTP-only wire clients in Core.AI, `Core.AI.Testing`) need a
   written rule first. Proposed rule for `CLAUDE.md`: *a provider-specific package is allowed
   when it is opt-in, has its own project named `Nordstein.Core.<Area>.<Provider>`, no other
   Core package references it, and it adds at most one provider dependency.* Until Daniel
   approves the rule, no task is written for these items. `Core.AspNetCore` stays out for now.
8. **Deferred and rejected items get no task.** The epic's reasons stand. Revisit a deferred
   item only when a product needs it.

## Alternatives considered

- **Move with history (`git filter-repo` or subtree).** This keeps blame, but brings dms names,
  layout and product words into Core history. The code changes a lot in the port anyway.
  Rejected.
- **One large port PR per package.** Fewer PRs, but a reviewer cannot hold a public-API review
  of several thousand lines. A mistake in Core cannot be recalled. Rejected.
- **New packages for everything (`Core.Text`, `Core.Security`).** This gives cleaner
  boundaries, but every product would have to add new packages for small helpers, and the
  package graph would grow without need. Rejected; Common already holds `Text/` and
  `Security/`.
- **Leave the code in dms.** If dms is retired, the code is lost and the next product writes
  it again. Rejected for the take list.

## Consequences

- About 17 independent PRs, each small enough for one review. Core's public surface grows in
  Common, Testing, Workflows and AI. Each PR calls out its API additions as `CLAUDE.md`
  requires.
- The four design items and the rule decision block a part of the epic. The other items do
  not wait for them.
- Credential primitives and the master key ring are security code. Their PRs follow
  `docs/cryptography.md`, and the review checks timing safety and fail-closed paths.
- The coverage gate adds the first Python to this repository. Its PR documents how to run it
  locally.
- After the port, nordstein-dms and Core hold two copies of the same code. That is accepted
  until dms is retired or moves to the Core packages itself.
