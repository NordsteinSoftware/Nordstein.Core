# Nordstein.Core

Product-agnostic foundation code shared across Nordstein applications. Nothing in here knows
what any one product is; nothing in here may ever learn.

| Package | Contents |
|---------|----------|
| [`Nordstein.Core.Common`](Nordstein.Core.Common) | Clock/randomness seams, async primitives, validation, type conversion, secret-protection contracts, Autofac helpers, hosting defaults |
| [`Nordstein.Core.Domain`](Nordstein.Core.Domain) | Domain objects/entities, repository and transaction contracts, generators, paging, persistence exceptions, entity events, Autofac discovery |
| [`Nordstein.Core.Testing`](Nordstein.Core.Testing) | `BaseTest<TModule>` and the shared MSTest + AwesomeAssertions + NSubstitute harness |

## The one rule

**Core may not reference any product. Ever.** The dependency arrow points one way: products
depend on Core. A Core type that needs to know about an agent, a trace, or a project belongs in
that product, not here — and if it feels like it belongs in both, it needs a seam (an interface
Core declares and the product implements), not a reference.

## Building

```bash
dotnet build Nordstein.Core.sln
dotnet test  Nordstein.Core.sln
```

Core builds and tests without any product — that is the property being protected. If it ever
stops being true, the split has regressed.

## How products consume it

A consuming product embeds this repository — today as a git submodule (Proxytrace mounts it at
`core/`) — and declares an item, not a reference:

```xml
<ItemGroup>
  <NordsteinCoreReference Include="Nordstein.Core.Common" />
</ItemGroup>
```

The product's `Directory.Build.targets` expands that item into either a `ProjectReference`
(**source mode** — the default, and what the submodule gives you) or a `PackageReference`
(**package mode**). Nothing here is published yet; [`PUBLISHING.md`](PUBLISHING.md) records the
feed, licence, and prefix decisions that must be settled before the first publish.

## History

The original Common and Testing packages were extracted from Proxytrace with `git filter-repo`,
mapping their pre-move paths so their full history came across — **not** a `git subtree split`,
which follows paths without renames and would have collapsed the past to a single commit. Domain was
added to this repository in a later extraction tranche. The original repository-extraction recipe is
in [`PUBLISHING.md`](PUBLISHING.md#how-this-repository-was-extracted).
