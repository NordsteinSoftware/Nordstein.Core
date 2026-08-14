using Autofac;
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Nordstein.Core.Testing;

namespace Nordstein.Core.Licensing.Tests;

[TestClass]
public sealed class LicenseActivatorTests : BaseTest<Module>
{
    [TestMethod]
    public void Validate_InvalidJwt_Throws()
    {
        var services = GetServices();
        var activator = services.GetRequiredService<ILicenseActivator>();

        var action = () => activator.Validate("garbage");

        action.Should().Throw<InvalidLicenseException>()
            .Which.Reason.Should().Be(InvalidLicenseReason.Malformed);
    }

    [TestMethod]
    public void Validate_ValidJwt_ReturnsSnapshotWithoutApplying()
    {
        var services = GetServices();
        var activator = services.GetRequiredService<ILicenseActivator>();
        var license = services.GetRequiredService<ILicenseService>();

        var snapshot = activator.Validate(Module.Factory.CreateJwt(tier: TestTierPolicy.Premium));

        snapshot.Tier.Should().Be(TestTierPolicy.Premium);
        license.Current.Tier.Should().Be(TestTierPolicy.Basic, "validation must not change the active license");
    }

    [TestMethod]
    public void Activate_ValidJwt_AppliesSnapshotWithSource()
    {
        var services = GetServices();
        var activator = services.GetRequiredService<ILicenseActivator>();
        var license = services.GetRequiredService<ILicenseService>();

        activator.Activate(Module.Factory.CreateJwt(tier: TestTierPolicy.Premium), LicenseSource.Stored);

        license.Current.Tier.Should().Be(TestTierPolicy.Premium);
        license.Current.Status.Should().Be(LicenseStatus.Active);
        license.Current.Source.Should().Be(LicenseSource.Stored);
    }

    [TestMethod]
    public void Activate_InvalidJwt_ThrowsAndKeepsCurrentLicense()
    {
        var services = GetServices();
        var activator = services.GetRequiredService<ILicenseActivator>();
        var license = services.GetRequiredService<ILicenseService>();
        activator.Activate(Module.Factory.CreateJwt(tier: TestTierPolicy.Premium), LicenseSource.Stored);

        var action = () => activator.Activate("garbage", LicenseSource.Stored);

        action.Should().Throw<InvalidLicenseException>();
        license.Current.Tier.Should().Be(TestTierPolicy.Premium, "a rejected JWT must not replace the active license");
    }

    [TestMethod]
    public void ActivateOrInvalid_ExpiredJwt_AppliesInvalidFallbackSnapshot()
    {
        var services = GetServices();
        var activator = services.GetRequiredService<ILicenseActivator>();
        var license = services.GetRequiredService<ILicenseService>();

        var expired = Module.Factory.CreateJwt(expires: DateTimeOffset.UtcNow.AddMinutes(-1));
        var snapshot = activator.ActivateOrInvalid(expired, LicenseSource.Stored);

        snapshot.Status.Should().Be(LicenseStatus.Invalid);
        license.Current.Tier.Should().Be(TestTierPolicy.Basic);
        license.Current.Status.Should().Be(LicenseStatus.Invalid);
        license.Current.Source.Should().Be(LicenseSource.Stored);
        license.Current.InvalidReason.Should().NotBeNullOrEmpty();
    }

    [TestMethod]
    public void ActivateConfigured_NoEnvironmentJwt_RevertsToFallback()
    {
        var services = GetServices();
        var activator = services.GetRequiredService<ILicenseActivator>();
        var license = services.GetRequiredService<ILicenseService>();
        activator.Activate(Module.Factory.CreateJwt(tier: TestTierPolicy.Premium), LicenseSource.Stored);

        activator.ActivateConfigured();

        license.Current.Tier.Should().Be(TestTierPolicy.Basic);
        license.Current.Source.Should().Be(LicenseSource.None);
    }

    [TestMethod]
    public void ActivateConfigured_WithEnvironmentJwt_RevertsToEnvironmentLicense()
    {
        var config = Module.Factory.Configuration(Module.Factory.CreateJwt(subject: "env@example.com"));
        var services = GetServices(builder => builder.RegisterInstance(config).SingleInstance());
        var activator = services.GetRequiredService<ILicenseActivator>();
        var license = services.GetRequiredService<ILicenseService>();
        activator.Activate(Module.Factory.CreateJwt(subject: "stored@example.com"), LicenseSource.Stored);

        activator.ActivateConfigured();

        license.Current.CustomerEmail.Should().Be("env@example.com");
        license.Current.Source.Should().Be(LicenseSource.Environment);
    }

    [TestMethod]
    public void ActivateConfigured_WithOverrideSnapshot_AdoptsOverrideVerbatim()
    {
        // Kiosk/demo deployments pin a pre-resolved snapshot that bypasses JWT validation.
        var overrideSnapshot = LicenseSnapshot.Fallback(Module.Policy) with
        {
            Tier = TestTierPolicy.Premium,
            Status = LicenseStatus.Active,
            Source = LicenseSource.Override,
            Features = new HashSet<string> { TestTierPolicy.Analytics },
        };
        var config = Module.Factory.Configuration() with { OverrideSnapshot = overrideSnapshot };
        var services = GetServices(builder => builder.RegisterInstance(config).SingleInstance());
        var license = services.GetRequiredService<ILicenseService>();

        license.Current.Should().Be(overrideSnapshot);
        license.HasFeature(TestTierPolicy.Analytics).Should().BeTrue();
    }

    [TestMethod]
    public void Activate_RaisesChangedEvent()
    {
        var services = GetServices();
        var activator = services.GetRequiredService<ILicenseActivator>();
        var license = services.GetRequiredService<ILicenseService>();
        var raised = false;
        license.Changed += () => raised = true;

        activator.Activate(Module.Factory.CreateJwt(tier: TestTierPolicy.Premium), LicenseSource.Stored);

        raised.Should().BeTrue();
    }
}
