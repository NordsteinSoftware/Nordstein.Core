using Autofac;
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Nordstein.Core.Common.Time;
using Nordstein.Core.Licensing.Internal;
using Nordstein.Core.Testing;

namespace Nordstein.Core.Licensing.Tests;

/// <summary>
/// Fills the <see cref="LicenseCheckService"/> branches not covered by <c>LicenseCheckServiceTests</c>:
/// the server-check-disabled short-circuit, the no-active-license no-op, restoring Active from
/// Grace, an unresolvable server tier, a transient failure inside the periodic loop, waking on a
/// runtime snapshot change, and the wake delay for an offline key without an expiry.
/// </summary>
[TestClass]
public sealed class LicenseCheckServiceEdgeCaseTests : BaseTest<Module>
{
    private (IServiceProvider Services, ILicenseServerClient Server, ILicenseCacheStore Cache) Compose(
        LicensingConfiguration config,
        MutableClock clock,
        LicenseCacheEntry? cached = null)
    {
        var server = Substitute.For<ILicenseServerClient>();
        var cache = Substitute.For<ILicenseCacheStore>();
        cache.Load().Returns(cached);

        var services = GetServices(builder =>
        {
            builder.RegisterInstance(config).SingleInstance();
            builder.RegisterInstance(clock).As<IClock>().SingleInstance();
            builder.RegisterInstance(server).As<ILicenseServerClient>().SingleInstance();
            builder.RegisterInstance(cache).As<ILicenseCacheStore>().SingleInstance();
        });

        return (services, server, cache);
    }

