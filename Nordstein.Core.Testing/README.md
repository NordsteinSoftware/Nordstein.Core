# Nordstein.Core.Testing

Shared MSTest harness for Nordstein applications.

## `BaseTest<TModule>`

Derive from it and every test gets a fresh Autofac container, built from
the harness's own module plus the module under test, and disposed automatically:

```csharp
[TestClass]
public sealed class ThingTests : BaseTest<MyProduct.Application.Module>
{
    [TestMethod]
    public async Task DoesTheThing()
    {
        IServiceProvider services = GetServices();
        var subject = services.GetRequiredService<IThing>();

        (await subject.DoAsync(CancellationToken)).Should().BeTrue();
    }
}
```

- `GetServices(Action<ContainerBuilder>?)` builds a container registered for per-test cleanup.
- `BuildContainer(...)` is the static escape hatch for an expensive `[ClassInitialize]` fixture
  that holds no test data; the caller owns disposal.
- `ConfigureContainer` is the per-class override point for substitutes.
- `CancellationToken` comes from the `TestContext`, so a test that hangs is cancelled by the
  runner rather than running to the timeout.

The package brings AwesomeAssertions and NSubstitute along transitively — a consuming test
project does not reference them itself.

## Shared fakes

| Type | For |
|---|---|
| `Time.ManualClock` | One instant served as both `IClock` and `TimeProvider`; `Advance`, `SetUtcNow` (backward = clock skew), timers that fire only inside an advance, `WaitForPendingTimerAsync`. |
| `Http.ScriptedHttpMessageHandler` | Scripted answers under an `HttpClient`: `Respond`, `Throw`, `TimeOut`, `Hang`, `RepeatLast`; `Requests`, `RemainingSteps`. |
| `Http.NoNetworkHttpMessageHandler` | Proves no outbound call: every send throws and is kept in `Attempts`. |
| `Logging.RecordingLoggerProvider`, `Logging.RecordingLogger<T>` | Every enabled log entry with category, level, event id, message, exception and structured properties. |
| `Certificates.ThrowawayCertificateAuthority` | An in-memory CA issuing short-lived TLS server certificates as `X509Certificate2` and PEM. |

All are safe for concurrent use; the XML docs state each contract. See `docs/testing.md` in the
repository for how to wire them.

## Architecture fences (`Fences`)

The reflection and metadata toolkit for fence tests: `InjectedParameters`, `ParametersMentioning`
and `Mentions` (the parameter walk), `StaticMembers` (the static-member scan),
`InternalTypesOutsideInternalNamespaces`, `ReferencedMemberNames` and `ReferencedTypeNames` (the
metadata reference scan), and `SolutionFile` / `SolutionProject` (the solution-derived assembly
list). Every scan returns sorted, distinct lines to compare with an allowlist; prove each fence can
fail with a planted violating type in its own test project.
