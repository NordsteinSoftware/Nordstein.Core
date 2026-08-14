using Autofac;
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Nordstein.Core.Common.Async;
using Nordstein.Core.Licensing.Internal;
using Nordstein.Core.Testing;

namespace Nordstein.Core.Licensing.Tests;

[TestClass]
public sealed class LicenseStartupGateTests : BaseTest<Module>
{
    /// <summary>
    /// Constructs a <see cref="LicenseService"/> in isolation with the given configuration,
    /// bypassing AutoActivate so we can assert directly on startup-resolution outcomes.
    /// </summary>
    private static LicenseService Create(LicensingConfiguration config)
    {
        var policy = new TestTierPolicy();
        var validator = new JwtLicenseValidator(config, policy, NullLogger<JwtLicenseValidator>.Instance);
        var resolver = new ConfiguredLicenseResolver(config, validator, policy, NullLogger<ConfiguredLicenseResolver>.Instance);
        var trigger = Substitute.For<ILicenseRefreshTrigger>();
        return new LicenseService(resolver, NoOpLock(), () => trigger, NullLogger<LicenseService>.Instance);
    }

    private static IAsyncLock NoOpLock()
    {
        var gate = Substitute.For<IAsyncLock>();
        gate.Lock(Arg.Any<object>()).Returns(Substitute.For<IDisposable>());
        gate.LockAsync(Arg.Any<object>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(Substitute.For<IDisposable>()));
        return gate;
    }

    [TestMethod]
    public void Construct_NoJwt_RunsFallback()
    {
        var service = Create(Module.Factory.Configuration(jwt: null));

        service.Current.Tier.Should().Be(TestTierPolicy.Basic);
        service.Current.Status.Should().Be(LicenseStatus.Free);
        service.Current.Source.Should().Be(LicenseSource.None);
    }

    [TestMethod]
    public void Construct_ValidJwt_RunsActive()
    {
        var service = Create(Module.Factory.Configuration(Module.Factory.CreateJwt(tier: TestTierPolicy.Premium)));

        service.Current.Tier.Should().Be(TestTierPolicy.Premium);
        service.Current.Status.Should().Be(LicenseStatus.Active);
        service.Current.Source.Should().Be(LicenseSource.Environment);
        service.HasFeature(TestTierPolicy.Sso).Should().BeTrue();
    }

    [TestMethod]
    public void Construct_MalformedJwt_DegradesToInvalidFallback()
        => AssertInvalid(Create(Module.Factory.Configuration("garbage")));

    [TestMethod]
    public void Construct_BadSignature_DegradesToInvalidFallback()
        => AssertInvalid(Create(Module.Factory.Configuration(Module.Factory.CreateJwt(sign: false))));

    [TestMethod]
    public void Construct_WrongIssuer_DegradesToInvalidFallback()
        => AssertInvalid(Create(Module.Factory.Configuration(Module.Factory.CreateJwt(issuer: "https://evil.example.com"))));

    [TestMethod]
    public void Construct_WrongAudience_DegradesToInvalidFallback()
        => AssertInvalid(Create(Module.Factory.Configuration(Module.Factory.CreateJwt(audience: "nope"))));

    [TestMethod]
    public void Construct_ExpiredJwt_DegradesToInvalidFallback()
        => AssertInvalid(Create(Module.Factory.Configuration(Module.Factory.CreateJwt(expires: DateTimeOffset.UtcNow.AddMinutes(-1)))));

    /// <summary>
    /// An invalid configured license must never crash the host: it boots with fallback-tier
    /// entitlements, LicenseStatus.Invalid, and the rejection reason for the product UI.
    /// </summary>
    private static void AssertInvalid(LicenseService service)
    {
        service.Current.Tier.Should().Be(TestTierPolicy.Basic);
        service.Current.Status.Should().Be(LicenseStatus.Invalid);
        service.Current.Source.Should().Be(LicenseSource.Environment);
        service.Current.InvalidReason.Should().NotBeNullOrEmpty();
        service.HasFeature(TestTierPolicy.Sso).Should().BeFalse();
    }

    [TestMethod]
    public async Task ForceRefresh_DelegatesToRefreshTrigger()
    {
        var trigger = Substitute.For<ILicenseRefreshTrigger>();
        var policy = new TestTierPolicy();
        var config = Module.Factory.Configuration(Module.Factory.CreateJwt());
        var validator = new JwtLicenseValidator(config, policy, NullLogger<JwtLicenseValidator>.Instance);
        var resolver = new ConfiguredLicenseResolver(config, validator, policy, NullLogger<ConfiguredLicenseResolver>.Instance);
        var service = new LicenseService(resolver, NoOpLock(), () => trigger, NullLogger<LicenseService>.Instance);

        await service.ForceRefreshAsync(CancellationToken);

        await trigger.Received(1).RunCheckNowAsync(Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public void GetServices_NoLicense_LicenseServiceResolvesToFallback()
    {
        var services = GetServices();
        var licenseService = services.GetRequiredService<ILicenseService>();

        licenseService.Current.Status.Should().Be(LicenseStatus.Free);
    }

    [TestMethod]
    public void GetServices_WithPremiumJwt_LicenseServiceResolvesToActive()
    {
        var jwt = Module.Factory.CreateJwt(tier: TestTierPolicy.Premium);
        var config = Module.Factory.Configuration(jwt);

        var services = GetServices(builder =>
            builder.RegisterInstance(config).SingleInstance());

        var licenseService = services.GetRequiredService<ILicenseService>();
        licenseService.Current.Status.Should().Be(LicenseStatus.Active);
        licenseService.HasFeature(TestTierPolicy.Analytics).Should().BeTrue();
    }
}
