# Dependencies

Every package Core references becomes a transitive dependency of every Nordstein product, and a
package version restored by a consumer cannot be recalled. So Core's bar for a dependency is
stricter than a product's: the default answer is the BCL. This page is the bar, and how updates
and CI actions are kept current and pinned.

## The package bar

It applies to a new direct `PackageReference` in any Core project and to a major-version bump of
an existing one. A transitive dependency is judged with its parent. A package that does not pass
is not taken; a part of a change that would need it is left out and raised with the owner.

A package in a **shipped project** (one that packs to `Nordstein.Core.*`) needs every one of:

1. **A need the BCL cannot meet.** The commit message names what the BCL lacks, which consumer
   needs it, and what it pulls in transitively. "It is more convenient" is not a need.
2. **No product or provider coupling.** EF providers, ASP.NET, model-provider SDKs and JSON
   libraries beyond `System.Text.Json` stay in the products (`CLAUDE.md`, Hard Rules). Core may
   reference a framework's abstractions or a standalone library of it where a seam needs it
   (`Microsoft.EntityFrameworkCore.Relational`, `Microsoft.AspNetCore.DataProtection`), judged by
   what it pulls in, never the framework's host or a provider. An opt-in provider package
   (`Nordstein.Core.<Area>.<Provider>`,
   [ADR 0007](adr/0007-opt-in-provider-packages-and-vector-search.md)) may take one provider
   dependency; today only `Nordstein.Core.Testing.Postgres` does (`Testcontainers.PostgreSql`).
3. **A permissive OSI-approved licence** (MIT, Apache-2.0, BSD) that lets every product ship it.
4. **A maintainer that will be there**: Microsoft, the .NET Foundation or another major foundation,
   or a project with at least 1,000 GitHub stars and more than one active maintainer.
5. **A release in the last 12 months**, a stable version (no prerelease), and support for Core's
   target framework.
6. **The owner's approval**, because the dependency reaches every product.

A package in a **test project** (`*.Tests`) never ships, so it needs 3, 4 and 5. Core's test
stack is MSTest, AwesomeAssertions, NSubstitute and coverlet (and the EF Core in-memory provider
for storage tests, and Npgsql for the tests of the PostgreSQL packages); a second library for the same job (another mocking or assertion library) is
not taken.

## Updates

- `.github/dependabot.yml` proposes NuGet and GitHub Actions updates weekly: minor and patch grouped
  into one pull request per ecosystem, each major in its own.
- **Cooldown:** a version is proposed 7 days after its release at the earliest (`cooldown`,
  `default-days: 7`), so a broken or compromised release is usually withdrawn before it reaches
  Core. Security updates are not delayed.
- A rejected update is an `ignore:` entry in `dependabot.yml` stating why and what must happen before
  it can go, never an `@dependabot ignore` comment (which does not stick on a grouped pull request).
- `tools/tests/test_dependency_policy.py` fails when an ecosystem has no 7-day cooldown.

## CI actions and tools

- Every workflow `uses:` names its action by full commit SHA with the version as a comment
  (`uses: actions/checkout@<40-hex sha> # v7.0.1`); Dependabot updates both. A tag can be moved to
  other code, a commit cannot. A new `uses:` follows the same form, its SHA taken from the action's
  own tag (`git ls-remote https://github.com/<owner>/<repo> refs/tags/<version>`), never guessed.
- Still on a tag, until a verified SHA is pinned: `github/codeql-action/init@v4` and
  `github/codeql-action/analyze@v4`. `tools/tests/test_dependency_policy.py` lists them and fails on
  any other unpinned `uses:`.
- Binaries CI downloads (gitleaks, actionlint) are pinned by version in the URL; Dependabot does not
  track them, so their version is bumped by hand.
- CI's `actionlint` job lints every workflow, the shell of its `run:` steps included.
