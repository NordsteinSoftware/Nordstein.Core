using Autofac;
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Nordstein.Core.Common.Time;
using Nordstein.Core.Licensing.Internal;
using Nordstein.Core.Testing;

namespace Nordstein.Core.Licensing.Tests;

[TestClass]
public sealed class LicenseCheckServiceTests : BaseTest<Module>
{
    private (LicenseCheckService Service, LicenseService License, ILicenseServerClient Server, ILicenseCacheStore Cache, MutableClock Clock)
        Build(LicenseCacheEntry? cached = null, string? jwt = null)
    {
        var clock = new MutableClock(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
        var server = Substitute.For<ILicenseServerClient>();
        var cache = Substitute.For<ILicenseCacheStore>();
        cache.Load().Returns(cached);

        var effectiveJwt = jwt ?? Module.Factory.CreateJwt(tier: TestTierPolicy.Premium);
        var config = Module.Factory.Configuration(effectiveJwt);

        var services = GetServices(builder =>
        {
            builder.RegisterInstance(config).SingleInstance();
            builder.RegisterInstance(clock).As<IClock>().SingleInstance();
            builder.RegisterInstance(server).As<ILicenseServerClient>().SingleInstance();
            builder.RegisterInstance(cache).As<ILicenseCacheStore>().SingleInstance();
        });

        var license = services.GetRequiredService<LicenseService>();
        var checkService = services.GetRequiredService<LicenseCheckService>();

        return (checkService, license, server, cache, clock);
    }

    private static LicenseCheckResult Valid(IClock clock) =>
        new(LicenseCheckResult.Valid, null, null, clock.UtcNow);

    private static LicenseCheckResult Revoked(IClock clock) =>
        new(LicenseCheckResult.Revoked, null, null, clock.UtcNow);

    private static LicenseCheckResult Unknown(IClock clock) =>
        new(LicenseCheckResult.Unknown, null, null, clock.UtcNow);

    [TestMethod]
    public async Task ValidResult_StaysActive()
    {
        var (service, license, server, cache, clock) = Build();
        server.CheckAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Valid(clock));

        await service.RunCheckNowAsync(CancellationToken);

        license.Current.Status.Should().Be(LicenseStatus.Active);
        cache.Received().Save(Arg.Any<LicenseCacheEntry>());
    }

    [TestMethod]
    public async Task ValidResult_WithUpdatedTierAndLimits_AppliesResolvedNames()
    {
        // Raw names from the server response resolve through the policy (case-insensitively);
        // unknown limit names are dropped rather than leaking into the snapshot.
        var (service, license, server, _, clock) = Build();
        var updatedLimits = new Dictionary<string, long>
        {
            ["maxusers"] = 42,
            ["MaxUnicorns"] = 7,
        };
        server.CheckAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new LicenseCheckResult(LicenseCheckResult.Valid, "premium", updatedLimits, clock.UtcNow));

        await service.RunCheckNowAsync(CancellationToken);

