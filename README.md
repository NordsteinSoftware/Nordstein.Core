# Nordstein.Core

The shared, product-agnostic foundation of all Nordstein Software products: domain and storage
building blocks, a licensing engine, hosting and utility seams, and the common test harness.
Nothing in here knows what any one product is; nothing in here may ever learn.

Because every product stands on this code, it is held to a deliberately higher bar than a product
repository: warnings are errors, every public member is documented and tested, and changes get
adversarial review. [`CLAUDE.md`](CLAUDE.md) states that standard of care in full;
[`docs/`](docs/README.md) documents each package.

## Packages

| Package | What it gives a product |
|---------|-------------------------|
| [`Nordstein.Core.Common`](Nordstein.Core.Common) | The everyday seams and utilities: `IClock` and `IRandom` (testable time/randomness), the keyed `IAsyncLock`, validation helpers, type conversion, secret-protection contracts (`ISecretProtector`, `ISecretHasher`, `Sha256`), Autofac registration helpers, resilient-hosting defaults (`AddResilientBackgroundServices`), text/serialization helpers |
| [`Nordstein.Core.Domain`](Nordstein.Core.Domain) | The domain foundation: `IDomainObject`/`IDomainEntity` contracts and bases, the factory-delegate pattern, test-data generators, `IRepository`/`ITransaction`, archive (soft-delete) contracts, paging, persistence exceptions, entity events, and Autofac discovery of a product's domain assembly |
| [`Nordstein.Core.Storage`](Nordstein.Core.Storage) | The EF Core storage foundation: `NordsteinDbContext`, generic repositories with optimistic concurrency, the ambient-transaction seam, a reference-data cache, and `StorageFoundationModule<TContext>` (per-entity discovery). **Provider-neutral** — depends on EF Core Relational only; each product brings its own provider, concrete context, and migrations |
| [`Nordstein.Core.Licensing`](Nordstein.Core.Licensing) | The generic license engine: JWT verification, runtime activation, the resolved `LicenseSnapshot`, feature/limit queries, and the periodic server check with offline grace. The product supplies its tier/feature/limit vocabulary through `ILicenseTierPolicy` and its identity (issuer, audience, trust root) through `LicensingConfiguration` |
| [`Nordstein.Core.AI`](Nordstein.Core.AI) | The AI foundation: LLM message/conversation types, tool specifications, prompt templates, completions and token usage, model parameters, the versionless `IAgent` and `IModelClient` contracts, and structured model-output parsing (`IOutputFormat`, `ITextSerializer`). Provider implementations stay in the products |
| [`Nordstein.Core.Testing`](Nordstein.Core.Testing) | The shared test harness: `BaseTest<TModule>` with per-test, self-disposing Autofac containers, plus the MSTest + AwesomeAssertions + NSubstitute baseline every product suite builds on |

Dependencies flow strictly downward — Storage → Domain → Common; Licensing → Common; Testing →
Common — and never outward to a product. See [`docs/architecture.md`](docs/architecture.md).

## The one rule

**Core may not reference any product. Ever.** The dependency arrow points one way: products
depend on Core. A Core type that needs to know about a product concept belongs in that product,
not here — and if it feels like it belongs in both, it needs a seam (an interface Core declares
and the product implements), not a reference.

The rule is enforced mechanically, not by review: this solution builds and tests standalone, and
the consuming product's CI additionally packs Core and rebuilds the whole product against the
resulting `.nupkg` files — which catches types missing from the package surface and dependencies
Core forgot to declare.

## Building

Requires the .NET 10 SDK.

```bash
dotnet build Nordstein.Core.sln    # warning-free, or it fails: TreatWarningsAsErrors is on
dotnet test  Nordstein.Core.sln    # the full suite — it is fast, so always run all of it
```

Core builds and tests without any product present — that is the property being protected. If it
ever stops being true, the split has regressed.

## How products consume it

A consuming product embeds this repository — today as a git submodule (Proxytrace mounts it at
`core/`) — and declares an item, not a reference:

```xml
<ItemGroup>
  <NordsteinCoreReference Include="Nordstein.Core.Common" />
</ItemGroup>
```

The product's `Directory.Build.targets` expands that item into either a `ProjectReference`
(**source mode** — the default when the submodule checkout is present, keeping a Core change a
one-build edit) or a `PackageReference` at `$(NordsteinCoreVersion)` (**package mode**). Core
versions independently of its consumers. Nothing is published to a feed yet;
[`PUBLISHING.md`](PUBLISHING.md) records the feed, licence, and prefix decisions that must be
settled before the first publish.

## Documentation

| | |
|---|---|
| [`docs/`](docs/README.md) | Per-package developer docs: architecture & DI conventions, code style, the domain and storage foundations, validation, licensing, testing |
| [`docs/product-playbook.md`](docs/product-playbook.md) | The transferable practices for building a product on Core — docs discipline, e2e testing, user manual, i18n, CI shape, perf budgets |
| [`CLAUDE.md`](CLAUDE.md) | The standard of care for changes to this repository, and AI-assistant guidance |
| [`PUBLISHING.md`](PUBLISHING.md) | Packaging/versioning decisions and how this repository was extracted |

## History

Core was extracted from Proxytrace (the first consuming product) in tranches — Common and
Testing first, then Domain, Storage, and Licensing. The original extraction used
`git filter-repo` with pre-move path mappings so the code's full history came across — **not** a
`git subtree split`, which follows paths without renames and would have collapsed the past to a
single commit; later tranches were migrated as ordinary commits into this repository. The
verified recipe is in [`PUBLISHING.md`](PUBLISHING.md#how-this-repository-was-extracted).

## License

See [`LICENSE`](LICENSE). The terms for Core as a separately distributed library are still being
settled before the first package publish — [`PUBLISHING.md`](PUBLISHING.md) tracks that decision.
