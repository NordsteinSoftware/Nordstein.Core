# C# Backend Code Style

Baseline assumptions: .NET 10+, latest C# language version, `Nullable=enable`,
`ImplicitUsings=enable`, `TreatWarningsAsErrors=true`. If a project deviates, it must say so
explicitly in its own README — silence means these rules apply.

Keywords used below: **MUST** (non-negotiable), **SHOULD** (default; deviations need a reason in a
comment), **PREFER** (tie-breaker).

---

## 1. Philosophy

1. **Make dependencies explicit.** Every collaborator arrives through the constructor. If a class
   needs something, you can see it in its signature without reading the body.
2. **Prefer composition over inheritance.** Inherit only from framework base types (`ControllerBase`,
   `BackgroundService`, `Entity`) — never from my own concrete classes.
3. **Immutable by default, mutable by justification.** Data is `record` + `init`; behavior lives in
   small stateless services.
4. **Stateless by default.** A service that holds per-request or per-entity state is a bug waiting
   for concurrency. State belongs in the database, the entity, or an explicitly thread-safe store.
5. **Async all the way.** No sync-over-async, ever. Every await point accepts a
   `CancellationToken`.
6. **Small, honest abstractions.** An interface exists to hide an implementation or to be faked in
   tests — not for ceremony. Records are the exception: plain data is exposed as concrete records.
7. **The compiler is the first reviewer.** Warnings are errors. Suppression operators are a last
   resort, not a style.
8. **Tests are code.** They get the same naming, structure, and review attention.

---

## 2. Project Baseline & Tooling

- **MUST** target a current .NET (10+), `<Nullable>enable</Nullable>`, `<ImplicitUsings>enable</ImplicitUsings>`.
- **MUST** set `<TreatWarningsAsErrors>true</TreatWarningsAsErrors>` solution-wide. Never merge
  code that adds a warning; never disable a warning globally to get past one.
- **MUST NOT** use `#pragma warning disable` for nullable warnings. Fix the null flow instead (see §8).
- **SHOULD** use file-scoped namespaces (`namespace Foo.Bar;`) everywhere except generated code.
- **SHOULD** keep a `GlobalUsings.cs` with only the one or two namespaces every file needs. Rely on
  implicit usings for the BCL; do not create a dumping ground.
- **SHOULD NOT** add analyzer packages that fight the format below (StyleCop's ordering rules, for
  example). The build + review process is the gate; if analyzers are added, they must encode *these*
  rules, not a different style.
- **MUST** run a build (and the affected tests) before claiming a change is done. "It compiles in my
  head" is not done.

---

## 3. Layering

- **SHOULD** Onion Architecture
- **MUST** keep dependencies pointing one way: `Host → Application → Domain`, with `Storage` and
  `Infrastructure` implementing interfaces declared in `Domain` (or a shared `Common`/Core layer).
  Domain references nothing outward and performs no I/O.
- **MUST NOT** create cycles between projects, and **MUST NOT** let a lower layer reference a layer
  above it (no `Domain → Storage`, no `Application → Api`).
- **MUST** keep the composition root (DI registration, configuration binding, middleware order) in
  the host only. Libraries expose registrations; they never build their own container.
- **SHOULD** mirror the product's project names, one project per layer, plus `Product.<Layer>.Tests`
  per layer. Test projects reference production projects — never the other way around.
- **SHOULD** put shared, product-agnostic code (validation, storage bases, async primitives, test
  harness) into a separate reusable core project rather than a `Utils` folder.

---

## 4. Interfaces, Visibility, Records

This is the part that replaces "public API" in other languages.

- **MUST** expose cross-layer/public API as interfaces (`IAgent`, `IAgentRepository`,
  `IIngestionStream`). Implementations are `internal sealed` by default.
- **MUST NOT** put an interface on plain data. POCOs, DTOs, request/response models, events, and
  value objects are concrete `record` types — no `IAgentDto`, ever.
- **MUST** name interfaces `IThing` and the implementation `Thing` (or a descriptive name, e.g.
  `RedisIngestionStream` for `IIngestionStream` when there are multiple implementations).
- **MUST** keep one interface per file, named after the interface. Small types that are part of the
  contract (an accompanying `record struct`, the matching exception) may live in the same file.
- **MUST** make implementations `sealed` unless they are explicitly designed for inheritance. Default
  to `sealed` for records and classes alike.
- **SHOULD** keep everything `internal` unless another assembly genuinely consumes it. Test access is
  granted with `[assembly: InternalsVisibleTo("Product.Layer.Tests")]`, not by widening visibility.