        license.Current.Tier.Should().Be(TestTierPolicy.Premium);
        license.Current.Limits[TestTierPolicy.MaxUsers].Should().Be(42);
        license.Current.Limits.Should().NotContainKey("MaxUnicorns");
    }

    [TestMethod]
    public async Task RevokedResult_DowngradesToExpired()
    {
        var (service, license, server, _, clock) = Build();
        server.CheckAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Revoked(clock));

        await service.RunCheckNowAsync(CancellationToken);

        license.Current.Status.Should().Be(LicenseStatus.Expired);
        license.Current.Tier.Should().Be(TestTierPolicy.Basic);
        license.HasFeature(TestTierPolicy.Analytics).Should().BeFalse();
    }

    [TestMethod]
    public async Task Unreachable_6Days_StaysActive()
    {
        var lastOk = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var (service, license, server, _, clock) = Build(new LicenseCacheEntry("jti", lastOk, "valid"));
        server.CheckAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Unknown(clock));

        clock.Advance(TimeSpan.FromDays(6));
        await service.RunCheckNowAsync(CancellationToken);

        license.Current.Status.Should().Be(LicenseStatus.Active);
    }

    [TestMethod]
    public async Task Unreachable_7Days_EntersGrace()
    {
        var lastOk = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var (service, license, server, _, clock) = Build(new LicenseCacheEntry("jti", lastOk, "valid"));
        server.CheckAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Unknown(clock));

        clock.Advance(TimeSpan.FromDays(7));
        await service.RunCheckNowAsync(CancellationToken);

        license.Current.Status.Should().Be(LicenseStatus.Grace);
        license.Current.GracePeriodEndsAt.Should().NotBeNull();
    }

    [TestMethod]
    public async Task Unreachable_14Days_DowngradesToFallback()
    {
        var lastOk = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var (service, license, server, _, clock) = Build(new LicenseCacheEntry("jti", lastOk, "valid"));
        server.CheckAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Unknown(clock));

        clock.Advance(TimeSpan.FromDays(14));
        await service.RunCheckNowAsync(CancellationToken);

        license.Current.Status.Should().Be(LicenseStatus.Free);
        license.Current.Tier.Should().Be(TestTierPolicy.Basic);
    }

    [TestMethod]
    public async Task Unreachable_NoCache_14Days_DowngradesToFallback()
    {
        // A deployment that has never reached the license server (no cache entry) must still
        // degrade through Grace to the fallback tier once the offline grace window elapses,
        // measured from service start (clock initial time captured in Build).
        var (service, license, server, _, clock) = Build(cached: null);
        server.CheckAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Unknown(clock));

        clock.Advance(TimeSpan.FromDays(14));
        await service.RunCheckNowAsync(CancellationToken);

        license.Current.Status.Should().Be(LicenseStatus.Free);
        license.Current.Tier.Should().Be(TestTierPolicy.Basic);
    }

    [TestMethod]
    public async Task ExecuteAsync_LicenseActivatedAtRuntime_TriggersServerCheck()
    {
        // Boot without any license: the loop must idle (no jti to check) but react to a
        // license activated later at runtime (e.g. a stored license applied by the product).
        var clock = new MutableClock(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
        var server = Substitute.For<ILicenseServerClient>();
        var cache = Substitute.For<ILicenseCacheStore>();
        cache.Load().Returns((LicenseCacheEntry?)null);

        var services = GetServices(builder =>
        {
            builder.RegisterInstance(Module.Factory.Configuration()).SingleInstance();
            builder.RegisterInstance(clock).As<IClock>().SingleInstance();
            builder.RegisterInstance(server).As<ILicenseServerClient>().SingleInstance();
            builder.RegisterInstance(cache).As<ILicenseCacheStore>().SingleInstance();
        });

        var license = services.GetRequiredService<LicenseService>();
        var checkService = services.GetRequiredService<LicenseCheckService>();
        var activator = services.GetRequiredService<ILicenseActivator>();

        var checkPerformed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        server.CheckAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                checkPerformed.TrySetResult();
                return Valid(clock);
            });

        await checkService.StartAsync(CancellationToken);
        try
        {
            server.ReceivedCalls().Should().BeEmpty("no license is active yet");

            activator.Activate(Module.Factory.CreateJwt(tier: TestTierPolicy.Premium), LicenseSource.Stored);

            await checkPerformed.Task.WaitAsync(TimeSpan.FromSeconds(10), CancellationToken);
        }
        finally
        {
            await checkService.StopAsync(CancellationToken);
        }

        license.Current.Status.Should().Be(LicenseStatus.Active);
        license.Current.Tier.Should().Be(TestTierPolicy.Premium);
    }

    [TestMethod]
    public async Task ForceRefresh_TriggersImmediateCheck()
    {
        var (_, license, server, _, clock) = Build();
        server.CheckAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Revoked(clock));

        await license.ForceRefreshAsync(CancellationToken);

        await server.Received(1).CheckAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        license.Current.Status.Should().Be(LicenseStatus.Expired);
    }

    [TestMethod]
    public async Task OfflineLicense_ForcedRefresh_NeverContactsServer_StaysActive()
    {
        // An offline-only key (offline: true) is exempt from the revocation check entirely — the
        // whole point for air-gapped installs. A forced refresh must make zero server calls and
        // never write the offline-grace cache.
        var offlineJwt = Module.Factory.CreateJwt(tier: TestTierPolicy.Premium, offline: true);
        var (service, license, server, cache, _) = Build(jwt: offlineJwt);

        license.Current.Offline.Should().BeTrue("the offline key activated cleanly");

        await service.RunCheckNowAsync(CancellationToken);

        license.Current.Status.Should().Be(LicenseStatus.Active);
        license.Current.Tier.Should().Be(TestTierPolicy.Premium);
        server.ReceivedCalls().Should().BeEmpty("offline-only licenses never contact the server");
        cache.DidNotReceive().Save(Arg.Any<LicenseCacheEntry>());
    }

    [TestMethod]
    public async Task OfflineLicense_PastExpiry_DowngradesToExpired_WithoutServer()
    {
        // The offline key is valid at activation (the validator's lifetime check uses the real
        // clock) but the injected clock has advanced past its exp. With no server to ask, expiry
        // is the only thing that ends an offline key — and it is enforced locally.
        var offlineJwt = Module.Factory.CreateJwt(
            tier: TestTierPolicy.Premium,
            offline: true,
            expires: DateTimeOffset.UtcNow.AddMinutes(5));

        var clock = new MutableClock(DateTimeOffset.UtcNow.AddDays(10));
        var server = Substitute.For<ILicenseServerClient>();
        var cache = Substitute.For<ILicenseCacheStore>();

        var services = GetServices(builder =>
        {
            builder.RegisterInstance(Module.Factory.Configuration(offlineJwt)).SingleInstance();
            builder.RegisterInstance(clock).As<IClock>().SingleInstance();
            builder.RegisterInstance(server).As<ILicenseServerClient>().SingleInstance();
            builder.RegisterInstance(cache).As<ILicenseCacheStore>().SingleInstance();
        });

        var license = services.GetRequiredService<LicenseService>();
        var service = services.GetRequiredService<LicenseCheckService>();

        license.Current.Offline.Should().BeTrue("the offline key activated cleanly");

        await service.RunCheckNowAsync(CancellationToken);

        license.Current.Status.Should().Be(LicenseStatus.Expired);
        license.Current.Tier.Should().Be(TestTierPolicy.Basic);
        server.ReceivedCalls().Should().BeEmpty("an expired offline key still never contacts the server");
    }

    [TestMethod]
    public async Task ExecuteAsync_OfflineLicense_PeriodicLoopNeverContactsServer()
    {
        // The contract's core requirement is about the PERIODIC check, not just the forced-refresh
        // path: an offline key must make the background loop skip the server entirely. Run the
        // actual loop (StartAsync) and assert zero server calls. (A normal key would contact the
        // server immediately on the first iteration — see ExecuteAsync_LicenseActivatedAtRuntime.)
        var clock = new MutableClock(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
        var server = Substitute.For<ILicenseServerClient>();
        var cache = Substitute.For<ILicenseCacheStore>();
        cache.Load().Returns((LicenseCacheEntry?)null);

        var offlineJwt = Module.Factory.CreateJwt(tier: TestTierPolicy.Premium, offline: true);
        var services = GetServices(builder =>
        {
            builder.RegisterInstance(Module.Factory.Configuration(offlineJwt)).SingleInstance();
            builder.RegisterInstance(clock).As<IClock>().SingleInstance();
            builder.RegisterInstance(server).As<ILicenseServerClient>().SingleInstance();
            builder.RegisterInstance(cache).As<ILicenseCacheStore>().SingleInstance();
        });

        var license = services.GetRequiredService<LicenseService>();
        var checkService = services.GetRequiredService<LicenseCheckService>();

        license.Current.Offline.Should().BeTrue("the offline key activated cleanly");

        await checkService.StartAsync(CancellationToken);
        try
        {
            // The first loop iteration runs synchronously up to its delay; give it a moment anyway.
            await Task.Delay(100, CancellationToken);

            server.ReceivedCalls().Should().BeEmpty("the periodic loop must skip the check for offline keys");
            license.Current.Status.Should().Be(LicenseStatus.Active);
        }
        finally
        {
            await checkService.StopAsync(CancellationToken);
        }
    }
}
