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

## Core principles (non-negotiable, shared across all Nordstein repos)

1. **No shared state between tests.** No system-under-test, fakes, fixtures, or seeded entities
   in instance fields, static fields, or `TestContext.Properties`. Everything a test needs is
   built *inside* the test method from a fresh service provider.
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
  deliberate last-resort escape hatch for a genuinely expensive `[ClassInitialize]` fixture.
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

- Build the container **inside the test method** — the no-shared-fixture rule still applies.
- Pin the image to the tag the deployed stack actually runs.
- When no container runtime is reachable, `Assert.Inconclusive` (report as skipped) — `dotnet
  test` must never acquire a hard Docker dependency. Provide an environment flag (Proxytrace uses
  `PROXYTRACE_REQUIRE_DOCKER_TESTS`) that flips the skip into a hard failure, and set it in CI so
  the coverage can never be lost silently.
- The skip guard must wrap the builder's `Build()` call, not just `StartAsync` — Testcontainers
  pings the Docker endpoint during `Build()`, so that is where a missing runtime actually throws.

Default to a mock; a container test is a targeted supplement for what a mock structurally cannot
cover, and it costs seconds, not milliseconds.

## Changing the harness itself

`Nordstein.Core.Testing` is consumed by thousands of tests across the products. Before changing
its behavior (container composition order, cleanup semantics, the in-memory database identity),
ask what every existing test that relies on the current behavior will do — and verify against a
consuming product's suite (source mode makes that a one-build check), not just Core's own.