- **SHOULD** mark implementation types registered by reflection (`[UsedImplicitly]` or equivalent) so
  accidental deletion is caught and tooling does not flag them as unused.
- Public surface is limited to: interfaces, records/enums/DTOs, exceptions, attributes, delegate
  declarations, and DI registration entry points (modules). Everything else is `internal`.
- **PREFER delegate factories over factory interfaces** for "create an instance of X" seams:

  ```csharp
  public interface IAgent
  {
      public delegate IAgent CreateNew(string name, IProject project, bool isSystemAgent = false);
  }

  // registered once in DI; tests can register a fake with one line
  builder.Register<IAgent.CreateNew>(c => (...args) => new Agent(args, c.Resolve<IClock>()));
  ```

  A single-method factory interface adds a name and a test double for no benefit.

- **Controllers are a framework entry point, not an API to call**: `public`, but still thin (see §12).

---

## 5. Dependency Injection

DI is not a pattern to apply where convenient — it is the wiring model for everything.

- **MUST** use constructor injection. Never property injection, never method injection, never
  `new`-ing up a service inside another service.
- **MUST NOT** inject `IServiceProvider` or resolve services dynamically (service locator). The only
  sanctioned exceptions are framework seams that inherently need it (hosted-service factories,
  `ILifetimeScope` inside a singleton that must create per-request scopes). Document those in place.
- **MUST NOT** capture `IServiceProvider`/`HttpContext` in singletons. If a service needs the
  request's `DbContext` or current user, it is scoped (`InstancePerLifetimeScope` / `AddScoped`).
- **MUST** register services in per-project registration modules/extension methods
  (`Product.Domain.Module`, `services.AddApplication()`), one composition point per layer. The host
  module alone decides what runs.
- **SHOULD** choose lifetimes deliberately:
  - **Singleton** — stateless services, caches, broadcasters, options. The default for services that
    hold no per-request state.
  - **Scoped** — anything touching the request `DbContext`, the current user, or request-scoped state.
  - **Transient** — cheap per-call stores/queries. Use sparingly; prefer injecting the interface.
  Make the reason for a lifetime visible in code review; a `SingleInstance` that captures a repository
  is a bug.
- **MUST** inject the *interface*, never the implementation — including in tests, so fakes work.
- **SHOULD** inject configuration as a validated options object, not `IConfiguration`:

  ```csharp
  internal sealed record StatisticsOptions
  {
      public required TimeSpan Retention { get; init; }

      public void Validate()
      {
          if (Retention <= TimeSpan.Zero)
              throw new InvalidOperationException($"{nameof(Retention)} must be positive.");
      }
  }
  ```

  Bind and `Validate()` once at startup, register the instance as a singleton. Never thread
  `IConfiguration` through the app.
- **MUST** get `HttpClient` from the factory (`IHttpClientFactory`) with named clients; never
  construct one ad hoc, and never store one in a static field.
- **SHOULD** register `Func<T>`/`Lazy<T>` delegates for construction seams (e.g. `Func<StorageDbContext>`)
  rather than injecting the container.
- **PREFER** keyed/named registrations for multiple implementations of one interface (two HTTP
  clients, two notification channels) over a switch inside a single implementation.

A typical service:

```csharp
internal sealed class AgentService
{
    private readonly IAgentRepository repository;
    private readonly IClock clock;
    private readonly ILogger<AgentService> logger;

    public AgentService(IAgentRepository repository, IClock clock, ILogger<AgentService> logger)
    {
        this.repository = repository;
        this.clock = clock;
        this.logger = logger;
    }
}
```

- **MUST NOT** use primary constructors in product code. Constructor + explicit `this.` assignment
  makes dependencies greppable, keeps field declarations near their docs, and avoids captured-parameter
  surprises. (Test helpers may use them where they genuinely reduce noise.)
- **MUST** keep private fields camelCase **without** an underscore prefix, assigned via `this.`.

---

## 6. Immutability, Statelessness, Concurrency

- **MUST** model domain entities and storage entities as `record` types — even the mutable ones.
- **MUST** keep domain entities immutable: `init`/`private init` properties, no setters, all mutation
  expressed as `with` expressions or methods that return a new instance.
- **MUST** use `required` + `init` on storage entities and DTOs whose values a caller must supply.
- **SHOULD** express updates as `this with { ... }`:

  ```csharp
  public Task<IAgent> ChangeEndpointAsync(IModelEndpoint endpoint, CancellationToken cancellationToken = default)
      => ApplyAsync(this with { Endpoint = endpoint }, cancellationToken);
  ```

