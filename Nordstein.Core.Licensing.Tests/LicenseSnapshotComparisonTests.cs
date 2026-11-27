using AwesomeAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Nordstein.Core.Common.Async;
using Nordstein.Core.Licensing.Internal;

namespace Nordstein.Core.Licensing.Tests;

/// <summary>
/// Whether <see cref="LicenseService.ApplySnapshot"/> raises <see cref="LicenseService.Changed"/>: every scalar field and
/// both collections, compared by value, including the null collections a snapshot built outside the engine may carry.
/// </summary>
[TestClass]
public sealed class LicenseSnapshotComparisonTests
{
    private static readonly LicenseSnapshot Baseline = LicenseSnapshot.Fallback(new TestTierPolicy()) with
    {
        Features = new HashSet<string> { "A" },
        Limits = new Dictionary<string, long> { ["L"] = 1 },
    };

    [TestMethod]
    public void ApplySnapshot_EqualByValue_RaisesNothing()
    {
        LicenseSnapshot rebuilt = Baseline with
        {
            Features = new HashSet<string> { "A" },
            Limits = new Dictionary<string, long> { ["L"] = 1 },
        };

        RaisedAfter(rebuilt).Should().Be(0);
    }

    [TestMethod]
    public void ApplySnapshot_EachFieldChanged_RaisesChanged()
    {
        LicenseSnapshot[] changes =
        [
            Baseline with { Tier = TestTierPolicy.Premium },
            Baseline with { Status = LicenseStatus.Grace },
            Baseline with { ExpiresAt = DateTimeOffset.UnixEpoch },
            Baseline with { GracePeriodEndsAt = DateTimeOffset.UnixEpoch },
            Baseline with { CustomerEmail = "someone@example.com" },
            Baseline with { Jti = "another" },
            Baseline with { Source = LicenseSource.Stored },
            Baseline with { InvalidReason = "revoked" },
            Baseline with { Offline = true },
            Baseline with { Features = new HashSet<string> { "B" } },
            Baseline with { Features = new HashSet<string> { "A", "B" } },
            Baseline with { Limits = new Dictionary<string, long> { ["L"] = 2 } },
            Baseline with { Limits = new Dictionary<string, long> { ["M"] = 1 } },
        ];

        changes.Select(RaisedAfter).Should().AllBeEquivalentTo(1);
    }

    [TestMethod]
    public void ApplySnapshot_NullCollectionAgainstAFilledOne_RaisesChanged()
    {
        LicenseSnapshot noFeatures = Baseline with { Features = NullOf<IReadOnlySet<string>>() };
        LicenseSnapshot noLimits = Baseline with { Limits = NullOf<IReadOnlyDictionary<string, long>>() };

        RaisedAfter(noFeatures).Should().Be(1);
        RaisedAfter(noLimits).Should().Be(1);
    }

    [TestMethod]
    public void ApplySnapshot_SameCollectionInstances_RaisesNothing()
    {
        LicenseSnapshot same = Baseline with { Features = Baseline.Features, Limits = Baseline.Limits };

        RaisedAfter(same).Should().Be(0);
    }

    // Applies the baseline, then the snapshot, and counts the Changed events the second apply raised.
    private static int RaisedAfter(LicenseSnapshot snapshot)
    {
        LicenseService service = CreateService();
        service.ApplySnapshot(Baseline);
        int raised = 0;
        service.Changed += () => raised++;
        service.ApplySnapshot(snapshot);
        return raised;
    }

    private static LicenseService CreateService()
    {
        var policy = new TestTierPolicy();
        LicensingConfiguration config = Module.Factory.Configuration(jwt: null);
        var validator = new JwtLicenseValidator(config, policy, NullLogger<JwtLicenseValidator>.Instance);
        var resolver = new ConfiguredLicenseResolver(config, validator, policy, NullLogger<ConfiguredLicenseResolver>.Instance);
        var gate = Substitute.For<IAsyncLock>();
        gate.Lock(Arg.Any<object>()).Returns(Substitute.For<IDisposable>());
        var trigger = Substitute.For<ILicenseRefreshTrigger>();
        return new LicenseService(resolver, gate, () => trigger, NullLogger<LicenseService>.Instance);
    }

    // A null typed as a non-nullable reference, the way a snapshot deserialized or built outside the engine can carry one;
    // the `!` operator is banned, an array slot is not.
    private static T NullOf<T>()
        where T : class => (new T[1])[0];
}
