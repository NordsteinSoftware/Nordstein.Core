using Autofac;
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Nordstein.Core.Common.Async;
using Nordstein.Core.Licensing.Internal;
using Nordstein.Core.Testing;

namespace Nordstein.Core.Licensing.Tests;

/// <summary>
/// Targeted coverage for <see cref="LicenseService"/> members not exercised elsewhere:
/// <c>GetLimit</c> (present and missing), and the by-value limit comparison used to decide whether
/// applying a snapshot actually changed the license.
/// </summary>
[TestClass]
public sealed class LicenseServiceUnitTests : BaseTest<Module>
{
    private static LicenseService CreateFallbackService()
    {
        var policy = new TestTierPolicy();
        var config = Module.Factory.Configuration(jwt: null);
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
    public void GetLimit_PresentLimit_ReturnsConfiguredValue()
    {
        var jwt = Module.Factory.CreateJwt(tier: TestTierPolicy.Premium);
        var services = GetServices(builder =>
            builder.RegisterInstance(Module.Factory.Configuration(jwt)).SingleInstance());
        var license = services.GetRequiredService<ILicenseService>();

        // Premium grants unlimited (long.MaxValue) on both default limits.
        license.GetLimit(TestTierPolicy.MaxUsers).Should().Be(long.MaxValue);
    }

    [TestMethod]
    public void GetLimit_UnknownLimit_ReturnsZero()
    {
        var services = GetServices();
        var license = services.GetRequiredService<ILicenseService>();

        license.GetLimit("NoSuchLimit").Should().Be(0);
    }

    [TestMethod]
    public void ApplySnapshot_LimitsDifferInCount_TreatedAsChanged_RaisesChanged()
    {
        // Two snapshots identical in every scalar field and Features (same set reference), differing
        // only in the number of limits, must compare as *different* — exercising the count-mismatch
        // arm of the by-value limit comparison.
        var policy = new TestTierPolicy();
        var sharedFeatures = new HashSet<string>();
        var baseSnapshot = LicenseSnapshot.Fallback(policy);
        var twoLimits = baseSnapshot with
        {
            Features = sharedFeatures,
            Limits = new Dictionary<string, long> { ["A"] = 1, ["B"] = 2 },
        };
        var oneLimit = baseSnapshot with
        {
            Features = sharedFeatures,
            Limits = new Dictionary<string, long> { ["A"] = 1 },
        };

        var service = CreateFallbackService();
        service.ApplySnapshot(twoLimits);

        var raised = 0;
        service.Changed += () => raised++;
        service.ApplySnapshot(oneLimit);

        raised.Should().Be(1, "a different limit count is a real change");
        service.GetLimit("A").Should().Be(1);
        service.GetLimit("B").Should().Be(0);
    }

    [TestMethod]
    public void ApplySnapshot_IdenticalLimitsRebuiltDictionary_TreatedAsUnchanged_NoChanged()
    {
        // The by-value comparison must NOT fire Changed when a rebuilt-but-equivalent limit
        // dictionary is applied — the guard that stops every successful poll from looking "changed".
        var policy = new TestTierPolicy();
        var sharedFeatures = new HashSet<string>();
        var baseSnapshot = LicenseSnapshot.Fallback(policy) with { Features = sharedFeatures };
        var first = baseSnapshot with { Limits = new Dictionary<string, long> { ["A"] = 1, ["B"] = 2 } };
        var equivalent = baseSnapshot with { Limits = new Dictionary<string, long> { ["A"] = 1, ["B"] = 2 } };

        var service = CreateFallbackService();
        service.ApplySnapshot(first);

        var raised = 0;
        service.Changed += () => raised++;
        service.ApplySnapshot(equivalent);

        raised.Should().Be(0, "an equivalent rebuilt dictionary is not a change");
    }
}