- **MUST** expose collections as `IReadOnlyList<T>`, `IReadOnlyCollection<T>`, or
  `IReadOnlyDictionary<TKey, TValue>`. Defensively copy inbound collections (`.ToArray()`) so callers
  cannot mutate your state.
- **SHOULD** use `readonly struct`/`readonly record struct` for small value shapes (metrics, states,
  ranges). Avoid `System.Collections.Immutable` unless there is a measured need.
- **SHOULD** make stateless services singletons. If a service truly needs state, make it explicitly
  thread-safe (`ConcurrentDictionary`, `Channel<T>`) and document the lifetime contract.
- **MUST NOT** use `static` mutable state. `static` is allowed only for extension methods, pure
  helpers, and constants.
- **MUST** use a shared async lock abstraction (e.g. `IAsyncLock`) for in-process mutual exclusion
  around `await`s. Never hold a `lock`/`Monitor`/`SemaphoreSlim` across an `await`, and prefer keyed
  locks (per entity id) over one global lock.
- **MUST NOT** hand-roll thread synchronization in feature code when the platform/core provides a
  tested primitive.

---

## 7. Async & Cancellation

- **MUST** use `async`/`await` end to end. **MUST NOT** block: no `.Result`, `.Wait()`,
  `GetAwaiter().GetResult()`, `Task.Run(...).Result`, or sync-over-async of any kind.
- **MUST NOT** write `async void` (the lone exception is an event handler, which should itself be a
  thin wrapper around an awaited `Task`).
- **MUST** suffix async methods with `Async` (`FindAsync`, `CreateNewVersionAsync`). Framework
  conventions win: ASP.NET controller actions are exempt.
- **SHOULD** return `Task`/`Task<T>`. Use `ValueTask` only in measured hot paths or low-level
  primitives, never speculatively.
- **MUST** accept a `CancellationToken` on every asynchronous method and pass it through to every
  I/O call (`EF Core`, `HttpClient`, streams, delays).
  - Name it `cancellationToken`, last parameter.
  - `= default` on interfaces, domain methods, and library seams so callers can ignore it.
  - **No default** in controller actions and top-level hosts — the framework supplies one, and
    forgetting it should be a compile error.
- **MUST** attach `[EnumeratorCancellation]` when implementing `IAsyncEnumerable<T>` with a token:

  ```csharp
  public async IAsyncEnumerable<IAgentCall> EnumerateAsync(
      [EnumeratorCancellation] CancellationToken cancellationToken = default)
  {
      await foreach (var call in source.WithCancellation(cancellationToken))
          yield return call;
  }
  ```

- **MUST** let `OperationCanceledException` propagate on cancellation. Catch it only to clean up;
  never log a cancellation as an error.
- **SHOULD** fan out independent I/O with `Task.WhenAll`; **MUST NOT** run concurrent operations on
  the same `DbContext` (it is not thread-safe) — parallelize at the query/service level or use
  separate scopes.
- **MUST NOT** use fire-and-forget tasks in request paths. Durable work goes through the queue/hosted
  service; short bounded work is awaited.
- **SHOULD** model producer/consumer with `Channel<T>` (bounded for backpressure, e.g. SSE fan-out)
  and long-running loops with `BackgroundService.ExecuteAsync(stoppingToken)`; honour the stopping
  token on shutdown and catch per-iteration failures with backoff so one bad iteration cannot kill
  the host.
- **SHOULD NOT** sprinkle `ConfigureAwait(false)` in an ASP.NET Core app (there is no sync context).
  Use it in general-purpose libraries where the consumer's context is unknown.

---

## 8. Nullability & Guards

- **MUST** keep `Nullable` enabled and treat warnings as errors.
- **MUST NOT** suppress nullability with the null-forgiving `!` operator — anywhere, including tests
  and "just this once". Solve it with flow analysis (`is not null`, early return, `required`,
  `?? throw`, `TryGetValue`). If the framework's own signature is genuinely wrong (a known BCL
  edge case), isolate it in one place with a comment explaining why — and do not add a second.
- **MUST** use `is not null` / `is { } value` / `is null` in product code. Reserve `!= null` for
  expression trees (EF Core `Where`) and test-double argument matchers, where patterns do not compile.
- **MUST** guard public entry points:

  ```csharp
  ArgumentNullException.ThrowIfNull(message);
  ArgumentException.ThrowIfNullOrWhiteSpace(name);
  ```

  Use `nameof` in messages/validation; never hardcode a parameter name.
