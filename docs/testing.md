# Testing

Two things live in this repository: the tests **of** Core (`*.Tests` projects) and the shared
test harness **for** every Nordstein product (`Nordstein.Core.Testing`). Both are held to the
foundation bar: a harness bug silently weakens every product's suite, and Core's own tests are
the only safety net the foundation has.

## The coverage bar (Core-specific)

- **Every public type and member is exercised by a test.** If it is public, a consumer will call
  it; if no test calls it, we do not know it works.
- **Every branch of non-trivial logic gets a test**, including the unhappy paths: `null`/empty
  inputs, cancellation observed mid-operation, concurrent callers for anything claiming thread
  safety, disposal, boundary values.
- **Every bug fix starts with a failing regression test** — red first, then the fix.
- Run the **full solution** before claiming done — `dotnet test Nordstein.Core.sln` takes
  seconds; scoped runs are for iterating only, and a scoped green run is never reported as "all
  tests pass".

### The coverage gate (CI)

The owner's floor is **90 % line and 90 % branch coverage for every shipped assembly**, the
test harness `Nordstein.Core.Testing` included. CI's `build` job enforces it after the one test run:

```bash
dotnet test Nordstein.Core.sln --settings tools/coverage.runsettings --collect "XPlat Code Coverage" --results-directory artifacts/coverage
python3 tools/coverage_gate.py --reports artifacts/coverage --floors tools/coverage-floors.json
```

- `tools/coverage.runsettings` instruments `[Nordstein.Core.*]*` except `[*.Tests]*`, and skips
  generated code (`GeneratedCodeAttribute`, `CompilerGeneratedAttribute`) and auto-properties.
- `tools/coverage_gate.py` merges every `coverage.cobertura.xml` under `--reports` per line (an
  assembly appears in the report of every test project that loads it: hits add up, each branch
  condition keeps its best coverage), truncates each percentage to one decimal (89.99 % is 89.9 %)
  and compares it with `tools/coverage-floors.json` (`{"<assembly>": {"line": 90.0, "branch": 90.0}}`).
- It prints `coverage gate: PASS — line N %, branch N %` (or `FAIL`) and one `BELOW`, `NO FLOOR` or
  `NO DATA` line per failure, writes `coverage.json` next to the reports, and exits 0 or 1.
- **Never green on a missing result.** No report file, an unreadable or empty report, unreadable
  floors, a measured assembly without a floor and a floor without coverage all fail.
- A new shipped assembly gets its floor (at least 90.0 for both) in the same change;
  `tools/tests/test_coverage_config.py` fails on a shipped assembly the settings do not measure, on
  a test project they do, and on a missing floor or one below 90. Lowering a floor needs the owner.
- The gate's own tests run in CI's `tooling` job: `python3 -m unittest discover -s tools/tests`.
  Python standard library only.

### Repository checks (CI `tooling` job)

- `python3 -m unittest discover -s tools/tests`: the tests of everything under `tools/` and
  `.claude/hooks/`, including one that runs each check against the live repository.
- `python3 tools/docs_check.py --root .`: every relative link in every Markdown file resolves (the
  file part; anchors are not checked; links in code are not links), and every `docs/*.md` page but
  `docs/README.md` has a row in the `CLAUDE.md` index table. One line per problem, exit 1 on any.
- `.claude/hooks/guards.py edit`, wired in `.claude/settings.json` as a `PreToolUse` hook on
  `Edit|Write|MultiEdit`: denies (exit 2, the reason on stderr) a C# edit that adds a
  `#pragma warning disable`, `#nullable disable`, `[SuppressMessage]` or `[ExcludeFromCodeCoverage]`
  without a `// WHY:` on its line or in the comment block directly above, or that adds a
  null-forgiving `!`, with no exemption. Only what an edit adds counts. It lexes the file, so a `!`
  in a string, a character, a comment, a directive (`#if !DEBUG`), a negation or `!=` is no alarm,
  while the holes of an interpolated string are code; its test runs it over every C# file of the
  repository as a positive control (it must find `Validation.Success`) and a false-alarm check. A
  malformed event exits 1 and never blocks. It catches accidents; review and CI are the backstop.
- `.claude/agents/reviewer.md`: the adversarial reviewer for Standard of Care 3, read-only.

## Core principles (non-negotiable, shared across all Nordstein repos)

