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
    [TestMethod]
    public void ApplySnapshot_EqualByValue_RaisesNothing()
    {
        RaisedAfter(Baseline(), Baseline()).Should().Be(0);
    }

    [TestMethod]
    public void ApplySnapshot_EachFieldChanged_RaisesChanged()
    {
        LicenseSnapshot[] changes =
        [
            Baseline() with { Tier = TestTierPolicy.Premium },
            Baseline() with { Status = LicenseStatus.Grace },
            Baseline() with { ExpiresAt = DateTimeOffset.UnixEpoch },
            Baseline() with { GracePeriodEndsAt = DateTimeOffset.UnixEpoch },
            Baseline() with { CustomerEmail = "someone@example.com" },
            Baseline() with { Jti = "another" },
            Baseline() with { Source = LicenseSource.Stored },
            Baseline() with { InvalidReason = "revoked" },
            Baseline() with { Offline = true },
            Baseline() with { Features = new HashSet<string> { "B" } },
            Baseline() with { Features = new HashSet<string> { "A", "B" } },
            Baseline() with { Limits = new Dictionary<string, long> { ["L"] = 2 } },
            Baseline() with { Limits = new Dictionary<string, long> { ["M"] = 1 } },
        ];

        changes.Select(change => RaisedAfter(Baseline(), change)).Should().AllBeEquivalentTo(1);
    }

    [TestMethod]
    public void ApplySnapshot_NullCollectionAgainstAFilledOne_RaisesChanged()
    {
        LicenseSnapshot noFeatures = Baseline() with { Features = NullOf<IReadOnlySet<string>>() };
        LicenseSnapshot noLimits = Baseline() with { Limits = NullOf<IReadOnlyDictionary<string, long>>() };

        RaisedAfter(Baseline(), noFeatures).Should().Be(1);
        RaisedAfter(Baseline(), noLimits).Should().Be(1);
    }

    [TestMethod]
    public void ApplySnapshot_SameCollectionInstances_RaisesNothing()
    {
        LicenseSnapshot first = Baseline();
        LicenseSnapshot same = first with { Features = first.Features, Limits = first.Limits };

        RaisedAfter(first, same).Should().Be(0);
    }

    // A snapshot with one feature and one limit, built fresh for every use.
    private static LicenseSnapshot Baseline() => LicenseSnapshot.Fallback(new TestTierPolicy()) with
    {
        Features = new HashSet<string> { "A" },
        Limits = new Dictionary<string, long> { ["L"] = 1 },
    };

    // Applies `first`, then `snapshot`, and counts the Changed events the second apply raised.
    private static int RaisedAfter(LicenseSnapshot first, LicenseSnapshot snapshot)
    {
        LicenseService service = CreateService();
        service.ApplySnapshot(first);
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