- **SHOULD** use the null-conditional and coalescing operators instead of branching:
  `endpoint?.Name`, `limit.Agent?.Name ?? limit.ApiKey?.Name ?? limit.Project.Name`, and
  `value ?? throw new InvalidOperationException(...)` for required values.
- **SHOULD** prefer pattern matching over manual null/type checks; `switch` arms end with
  `_ => throw new ArgumentOutOfRangeException(...)`.

---

## 9. Expression-Oriented Syntax

The C# features that remove ceremony are the default; verbose forms need a reason.

- **SHOULD** use expression-bodied members for one-liners and simple mappings; use block bodies when
  there is real logic (multiple statements, branching, logging):

  ```csharp
  public AgentDto ToDto(IAgent agent) => new(...);

  public async Task<IAgent> ApplyAsync(IAgent agent, CancellationToken cancellationToken)
  {
      // multi-step logic stays block-bodied
  }
  ```

- **SHOULD** use `switch` expressions with patterns instead of `if`/`else if` chains and type checks:

  ```csharp
  string eventName = evt switch
  {
      ProposalCreatedEvent => "proposal-created",
      ProposalStatusChangedEvent => "proposal-status-changed",
      _ => "unknown",
  };
  ```

- **SHOULD** use property/relational patterns (`is { Count: 0 }`, `is { Count: 1 } ids`,
  `requestedId is not { } id`).
- **SHOULD** use target-typed `new()` when the type is declared on the left, and collection
  expressions (`[]`, `[.. source]`) instead of `new List<T>()`/`Array.Empty`/`.ToList()` noise.
- **MUST** use LINQ method syntax. Query syntax (`from ... select`) is not used.
- **SHOULD** use `var` when the right-hand side makes the type obvious (`var agent = await repo...`),
  and be explicit when it does not — especially for interface-typed locals, where the interface is
  documentation and keeps fakes visible.
- **SHOULD** prefer string interpolation for messages, raw string literals (`"""`) for multi-line
  content (prompts, JSON, SQL), and interpolated raw strings (`$$"""`) when the content itself
  contains braces.
- **SHOULD** use `?? throw` expressions and throw-expressions in switch arms to keep assignments
  direct.
- **MAY** omit braces for a single-statement guard/short-circuit (`if (agent is null) return NotFound();`).
  Multi-statement blocks always use braces.
- **MUST NOT** use `#region` to hide code; split the type instead.

---

## 10. Error Handling & Logging

- **MUST** use exceptions for exceptional conditions; return values are for expected outcomes
  (`null`/`false`/`TryGet`). No result monads (`Result<T>`, `OneOf`) unless a project adopts them
  consistently and documents why.
- **MUST NOT** swallow exceptions. Either handle them meaningfully, or catch → add context → rethrow;
  `catch { }` is forbidden. `catch (Exception ex)` is allowed only at the outermost boundary of a
  loop/host, where it must log with context.
- **MUST** define custom exception types next to the feature they belong to (same file or same
  folder), make them `public` only when they cross a layer boundary, and keep their names explicit
  (`ProviderConnectionException`, `PromptNotFoundException`).
- **MUST** translate exceptions to transport concerns in one place — exception-handling middleware
  with small strategy mappers — not with per-action try/catch in controllers.
- **MUST** return a stable error shape (`{ error: { message, type, errorId } }` or RFC 7807), never a
  raw exception. Stack traces are development-only.
- **MUST NOT** use exceptions for control flow in hot paths (parsing, lookups with a normal
  "not found" outcome).
- **MUST** log through an injected `ILogger<T>`, using **structured message templates**, never
  string interpolation:

  ```csharp
  logger.LogWarning(
      "Attempted to change agent endpoint to the same endpoint (AgentId: {AgentId}, EndpointId: {EndpointId})",
      Id, modelEndpoint.Id);
  ```

  Interpolation destroys the structured fields; the analyzer treats it as a warning for a reason.
- **MUST NOT** log secrets, tokens, credentials, or unnecessary PII. Sanitize user-controlled values
  before logging (strip newlines/control characters).
- **SHOULD NOT** log-and-throw. Log at the boundary that owns the failure; let layers above see the
  exception.
- **MUST** dispose deterministically: `using`/`await using` for everything `IDisposable`/
  `IAsyncDisposable`.

---

## 11. HTTP API Design (ASP.NET Core)

