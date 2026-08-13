# Publishing Nordstein.Core

Nothing is published yet. This file records how to publish and, more importantly, what must be
settled first — each of these is hard or impossible to walk back once packages are in
consumers' caches.

## Decide before the first publish

### 1. Where the packages live

The source is public — this repository mirrors Proxytrace's visibility and is consumed as a
submodule — so a product that builds against the submodule needs no feed at all. This decision
only governs a product that consumes Core as a **package** instead:

| Option | Restore auth | Consequence |
|--------|--------------|-------------|
| **nuget.org**, public packages | none | anyone can restore; simplest |
| **GitHub Packages**, private | PAT required, no anonymous read | only authenticated builds restore |
| **Azure Artifacts**, private | PAT / credential provider | same, nicer feed tooling |

Because Proxytrace already consumes Core by submodule and stays publicly buildable regardless of
this choice, the feed is a convenience decision for future package consumers, not a buildability
constraint. If Core's source is ever taken private, revisit this: a private feed would then be the
only way a package consumer outside the organisation could restore.

### 2. The licence

`Directory.Build.props` packs `LICENSE`, currently a copy of Proxytrace's Elastic License
2.0, as a **placeholder**. A shared library distributed separately and consumed by products
that may not all be Elastic-licensed probably wants different terms. Settle this first: a
licence cannot be recalled from consumers who already restored the package.

### 3. The package ID prefix

Reserve the `Nordstein.*` prefix on nuget.org before the first public push, otherwise someone
else can take the next ID in the family.

## Publishing

Versioning is SemVer, one shared version across all Core packages, supplied by the pipeline:

```bash
dotnet pack Nordstein.Core.sln -c Release -p:NordsteinCoreVersion=1.2.3 -o artifacts
dotnet nuget push "artifacts/*.nupkg" --source <feed> --api-key <key> --skip-duplicate
```

The `.snupkg` symbol packages are pushed the same way and are worth pushing: without symbols and
SourceLink, stepping into Core from a consuming product stops working, which is the change
people notice and resent most about a package split.

Publish CI-build prereleases (`1.3.0-ci.<run>`) from every merge to Core's default branch, so a
product can validate an unreleased Core without a tag. The `core-package` CI job already builds
exactly these; it just does not push them.

## Consuming a published version

Set the version once, in the repository root `Directory.Build.props`:

```xml
<NordsteinCoreVersion Condition="'$(NordsteinCoreVersion)' == ''">1.2.3</NordsteinCoreVersion>
```

Pin exact versions and let Dependabot raise the bumps. Floating ranges across several packages
turn one bad publish into a failure in every product at once, with no diff to point at.

## How this repository was extracted

This repository was split out of Proxytrace once the reference mechanism had been validated
in-place. The record is kept here because the same steps apply to any future product that seeds
its own foundation from an existing codebase.

### Not `git subtree split`

The obvious command is the wrong one:

```bash
git subtree split --prefix=core -b core-extraction   # DON'T
```

It filters strictly by path and **does not follow renames**. Everything under `core/` had been
`git mv`'d there from `Proxytrace.Common/`, `Proxytrace.Common.Tests/` and `Proxytrace.Testing/`,
so a subtree split sees only the move commit onward — measured at the time: **1 commit**. That is
a copy with extra steps; every `git blame` and `git log` would dead-end at the extraction.

### `git filter-repo`, with the pre-move paths mapped on

filter-repo maps the old paths onto the new ones, so the pre-move history comes along. Run against
the merged Proxytrace `master`, it preserved **28 commits**, back through the licensing subsystem,
the secrets-at-rest retrofit and the original `rename to proxytrace`.

```bash
# git-filter-repo: `pip install git-filter-repo`, or a distro package (e.g. pacman -S git-filter-repo)

# filter-repo rewrites history in place — always run it on a throwaway clone
git clone --no-local <proxytrace-repo> ../core-extraction
cd ../core-extraction

git filter-repo \
  --path Proxytrace.Common.Tests --path Proxytrace.Common --path Proxytrace.Testing --path core \
  --path-rename Proxytrace.Common.Tests/:Nordstein.Core.Common.Tests/ \
  --path-rename Proxytrace.Common/:Nordstein.Core.Common/ \
  --path-rename Proxytrace.Testing/:Nordstein.Core.Testing/ \
  --path-rename core/:
```

Keep the `.Tests` rename **before** the `Proxytrace.Common/` one: renames apply in order and
first match wins.

This recipe documents the original Common/Testing extraction. `Nordstein.Core.Domain` was moved in
a later tranche after Core became a submodule; it is not part of the historical path mapping above.

Then verify before pushing — the extracted repository has no parent directory to inherit from,
which is exactly the kind of thing that only shows up here:

```bash
dotnet build Nordstein.Core.sln
dotnet test  Nordstein.Core.sln
dotnet pack  Nordstein.Core.sln -c Release -p:NordsteinCoreVersion=1.0.0 -o ./out
```

### How Proxytrace consumes it now

Proxytrace embeds this repository as a **git submodule at `core/`**, so the paths the build
already expects are unchanged: source mode, the Dockerfile restore layers and the `detect-changes`
`^core/` regex all keep working exactly as they did while Core was staged in-tree. A sibling
checkout (`../Core/`) and published packages were the alternatives; the submodule was chosen
because it needs no feed and keeps a Core edit a one-build change. Bumping Core in the product is a
submodule-pointer update, raised by Dependabot once it tracks submodules.
