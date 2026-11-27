# ADR 0002: Architecture fence kit in Core.Testing and the Internal/ fence

- Status: proposed
- Date: 2026-11-27
- Issue: [#73](https://github.com/NordsteinSoftware/Nordstein.Core/issues/73) (part of #54, ADR 0001 decision 6)

## Context

nordstein-dms guards its architecture with reflection tests ("fences"). The shared scan code is
`Nordstein.Dms.Testing/Fences/FenceScanExtensions.cs`; the fences and their planted positive
controls are in `Nordstein.Dms.Server.Tests/Fences/` (source at `cf146a85`). The scan code mixes
generic walks (constructor and method parameters, static members, type reachability) with dms
policy (`IsAccessContextSource`, `IsRawRepository`, `IsEntityGenerator`, the `Migrations`
exemption). The assembly list comes from `Internal/SolutionProjects.cs`, which hard-codes
`Nordstein.Dms.sln`, the `core/` folder and the kiosk.

dms also has the written rule "every `internal` type lives in an `Internal/` folder with a
matching `.Internal` namespace". No test enforces it. Core follows the same layout
(`Io/Internal/`, `Conversion/Internal/`), with four breaks in shipped assemblies:
`Common/Async/AsyncLock.cs`, `AI/Tools/JsonToolArgument.cs`, `Storage/Transaction.cs` and
`Testing/Module.cs`.

Questions: the public API of the kit, how a product names its own fences without Core knowing
its assemblies, and when the four breaks are fixed.

## Decision

1. **The kit is generic mechanics only; the policy stays in the product.** A new namespace
   `Nordstein.Core.Testing.Fences` in the existing `Nordstein.Core.Testing` package. It adds no
   package reference. Every scan takes the policy as an argument: a `Func<Type, bool>` for
   "fenced", namespace prefixes for exemptions. A product writes its fences, allowlists and
   planted controls in its own test project. `IsAccessContextSource`, `IsRawRepository` and
   `IsEntityGenerator` stay in dms.
2. **Public surface** (all extension methods, so the kit itself passes the static-helper scan):
   - `InjectedParameters(this Assembly)` returns `IEnumerable<InjectedParameter>`. It covers the
     constructor and method parameters of every non-interface type, including nested,
     compiler-generated and static types. `InjectedParameter` is a public sealed record
     `(Type Owner, Type ParameterType)`. Its `ToString()` is the `Owner.Full.Name: Display` line.
   - `ParametersMentioning(this Assembly, Func<Type, bool> isFenced)` returns the matching
     parameters as sorted, distinct lines (ordinal order).
   - `Mentions(this Type, Func<Type, bool> isFenced)` walks interfaces, base types, element types,
     generic arguments and constraints, and delegate `Invoke` signatures. It is cycle-safe.
   - `StaticHelpers(this Assembly, IEnumerable<string> exemptNamespacePrefixes)` lists every
     non-private, non-extension static method or property and every static field that is neither
     `const` nor `readonly`. Always exempt: names containing `<`, types named `Program`,
     `Coverlet.*`. An overload without exemptions is included.
   - `ReferencedAssembliesMatching(this Assembly, Func<AssemblyName, bool>)` returns the
     matching metadata references as sorted names.
   - `MisplacedInternalTypes(this Assembly)` returns every top-level non-public type whose
     namespace does not end in `.Internal` and does not contain `.Internal.`. Exempt are
     compiler-generated and file-local types (`[CompilerGenerated]`, `[Embedded]`, a `<` in the
     name) and `Coverlet.*`. Nested types follow their outer type.
   - `Display(this Type)` gives the C# spelling without namespaces.
3. **The solution-derived assembly list is data, not a rule.** `SolutionFile` is a public sealed
   class. `new SolutionFile("X.sln")` walks up from `AppContext.BaseDirectory`. A second
   constructor takes an explicit start directory, for tests. It reads `.sln` and `.slnx`.
   Members:
   - `Root`.
   - `Projects`: `IReadOnlyList<SolutionProject>`, where `SolutionProject` is the record
     `(Name, RelativePath, AssemblyName)`. `AssemblyName` comes from the csproj, else the
     project name.
   - `UnlistedProjectFiles()`: csproj files under the root that the solution does not list.
     It skips `bin/`, `obj/` and every dot-directory.
   - `SolutionProject.Load()`: loads the project's assembly by name.

   Which projects count as production is the product's own filter
   (`Projects.Where(p => !p.Name.EndsWith("Tests"))`). Core knows no project names. A missing
   solution, or an unknown or unreadable format, throws `InvalidOperationException` that names
   the file and the start directory.
4. **Every fence has a planted positive control.** This is a convention, not an API. Each fence
   test has a sibling test that runs the same scan over a planted violating type in the test
   project's `Fences/Internal/`, and asserts the exact hits. Core's own tests do the same for
   every scan branch.
5. **Core adopts the Internal/ fence for its shipped assemblies.** A new test project,
   `Nordstein.Core.Testing.Tests`, references every packable project. It holds the kit's tests
   and planted controls, plus Core's own fences:
   - Every packable project in `Nordstein.Core.sln` loads in the test host, and no csproj on
     disk is unlisted.
   - `MisplacedInternalTypes` is empty for every packable assembly.

   Test projects are not fenced. They ship nothing, and 13 test files hold internal helpers at
   the project root today. Core does not adopt the static-helper fence: `BaseTest.BuildContainer`
   and other public statics are part of the existing contract.
6. **The four breaks move before the fence lands, in their own PR.** They move into
   `Internal/` with a `.Internal` namespace. All four types are internal, so no public API
   changes. The fence PR then starts green and has no unrelated moves.

## Alternatives considered

- **Port `FenceScanExtensions` as it is.** Rejected. It names dms types and would make Core know
  a product (the One Rule).
- **Attribute-driven fences** (`[Fenced]` on a type, one generic test). Rejected. That puts a
  marker on production types, so the policy leaks into the code it guards. A planted control
  cannot prove an attribute that is missing.
- **NetArchTest or ArchUnitNET.** Rejected for now. Each adds a dependency to every consumer of
  `Core.Testing`. Neither covers the dms parameter-reachability walk (delegates, `IIndex<,>`,
  `Lazy<>`). The walk is about 150 lines that we already own.
- **A separate `Nordstein.Core.Testing.Architecture` package.** Rejected. ADR 0001 decision 2
  allows existing packages only, and the kit needs nothing that `Core.Testing` lacks.
- **Fix the four breaks in the fence PR.** Rejected. The diff would mix moves with new API and be
  harder to review.
- **Fence test projects too.** Deferred. That needs 13 more moves for no consumer-facing gain.
  Revisit it when a test project ships a package.

## Consequences

- New public API in `Nordstein.Core.Testing`: `Fences` namespace, two records, one class, seven
  extension methods. A minor version bump. XML docs and `docs/testing.md` ("Architecture
  fences") come in the same PR.
- `Nordstein.Core.sln` gains `Nordstein.Core.Testing.Tests`. Adding a packable project without
  listing it fails the build's test run.
- dms (switched off) can drop its copy of the walk and `SolutionProjects` once it consumes the
  release. Its policy predicates stay. No task is written for it now.
- The `.slnx` support is new code with no dms precedent. It needs its own tests: a fixture
  solution in each format.