- **MUST** keep controllers thin: bind, guard, delegate to application/domain services, map to DTO,
  return a status. No business rules, no query composition beyond wiring.
- **SHOULD** use MVC controllers for REST surfaces (attributes, filters, consistent
  `ActionResult<T>` signatures); minimal APIs are fine for small standalone endpoints
  (webhooks, MCP, fallbacks). Pick one style per surface and stay consistent.
- **MUST** use `Task<ActionResult<T>>`/`Task<IActionResult>` actions with `[FromQuery]`/`[FromBody]`
  parameters and a `CancellationToken` (no default).
- **MUST NOT** leak domain entities or storage entities: request/response types are records under a
  DTO namespace, mapped explicitly by hand-written mappers (`AgentDtoMapper`). No reflection-based
  auto-mappers.
- **MUST** validate input at the boundary (validation attributes + a single `[ApiController]`) and
  cap collection sizes/limits with named constants.
- **MUST** authorize by attribute at the surface (`[Authorize]`, `[RequiresFeature(...)]`) and enforce
  ownership/resource access in a service or guard, not by scattering checks in actions.
- **SHOULD** return correct semantics: 404 for missing/null, 409 for conflict, 402 for license
  gating, 400 for malformed input, 501 for unimplemented optional features.
- **SHOULD** stream long-running progress/results with SSE or `IAsyncEnumerable`, and push state
  changes with a broadcaster abstraction — never busy-poll.
- **MUST NOT** put `Task.Run` or background work in an action without a durable queue behind it.

---

## 12. Configuration

- **MUST** bind configuration sections to typed records/classes and validate them once at startup
  (a `Validate()` that throws `InvalidOperationException`, or `IValidateOptions` + validate-on-start).
  The app must fail fast, not at first use.
- **MUST NOT** inject `IConfiguration` into services; inject the options object.
- **SHOULD** register defaults in the owning layer and let the host override them (so the layer is
  runnable/testable on its own).
- **MUST** keep secrets out of source; environment/secret providers only. Nothing secret in logs
  (§10) or in tests.
- **SHOULD** use `TimeProvider`/`IClock` and an `IRandom`-style seam instead of `DateTimeOffset.UtcNow`
  and `Random.Shared`, so time and randomness are testable. Never seed anything security-sensitive
  from a seeded test RNG.
- **MUST** use `DateTimeOffset` for timestamps everywhere. `DateTime` is a bug report waiting to
  happen.

---

## 13. Persistence (EF Core)

- **MUST** keep `DbContext` as the unit of work; repositories wrap it and return **domain** entities,
  never storage entities.
- **MUST NOT** let `IQueryable<T>` escape the storage layer. Callers get materialized results or
  `IAsyncEnumerable<T>`.
- **MUST** use `AsNoTracking()` on read paths; track only when the entity will be updated.
- **MUST** push work to the database: filtering, aggregation, `ExecuteUpdateAsync`/`ExecuteDeleteAsync`
  for bulk changes. Verify with `ToQueryString()` that the SQL is server-side and sensible.
- **MUST NOT** load unbounded tables into memory on hot paths; page, aggregate, or stream.
- **MUST** treat storage entities as persistence shapes: `record`, `required` + `init`, extend the
  shared `Entity` base, Guid identity, `Guid` foreign keys — while domain entities hold the related
  domain entity (`IModelEndpoint`, `IReadOnlyCollection<IEvaluator>`).
- **SHOULD** map explicitly between domain and storage via hand-written mappers; no auto-mapping.
- **MUST** make migrations explicit and reviewed; never edit a shipped migration. Guard schema drift
  with a test.
- **MUST** add or extend a scale/perf test when changing a query on a table that grows unboundedly —
  an in-memory test on three rows proves nothing about a query plan.

---

## 14. Testing

- **MUST** have a `Product.<Layer>.Tests` project mirroring each layer, sharing naming and folder
  structure with the source.
- **MUST** use the same DI composition as production. Build a fresh container per test via a shared
  test base (`BaseTest<TModule>`), not a context shared across tests.
- **MUST NOT** use static/shared mutable test state or fixtures; isolation is the default.
- **MUST** fake at seams: register test doubles through DI (`NSubstitute` by default), either as a
  stateless stub or as a single instance the test can assert on.