1. **No shared state between tests.** No system-under-test, fakes, fixtures, or seeded entities
   in instance fields, static fields, or `TestContext.Properties`. Everything a test needs is
   built *inside* the test method from a fresh service provider. A shared service-container
   handle under the opt-in exception in
   [Container-backed tests](#container-backed-tests-rare-deliberate) is infrastructure, not test
   state: it holds no test data and is never a fixture.
2. **No `[TestFixture]`-style helper classes or shared setup objects.** Dependencies are
   configured through **DI + NSubstitute**, not hand-rolled fixture plumbing.
3. **The DI container is the fixture.** A fresh, isolated container (with its own in-memory
   database where storage is involved) is created per `GetServices()` call; `[TestCleanup]`
   disposes it. That is how you get isolation *and* avoid fixture classes at the same time.
4. **Substitute infrastructure, never the domain.** Fake external clients and I/O; use real
   domain entities, real repositories, real in-memory storage.

If you find yourself writing `private readonly Foo foo = Substitute.For<Foo>();` at class scope —
stop; register the substitute in the container instead.

## The harness (`Nordstein.Core.Testing`)

All tests extend `BaseTest<TModule>` (MSTest + AwesomeAssertions + NSubstitute):

```csharp
[TestClass]
public sealed class MyTests : BaseTest<Module>
{
    [TestMethod]
    public async Task GetAsync_AfterAdd_RoundTrips()
    {
        IServiceProvider services = GetServices();

        var repo = services.GetRequiredService<IRepository<ICustomer>>();
        var generator = services.GetRequiredService<IDomainEntityGenerator<ICustomer>>();

        ICustomer entity = await generator.CreateAsync(CancellationToken); // persists
        ICustomer result = await repo.GetAsync(entity.Id, CancellationToken);

        result.Id.Should().Be(entity.Id);
    }
}
```

How it works, and why it is stateless:

- `GetServices(action)` builds a **brand-new Autofac container every call**: it registers
  `Nordstein.Core.Testing.Module`, then your `TModule`, then the class-level
  `ConfigureContainer(ContainerBuilder)` override, then the per-call `action`.
- Containers are recorded in `TestContext.Properties` and disposed in `[TestCleanup]` — never
  manage container lifetime yourself. `BuildContainer` (static, not auto-disposed) exists as a
  deliberate last-resort escape hatch for a genuinely expensive `[ClassInitialize]` fixture that
  holds no test data.
- `CancellationToken` is a protected property sourced from `TestContext.CancellationToken`; pass
  it to every async call.
- MSTest creates a new test-class instance per test method, and each test builds its own
  container — there is no state to leak as long as everything stays inside the method.

Two configuration hooks: `ConfigureContainer` applies to every test in the class (use sparingly);
`GetServices(builder => …)` is per-method and is the default choice.

Each test project ships **one `Module : Autofac.Module`** wiring the layer under test plus the
always-needed stubs. That per-project module *is* the shared baseline — there are no other shared
fixtures.

## Substitution toolkit

- `builder.RegisterStub<T>()` registers an NSubstitute fake, optionally with behavior:
  `builder.RegisterStub<IMailClient>(fake => fake.SendAsync(default!, default).ReturnsForAnyArgs(…))`.
  ⚠️ **Scope gotcha:** `RegisterStub` is `InstancePerDependency` — every resolve returns a
  *different* fake, so `Received()` assertions against a freshly resolved stub silently see
  nothing.
- For a fake you must both configure **and assert on**, create it once and
  `builder.RegisterInstance(fake).As<T>()` — same instance for the SUT and the assertion, still
  local to the test method.
- Default to **real in-memory storage** over substituted repositories — it exercises the real
  mappers. Substitute `IRepository<T>` only to test failure handling or isolate from persistence.
- **NSubstitute 6 nullability (no `!` allowed):** `CallInfo.Arg<T>()` returns `T?` — guard with
  `ArgumentNullException.ThrowIfNull(arg)` before feeding it somewhere non-null. `Arg.Is<T>(x => …)`
  predicates are expression trees, so `x is not null` is illegal — use `x != null && …`.
- Private **static** helper methods taking a `ContainerBuilder` (or building a value object) are
  fine — the rule is *no shared state*, not *no helper methods*.

## Naming

```
[Subject]_[Condition]_[ExpectedOutcome]
```

e.g. `LockAsync_WhenCancelled_ThrowsOperationCanceled`, `Map_WithArchivedReferent_StillResolves`.

## Container-backed tests (rare, deliberate)

Substituting a client library asserts how we *call* a driver, never how the server *replies* — a
mocked suite cannot see a wire-format change (a full protocol switch in a Redis client passed a
mocked suite untouched). Where reply parsing or server-side semantics are the thing under test,
start the real service in a throwaway container (Testcontainers):

- Build the container **inside the test method** by default — the no-shared-fixture rule still
  applies.
- Pin the image to the tag the deployed stack actually runs.
- When no container runtime is reachable, `Assert.Inconclusive` (report as skipped) — `dotnet
  test` must never acquire a hard Docker dependency. Provide an environment flag (Proxytrace uses
  `PROXYTRACE_REQUIRE_DOCKER_TESTS`) that flips the skip into a hard failure, and set it in CI so
  the coverage can never be lost silently.
- The skip guard must wrap the builder's `Build()` call, not just `StartAsync` — Testcontainers
  pings the Docker endpoint during `Build()`, so that is where a missing runtime actually throws.
- **Opt-in exception: one shared service container per test process.** A product may start one
  container lazily and share it across a test process only when every condition holds:
  - every test creates its own namespace on the server (a database, a schema, a bucket) from a
    generated name inside the test method, and drops it when the test ends — also when it fails
    or is inconclusive;
  - the server holds no test data between tests: anything shared, such as a template the
    namespaces are cloned from, holds only schema and its bookkeeping;
  - startup is lazy, thread-safe and not bound to one test's cancellation token: a cancelled
    first caller never fails a later one;
  - a startup failure reaches every test through the skip/require policy above, per test;
  - the handle exposes only the per-test namespace's connection details, never the container;
  - tests that act on the server itself or on server-wide objects (roles, tablespaces, server
    configuration) keep their own container.

Default to a mock; a container test is a targeted supplement for what a mock structurally cannot
cover, and it costs seconds, not milliseconds.

## Lessons from the products

Rules a consuming product's suite learned the hard way; they hold for Core's tests too.

- **Never `Assert.Inconclusive` inside an exception filter.** The runtime swallows an exception
  thrown while a `catch (…) when (…)` filter runs: the filter counts as false, the original
  exception propagates, and a test meant to be skipped fails instead. Catch in the body, decide
  there, and rethrow with a bare `throw;` (which keeps the stack trace):

  ```csharp
  catch (Exception exception)
  {
      SkipOrRethrow(exception); // returns only when the runtime is required; else Assert.Inconclusive
      throw;
  }
  ```
- **Gate opt-in suites with an environment variable, never a runsettings filter.** VSTest ANDs a
  runsettings `TestCaseFilter` with the command line's `--filter`, so a filter that excludes perf
  tests silently drops the very tests a `--filter` asks for. Gate in the suite's base class
  `[TestInitialize]`: every test is `Inconclusive` unless `<PRODUCT>_RUN_PERF_TESTS=1`. Never gate
  inside a test body, and load budgets lazily, behind the gate.
- **An inconclusive result is not a pass.** A local green run whose container tests were skipped
  proves nothing about them; CI sets the require flag ([Container-backed tests](#container-backed-tests-rare-deliberate))
  so a skip is red there.
- **Triage before touching code.** Classify a red test from its evidence (the TRX, the logs):
  - a product bug: the behaviour is wrong. Fix the code, keep the test;
  - infrastructure: a registry rate limit, a daemon gone, the network denied. Fix the environment
    and re-run once; never change a test for it;
  - a test bug: it asserts the wrong thing, races, or depends on order or shared state. Fix the
    test and the pattern it copied, and say so in the change.

  Never skip, disable, retry until green, or quarantine a test.
- **Every guard test carries a positive control.** A fence, scan or guard that inspects code (all
  types in `Internal/`, no static state, no suppression) also runs against a planted violation, or
  asserts that it finds a known instance, so a scan that silently matches nothing fails. Never
  delete the planted violation.

## Changing the harness itself

`Nordstein.Core.Testing` is consumed by thousands of tests across the products. Before changing
its behavior (container composition order, cleanup semantics, the in-memory database identity),
ask what every existing test that relies on the current behavior will do — and verify against a
consuming product's suite (source mode makes that a one-build check), not just Core's own.