    private static MutableClock Clock() => new(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));

    private static LicenseCheckResult Valid(IClock clock) => new(LicenseCheckResult.Valid, null, null, clock.UtcNow);

    private static LicenseCheckResult Unknown(IClock clock) => new(LicenseCheckResult.Unknown, null, null, clock.UtcNow);

    [TestMethod]
    public async Task ExecuteAsync_ServerCheckDisabled_KeepsStartupSnapshotAndMakesNoServerCalls()
    {
        var clock = Clock();
        var jwt = Module.Factory.CreateJwt(tier: TestTierPolicy.Premium);
        var config = Module.Factory.Configuration(jwt) with { ServerCheckEnabled = false };
        var (services, server, _) = Compose(config, clock);
        var license = services.GetRequiredService<LicenseService>();
        var checkService = services.GetRequiredService<LicenseCheckService>();

        await checkService.StartAsync(CancellationToken);
        try
        {
            await Task.Delay(50, CancellationToken);

            server.ReceivedCalls().Should().BeEmpty("server checks are disabled");
            license.Current.Status.Should().Be(LicenseStatus.Active);
            license.Current.Tier.Should().Be(TestTierPolicy.Premium);
        }
        finally
        {
            await checkService.StopAsync(CancellationToken);
        }
    }

    [TestMethod]
    public async Task RunCheckNow_NoActiveLicense_DoesNothing()
    {
        // No configured JWT ⇒ the snapshot has no jti; a forced re-check must return without
        // contacting the server or changing anything.
        var clock = Clock();
        var config = Module.Factory.Configuration(jwt: null);
        var (services, server, _) = Compose(config, clock);
        var license = services.GetRequiredService<LicenseService>();
        var checkService = services.GetRequiredService<LicenseCheckService>();

        license.Current.Jti.Should().BeNull();

        await checkService.RunCheckNowAsync(CancellationToken);

        server.ReceivedCalls().Should().BeEmpty("there is no license to check");
        license.Current.Status.Should().Be(LicenseStatus.Free);
    }

    [TestMethod]
    public async Task RunCheckNow_ValidAfterGrace_RestoresActiveAndClearsGraceWindow()
    {
        var lastOk = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var clock = Clock();
        var config = Module.Factory.Configuration(Module.Factory.CreateJwt(tier: TestTierPolicy.Premium));
        var (services, server, _) = Compose(config, clock, new LicenseCacheEntry("jti", lastOk, "valid"));
        var license = services.GetRequiredService<LicenseService>();
        var checkService = services.GetRequiredService<LicenseCheckService>();

        // First: unreachable at +7 days drives the license into Grace.
        server.CheckAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Unknown(clock));
        clock.Advance(TimeSpan.FromDays(7));
        await checkService.RunCheckNowAsync(CancellationToken);
        license.Current.Status.Should().Be(LicenseStatus.Grace);

        // Then: a Valid result restores Active and clears the grace window.
        server.CheckAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Valid(clock));
        await checkService.RunCheckNowAsync(CancellationToken);

        license.Current.Status.Should().Be(LicenseStatus.Active);
        license.Current.Tier.Should().Be(TestTierPolicy.Premium);
        license.Current.GracePeriodEndsAt.Should().BeNull();
    }

    [TestMethod]
    public async Task RunCheckNow_ValidWithUnresolvableUpdatedTier_KeepsCurrentTierAndFeatures()
    {
        // The server reports a tier the policy does not know; it is ignored and the current tier
        // (and its features) are kept rather than downgraded or crashed.
        var clock = Clock();
        var config = Module.Factory.Configuration(Module.Factory.CreateJwt(tier: TestTierPolicy.Premium));
        var (services, server, _) = Compose(config, clock);
        var license = services.GetRequiredService<LicenseService>();
        var checkService = services.GetRequiredService<LicenseCheckService>();

        server.CheckAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new LicenseCheckResult(LicenseCheckResult.Valid, "Platinum", null, clock.UtcNow));

        await checkService.RunCheckNowAsync(CancellationToken);

        license.Current.Status.Should().Be(LicenseStatus.Active);
        license.Current.Tier.Should().Be(TestTierPolicy.Premium, "an unknown server tier is ignored");
        license.HasFeature(TestTierPolicy.Analytics).Should().BeTrue("the current features are retained");
    }

    [TestMethod]
    public async Task ExecuteAsync_TransientCheckFailure_IsSwallowed_LoopKeepsRunning()
    {
        // A throw inside the periodic check must never escape the loop (that would stop the host);
        // it is logged and folded away, and the license is left untouched.
        var clock = Clock();
        var config = Module.Factory.Configuration(Module.Factory.CreateJwt(tier: TestTierPolicy.Premium));
        var (services, server, _) = Compose(config, clock);
        var license = services.GetRequiredService<LicenseService>();
        var checkService = services.GetRequiredService<LicenseCheckService>();

        var attempted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        server.CheckAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns<Task<LicenseCheckResult>>(_ =>
            {
                attempted.TrySetResult();
                throw new InvalidOperationException("transient boom");
            });

        await checkService.StartAsync(CancellationToken);
        try
        {
            await attempted.Task.WaitAsync(TimeSpan.FromSeconds(10), CancellationToken);

            license.Current.Status.Should().Be(LicenseStatus.Active, "a transient failure leaves the license as-is");
            license.Current.Tier.Should().Be(TestTierPolicy.Premium);
        }
        finally
        {
            await checkService.StopAsync(CancellationToken);
        }
    }

    [TestMethod]
    public async Task ExecuteAsync_SnapshotChangesWhileParked_WakesAndRechecks()
    {
        // After the first check the loop parks; a runtime license change must wake it (via the
        // Changed event) and drive another check rather than waiting a full interval.
        var clock = Clock();
        var config = Module.Factory.Configuration(Module.Factory.CreateJwt(tier: TestTierPolicy.Premium));
        var (services, server, _) = Compose(config, clock);
        var license = services.GetRequiredService<LicenseService>();
        var checkService = services.GetRequiredService<LicenseCheckService>();
        var activator = services.GetRequiredService<ILicenseActivator>();

        var calls = 0;
        var firstCheck = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondCheck = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        server.CheckAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                if (Interlocked.Increment(ref calls) == 1)
                    firstCheck.TrySetResult();
                else
                    secondCheck.TrySetResult();
                return Valid(clock);
            });

        await checkService.StartAsync(CancellationToken);
        try
        {
            await firstCheck.Task.WaitAsync(TimeSpan.FromSeconds(10), CancellationToken);

            // The loop is now parked on its interval delay; changing the license must wake it.
            activator.Activate(
                Module.Factory.CreateJwt(tier: TestTierPolicy.Premium, subject: "changed@example.com"),
                LicenseSource.Stored);

            await secondCheck.Task.WaitAsync(TimeSpan.FromSeconds(10), CancellationToken);
        }
        finally
        {
            await checkService.StopAsync(CancellationToken);
        }

        calls.Should().BeGreaterThanOrEqualTo(2, "the runtime change triggered a fresh check");
    }

    [TestMethod]
    public async Task ExecuteAsync_OfflineOverrideWithoutExpiry_ParksWithoutContactingServer()
    {
        // An offline override snapshot with no expiry is a perpetual air-gapped key: the loop must
        // enforce nothing (no expiry) and never contact the server, using the plain interval wake.
        var clock = Clock();
        var overrideSnapshot = LicenseSnapshot.Fallback(Module.Policy) with
        {
            Tier = TestTierPolicy.Premium,
            Status = LicenseStatus.Active,
            Jti = "offline-perpetual",
            Offline = true,
            ExpiresAt = null,
            Source = LicenseSource.Override,
        };
        var config = Module.Factory.Configuration() with { OverrideSnapshot = overrideSnapshot };
        var (services, server, cache) = Compose(config, clock);
        var license = services.GetRequiredService<LicenseService>();
        var checkService = services.GetRequiredService<LicenseCheckService>();

        license.Current.Offline.Should().BeTrue();
        license.Current.ExpiresAt.Should().BeNull();

        await checkService.StartAsync(CancellationToken);
        try
        {
            await Task.Delay(50, CancellationToken);

            server.ReceivedCalls().Should().BeEmpty("an offline key never contacts the server");
            cache.DidNotReceive().Save(Arg.Any<LicenseCacheEntry>());
            license.Current.Status.Should().Be(LicenseStatus.Active);
        }
        finally
        {
            await checkService.StopAsync(CancellationToken);
        }
    }
}