- **SHOULD** use MSTest for structure, AwesomeAssertions (or FluentAssertions) for assertions, and
  NSubstitute for fakes — one stack per solution; do not mix frameworks.

  ```csharp
  [TestClass]
  public sealed class AgentServiceTests : DomainTest<Module>
  {
      [TestMethod]
      public async Task ChangeEndpoint_UnknownEndpoint_Throws()
      {
          IServiceProvider services = GetServices();
          IAgentService service = services.GetRequiredService<IAgentService>();

          await FluentActions
              .Invoking(() => service.ChangeEndpointAsync(Guid.NewGuid(), CancellationToken.None))
              .Should().ThrowAsync<EntityNotFoundException>();
      }
  }
  ```

- **MUST** name tests `[Subject]_[Condition]_[ExpectedOutcome]`
  (`CreateNew_EmptyName_Throws`, `DiscoverTheories_WhenHalfFail_ProducesProposal`). Class names are
  `public sealed class <Subject>Tests`. Async tests do **not** get an `Async` suffix — the naming is
  about behavior.
- **SHOULD** use `[DataRow]` for parameterized cases; use `DisplayName` when the parameters need
  prose.
- **SHOULD** assert fluently (`value.Should().Be(...)`, `.Should().NotBeNull()`) and assert exceptions
  through `FluentActions.Invoking(...)`. Raw `Assert.*` only where the framework requires it
  (`Assert.Inconclusive` for gated integration tests).
- **MUST** gate tests that need external infrastructure (Docker, Redis) with an explicit skip
  (`Assert.Inconclusive`), and require them via an environment variable in CI — never leave them
  silently skipped.
- **SHOULD** test behavior through the public seam (the container), not private methods; if a test
  needs internals, prefer the interface and `InternalsVisibleTo` over widening the type.
- **SHOULD** use `TestServer`/`WebApplicationFactory` for HTTP-level tests and instantiate controllers
  directly only for pure unit-level cases.

---

## 15. Naming & Documentation

- **MUST** use: PascalCase for types, methods, properties, constants; camelCase for parameters and
  locals; camelCase (no `_`) for private fields.
- **SHOULD** let booleans read as predicates (`isDevelopment`, `ownsDirectory`, `canRetry`).
- **SHOULD** keep domain vocabulary out of infrastructure names: `*Repository`, `*Mapper`,
  `*Entity`, `*Dto`, `*Options`, `*Service`, `*Worker`/`*HostedService`, `*Module`, `*Config`.
- **MUST** document public members with XML docs in the 3-line form (blank line after `<summary>`
  and before `</summary>`):

  ```csharp
  /// <summary>
  /// Creates a new version with the given prompt and tools.
  /// </summary>
  ```

  Explain *why*, not *what* — especially for lifetime choices, workarounds, and concurrency.
- **SHOULD** reference the issue/PR (`#123`) and the relevant docs page in comments for non-obvious
  decisions, so the reason survives the author.
- **MUST** treat documentation as code: when a change invalidates a README/docs page, update it in
  the same change.

---

## 16. Forbidden List (Fast Reference)

Never, without an explicit, documented exception:

- `!` null-forgiving operator
- primary constructors in product code
- service locator / `IServiceProvider` injection
- `new` on a service that could be injected
- `static` mutable state
- `async void`, `.Result`, `.Wait()`, `GetAwaiter().GetResult()`
- blocking a `CancellationToken` API with `CancellationToken.None` in production code
- `lock`/`SemaphoreSlim` held across `await`
- `catch { }` and swallow-and-continue
- interpolated logger messages
- `DateTime` for timestamps
- public mutable properties / setters on domain entities
- `IQueryable<T>` escaping the storage layer
- `var` where the type is unclear to the reviewer
- `#region`
- `#pragma warning disable` for nullable warnings
- comments explaining *what* while the code already says it

## 17. Review Checklist

Before requesting review, confirm:

- [ ] DI: every dependency injected via constructor; lifetimes justified; no container access inside services.
- [ ] Public surface: interfaces + records only; implementations `internal sealed`.
- [ ] Immutability: records, `init`, `with`; no shared mutable state.
- [ ] Async: all I/O awaited; `CancellationToken` flows top to bottom; suffix on non-controller async methods.
- [ ] Errors: guards on entry, custom exceptions mapped centrally, no swallowed exceptions.
- [ ] Logging: structured templates, no secrets, no interpolation.
- [ ] Syntax: expression bodies for one-liners, switch expressions, collection expressions, method-syntax LINQ.
- [ ] Nullability: no `!`; flow analysis used; `is not null` in product code.
- [ ] Tests: added/updated at the right layer; named `[Subject]_[Condition]_[ExpectedOutcome]`; no shared state.
- [ ] Docs/comments updated in the same change; XML docs on new public members.
