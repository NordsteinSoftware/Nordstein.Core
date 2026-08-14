using AwesomeAssertions;

namespace Nordstein.Core.Storage.Tests;

/// <summary>
/// Unit coverage of the reference-data cache: per-id and snapshot storage, TTL expiry, and the
/// process-wide version registry that lets a write in one scope invalidate a copy held by another.
/// </summary>
[TestClass]
public sealed class EntityCacheTests
{
    private static ITestThing Thing(Guid id, string name = "x")
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        return new TestThing { Id = id, CreatedAt = now, UpdatedAt = now, Name = name };
    }

    [TestMethod]
    public void TryGet_WhenAbsent_ReturnsNull()
    {
        var cache = new EntityCache<ITestThing>(new EntityCacheVersions<ITestThing>());
        cache.TryGet(Guid.NewGuid()).Should().BeNull();
    }

    [TestMethod]
    public void SetThenTryGet_ReturnsTheEntity()
    {
        var cache = new EntityCache<ITestThing>(new EntityCacheVersions<ITestThing>());
        ITestThing thing = Thing(Guid.NewGuid(), "cached");

        cache.Set(thing);

        cache.TryGet(thing.Id).Should().BeSameAs(thing);
    }

    [TestMethod]
    public void Invalidate_EvictsAcrossEveryCacheSharingTheVersions()
    {
        var versions = new EntityCacheVersions<ITestThing>();
        var writer = new EntityCache<ITestThing>(versions);
        var reader = new EntityCache<ITestThing>(versions);
        ITestThing thing = Thing(Guid.NewGuid());
        writer.Set(thing);
        reader.Set(thing);

        // A write on one scope's cache must invalidate the copy the other scope holds.
        writer.Invalidate(thing.Id);

        reader.TryGet(thing.Id).Should().BeNull();
    }

    [TestMethod]
    public void TryGet_AfterTtlElapses_ReturnsNull()
    {
        var clock = new TestClock { UtcNow = DateTimeOffset.UnixEpoch };
        var cache = new EntityCache<ITestThing>(new EntityCacheVersions<ITestThing>(), clock, TimeSpan.FromMinutes(1));
        ITestThing thing = Thing(Guid.NewGuid());
        cache.Set(thing);

        clock.UtcNow = clock.UtcNow.AddMinutes(2);

        cache.TryGet(thing.Id).Should().BeNull();
    }

    [TestMethod]
    public void SetAll_ThenTryGetAll_ReturnsTheSnapshotUntilInvalidated()
    {
        var cache = new EntityCache<ITestThing>(new EntityCacheVersions<ITestThing>());
        IReadOnlyList<ITestThing> all = [Thing(Guid.NewGuid()), Thing(Guid.NewGuid())];

        cache.SetAll(all);
        cache.TryGetAll().Should().BeEquivalentTo(all);

        cache.InvalidateAll();
        cache.TryGetAll().Should().BeNull();
    }

    [TestMethod]
    public void TryGetAll_WhenAnotherCacheInvalidatesAnEntry_ReportsTheSnapshotStale()
    {
        var versions = new EntityCacheVersions<ITestThing>();
        var owner = new EntityCache<ITestThing>(versions);
        var other = new EntityCache<ITestThing>(versions);
        owner.SetAll([Thing(Guid.NewGuid())]);

        // A single-entity write on another scope's cache bumps the shared AllVersion; the owner's
        // still-populated snapshot must now read as stale (the version-mismatch branch).
        other.Invalidate(Guid.NewGuid());

        owner.TryGetAll().Should().BeNull();
    }

    [TestMethod]
    public void TryGetAll_AfterTtlElapses_ReturnsNull()
    {
        var clock = new TestClock { UtcNow = DateTimeOffset.UnixEpoch };
        var cache = new EntityCache<ITestThing>(new EntityCacheVersions<ITestThing>(), clock, TimeSpan.FromMinutes(1));
        cache.SetAll([Thing(Guid.NewGuid())]);

        clock.UtcNow = clock.UtcNow.AddMinutes(2);

        cache.TryGetAll().Should().BeNull();
    }

    [TestMethod]
    public void Versions_WhenTheTrackedIdCapIsExceeded_DropsTheMapAndFailsSafe()
    {
        var versions = new EntityCacheVersions<ITestThing>();
        Guid tracked = Guid.NewGuid();
        versions.Invalidate(tracked);
        versions.VersionOf(tracked).Should().Be(1);

        // Push past the 10,000-id cap; the next new id clears the whole map rather than growing it.
        for (int i = 0; i < 10_050; i++)
        {
            versions.Invalidate(Guid.NewGuid());
        }

        // A forgotten id reads back as 0 — a guaranteed miss, never a stale hit.
        versions.VersionOf(tracked).Should().Be(0);
    }

    private sealed class TestClock : TimeProvider
    {
        public DateTimeOffset UtcNow { get; set; }
        public override DateTimeOffset GetUtcNow() => UtcNow;
    }
}
