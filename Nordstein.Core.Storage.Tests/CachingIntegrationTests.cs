using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Nordstein.Core.Domain;
using Nordstein.Core.Testing;

namespace Nordstein.Core.Storage.Tests;

/// <summary>
/// The read-through cache wired for a <c>[Cacheable]</c> entity: discovery registers the cache and
/// its process-wide version registry, reads populate it, and writes invalidate it. Because the
/// repository and the cache are both resolved from the same (root) lifetime scope, the test can
/// observe the very cache instance the repository reads through.
/// </summary>
[TestClass]
public sealed class CachingIntegrationTests : BaseTest<Module>
{
    private static ITestCachedThing NewThing(string name)
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        return new TestCachedThing { Id = Guid.NewGuid(), CreatedAt = now, UpdatedAt = now, Name = name };
    }

    [TestMethod]
    public void Discovery_RegistersTheCacheAndVersionRegistryForACacheableEntity()
    {
        IServiceProvider services = GetServices();

        services.GetService<IEntityCache<ITestCachedThing>>().Should().NotBeNull();
        services.GetService<EntityCacheVersions<ITestCachedThing>>().Should().NotBeNull();
    }

    [TestMethod]
    public async Task FindAsync_PopulatesTheCache_AndServesTheSecondReadFromIt()
    {
        IServiceProvider services = GetServices();
        var repository = services.GetRequiredService<IRepository<ITestCachedThing>>();
        var cache = services.GetRequiredService<IEntityCache<ITestCachedThing>>();
        ITestCachedThing added = await repository.AddAsync(NewThing("cached"), CancellationToken);

        // First read populates...
        (await repository.FindAsync(added.Id, CancellationToken))!.Name.Should().Be("cached");
        cache.TryGet(added.Id).Should().NotBeNull();

        // ...second read is served from the cache (same value).
        (await repository.FindAsync(added.Id, CancellationToken))!.Name.Should().Be("cached");
    }

    [TestMethod]
    public async Task GetAllAsync_PopulatesTheSnapshot()
    {
        IServiceProvider services = GetServices();
        var repository = services.GetRequiredService<IRepository<ITestCachedThing>>();
        var cache = services.GetRequiredService<IEntityCache<ITestCachedThing>>();
        await repository.AddAsync(NewThing("a"), CancellationToken);
        await repository.AddAsync(NewThing("b"), CancellationToken);

        await repository.GetAllAsync(CancellationToken);

        cache.TryGetAll().Should().NotBeNull();
        cache.TryGetAll()!.Should().HaveCount(2);
    }

    [TestMethod]
    public async Task GetManyAsync_MixesCacheHitsAndDatabaseMisses()
    {
        IServiceProvider services = GetServices();
        var repository = services.GetRequiredService<IRepository<ITestCachedThing>>();
        ITestCachedThing a = await repository.AddAsync(NewThing("a"), CancellationToken);
        ITestCachedThing b = await repository.AddAsync(NewThing("b"), CancellationToken);

        // Warm the cache for `a` only, then request both — one hit, one miss.
        await repository.FindAsync(a.Id, CancellationToken);
        IReadOnlyList<ITestCachedThing> many = await repository.GetManyAsync([a.Id, b.Id], cancellationToken: CancellationToken);

        many.Select(t => t.Name).Should().BeEquivalentTo(["a", "b"]);
    }

    [TestMethod]
    public async Task GetAllAsync_ServesTheSecondCallFromTheSnapshot()
    {
        IServiceProvider services = GetServices();
        var repository = services.GetRequiredService<IRepository<ITestCachedThing>>();
        await repository.AddAsync(NewThing("a"), CancellationToken);

        (await repository.GetAllAsync(CancellationToken)).Should().ContainSingle(); // populates snapshot
        (await repository.GetAllAsync(CancellationToken)).Should().ContainSingle(); // served from snapshot
    }

    [TestMethod]
    public async Task EnumerateAsync_PopulatesTheCacheAsItStreams()
    {
        IServiceProvider services = GetServices();
        var repository = services.GetRequiredService<IRepository<ITestCachedThing>>();
        var cache = services.GetRequiredService<IEntityCache<ITestCachedThing>>();
        ITestCachedThing added = await repository.AddAsync(NewThing("streamed"), CancellationToken);

        await foreach (ITestCachedThing _ in repository.EnumerateAsync(CancellationToken)) { }

        cache.TryGet(added.Id).Should().NotBeNull();
    }

    [TestMethod]
    public async Task GetManyAsync_WhenEveryIdIsCached_ServesEntirelyFromCache()
    {
        IServiceProvider services = GetServices();
        var repository = services.GetRequiredService<IRepository<ITestCachedThing>>();
        ITestCachedThing a = await repository.AddAsync(NewThing("a"), CancellationToken);
        await repository.FindAsync(a.Id, CancellationToken); // warm the cache

        IReadOnlyList<ITestCachedThing> many = await repository.GetManyAsync([a.Id], cancellationToken: CancellationToken);

        many.Should().ContainSingle();
        many[0].Name.Should().Be("a");
    }

    [TestMethod]
    public async Task GetManyAsync_OnCacheableRepo_WhenMissingAndNotIgnored_Throws()
    {
        IServiceProvider services = GetServices();
        var repository = services.GetRequiredService<IRepository<ITestCachedThing>>();
        ITestCachedThing a = await repository.AddAsync(NewThing("a"), CancellationToken);
        await repository.FindAsync(a.Id, CancellationToken); // warm cache so the request mixes hit + miss

        await FluentActions
            .Invoking(() => repository.GetManyAsync([a.Id, Guid.NewGuid()], cancellationToken: CancellationToken))
            .Should().ThrowAsync<Nordstein.Core.Domain.Exceptions.EntitiesNotFoundException>();
    }

    [TestMethod]
    public async Task UpdateAsync_InvalidatesTheCachedEntry()
    {
        IServiceProvider services = GetServices();
        var repository = services.GetRequiredService<IRepository<ITestCachedThing>>();
        var cache = services.GetRequiredService<IEntityCache<ITestCachedThing>>();
        ITestCachedThing added = await repository.AddAsync(NewThing("v1"), CancellationToken);
        await repository.FindAsync(added.Id, CancellationToken); // populate
        cache.TryGet(added.Id).Should().NotBeNull();

        await repository.UpdateAsync(new TestCachedThing
        {
            Id = added.Id,
            CreatedAt = added.CreatedAt,
            UpdatedAt = added.UpdatedAt,
            Name = "v2",
        }, CancellationToken);

        cache.TryGet(added.Id).Should().BeNull();
    }
}
