using Autofac;
using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Nordstein.Core.Common.Time;
using Nordstein.Core.Domain;
using Nordstein.Core.Domain.Events;
using Nordstein.Core.Domain.Exceptions;
using Nordstein.Core.Testing;
using Nordstein.Core.Testing.Time;

namespace Nordstein.Core.Storage.Tests;

/// <summary>
/// The storage foundation's paths the scenario suites leave out: the cache on bulk removal and on
/// an invalidation outside a transaction, the cached multi-get with missing ids, the ambient
/// context outside a transaction, archive and unarchive losing a concurrency race at save, the
/// archive-only bulk delete, and discovery's handling of abstract, interface, join-row and
/// unconfigured entity types.
/// </summary>
[TestClass]
public sealed class StorageEdgePathTests : BaseTest<Module>
{
    [TestMethod]
    public async Task RemoveAllAsync_OnACachedRepository_DropsTheCachedSnapshot()
    {
        IServiceProvider services = GetServices();
        var repository = services.GetRequiredService<IRepository<ITestCachedThing>>();
        await repository.AddAsync(NewCachedThing("a"), CancellationToken);
        (await repository.GetAllAsync(CancellationToken)).Should().ContainSingle("the read fills the snapshot");

        await repository.RemoveAllAsync(CancellationToken);

        (await repository.GetAllAsync(CancellationToken)).Should().BeEmpty("the removal dropped the snapshot too");
    }

    [TestMethod]
    public async Task InvalidateCache_OutsideATransaction_DropsTheSnapshotAtOnce()
    {
        IServiceProvider services = GetServices();
        RepositoryProbe<ITestCachedThing, TestCachedThingEntity> probe = CachedProbe(services);
        await probe.AddAsync(NewCachedThing("first"), CancellationToken);
        (await probe.GetAllAsync(CancellationToken)).Should().ContainSingle();
        await AddBehindTheCacheAsync(services, "second");
        (await probe.GetAllAsync(CancellationToken)).Should().ContainSingle("the snapshot does not see a write that bypassed it");

        probe.DropCache();

        (await probe.GetAllAsync(CancellationToken)).Select(thing => thing.Name).Should().BeEquivalentTo(["first", "second"]);
    }

    [TestMethod]
    public async Task MapToStored_MapsADomainEntityAndPassesNullThrough()
    {
        IServiceProvider services = GetServices();
        RepositoryProbe<ITestCachedThing, TestCachedThingEntity> probe = CachedProbe(services);
        ITestCachedThing thing = NewCachedThing("mapped");

        TestCachedThingEntity? stored = await probe.MapToStored(thing);
        TestCachedThingEntity? none = await probe.MapToStored(null);

        stored.Should().NotBeNull();
        stored?.Id.Should().Be(thing.Id);
        stored?.Name.Should().Be("mapped");
        none.Should().BeNull();
    }

    [TestMethod]
    public async Task GetManyAsync_ThroughTheCache_WithAMissingId_ThrowsNamingIt()
    {
        IServiceProvider services = GetServices();
        var repository = services.GetRequiredService<IRepository<ITestCachedThing>>();
        ITestCachedThing present = await repository.AddAsync(NewCachedThing("present"), CancellationToken);
        Guid missing = Guid.NewGuid();

        await FluentActions
            .Awaiting(() => repository.GetManyAsync([present.Id, missing], cancellationToken: CancellationToken))
            .Should().ThrowAsync<EntitiesNotFoundException>()
            .WithMessage($"*'{missing}'*");
    }

    [TestMethod]
    public async Task GetManyAsync_ThroughTheCache_IgnoringMissingIds_ReturnsTheOnesFound()
    {
        IServiceProvider services = GetServices();
        var repository = services.GetRequiredService<IRepository<ITestCachedThing>>();
        ITestCachedThing present = await repository.AddAsync(NewCachedThing("present"), CancellationToken);

        IReadOnlyList<ITestCachedThing> found = await repository.GetManyAsync(
            [present.Id, Guid.NewGuid()], ignoreMissing: true, CancellationToken);

        found.Select(thing => thing.Id).Should().Equal(present.Id);
    }

    [TestMethod]
    public async Task AmbientContext_OutsideATransaction_HasNothingAndRefusesWrites()
    {
        IServiceProvider services = GetServices();
        var ambient = services.GetRequiredService<AmbientDbContext>();

        ambient.IsActive.Should().BeFalse();
        ambient.Transaction.Should().BeNull();
        ambient.TakePostCommit().Should().BeEmpty();
        Action write = () => ambient.RequireContext();
        write.Should().Throw<InvalidOperationException>().WithMessage("*No ambient transaction context is active*");

        AmbientSnapshot inside = await services.GetRequiredService<ITransaction>()
            .InvokeAsync(() => Task.FromResult(new AmbientSnapshot(ambient.Transaction is not null, ambient.TakePostCommit().Count)), CancellationToken);

        inside.Should().Be(new AmbientSnapshot(HasTransaction: true, PostCommitActions: 0));
    }

    [TestMethod]
    public async Task ArchiveAsync_WhenTheSaveLosesAConcurrencyRace_ReportsNoTransition()
    {
        var race = new ConcurrencyRace();
        IServiceProvider services = GetServices(builder => UseOptionsWith(builder, race));
        var repository = services.GetRequiredService<ITestDocRepository>();
        ITestDoc doc = await repository.AddAsync(NewDoc("contested"), CancellationToken);

        race.Armed = true;
        bool archived = await repository.ArchiveAsync(doc.Id, CancellationToken);
        race.Armed = false;

        archived.Should().BeFalse("another writer moved the row on after the read");
        (await repository.GetAsync(doc.Id, CancellationToken)).IsArchived.Should().BeFalse();
    }

    [TestMethod]
    public async Task Unarchive_WhenTheSaveLosesAConcurrencyRace_LeavesTheRowArchived()
    {
        var race = new ConcurrencyRace();
        IServiceProvider services = GetServices(builder => UseOptionsWith(builder, race));
        var repository = services.GetRequiredService<ITestDocRepository>();
        ITestDoc doc = await repository.AddAsync(NewDoc("contested"), CancellationToken);
        (await repository.ArchiveAsync(doc.Id, CancellationToken)).Should().BeTrue();

        race.Armed = true;
        await repository.Unarchive(doc.Id, CancellationToken);
        race.Armed = false;

        (await repository.GetAsync(doc.Id, CancellationToken)).IsArchived.Should().BeTrue("the unarchive lost the race");
    }

    [TestMethod]
    public async Task UpdateAsync_WhenTheSaveLosesAConcurrencyRace_ThrowsOptimisticConcurrency()
    {
        var race = new ConcurrencyRace();
        IServiceProvider services = GetServices(builder => UseOptionsWith(builder, race));
        var repository = services.GetRequiredService<IRepository<ITestThing>>();
        DateTimeOffset now = DateTimeOffset.UtcNow;
        ITestThing added = await repository.AddAsync(
            new TestThing { Id = Guid.NewGuid(), CreatedAt = now, UpdatedAt = now, Name = "before" }, CancellationToken);

        race.Armed = true;
        OptimisticConcurrencyException thrown = (await FluentActions
            .Awaiting(() => repository.UpdateAsync(
                new TestThing { Id = added.Id, CreatedAt = added.CreatedAt, UpdatedAt = added.UpdatedAt, Name = "after" },
                CancellationToken))
            .Should().ThrowAsync<OptimisticConcurrencyException>()).Which;
        race.Armed = false;

        thrown.InnerException.Should().BeOfType<DbUpdateConcurrencyException>();
        (await repository.GetAsync(added.Id, CancellationToken)).Name.Should().Be("before");
    }

    [TestMethod]
    public async Task RemoveAsync_WhenTheSaveLosesAConcurrencyRace_ReportsNotRemoved()
    {
        var race = new ConcurrencyRace();
        IServiceProvider services = GetServices(builder => UseOptionsWith(builder, race));
        var repository = services.GetRequiredService<ITestDocRepository>();
        ITestDoc doc = await repository.AddAsync(NewDoc("contested"), CancellationToken);

        race.Armed = true;
        bool removed = await repository.RemoveAsync(doc.Id, CancellationToken);
        race.Armed = false;

        removed.Should().BeFalse("another writer moved the row on after the read");
        (await repository.ContainsAsync(doc.Id, CancellationToken)).Should().BeTrue();
    }

    [TestMethod]
    public async Task RemoveAllAsync_OnAnArchiveOnlyRepository_IsRefused()
    {
        IServiceProvider services = GetServices();
        var repository = services.GetRequiredService<IRepository<ITestLockedDoc>>();

        await FluentActions.Awaiting(() => repository.RemoveAllAsync(CancellationToken))
            .Should().ThrowAsync<InvalidOperationException>().WithMessage("*archive-only*ArchiveAsync per row*");
    }

    [TestMethod]
    public async Task RemoveAllAsync_OnAnArchivableRepositoryThatAllowsHardDeletes_ClearsTheTable()
    {
        IServiceProvider services = GetServices();
        var repository = services.GetRequiredService<ITestDocRepository>();
        await repository.AddAsync(NewDoc("gone"), CancellationToken);

        await repository.RemoveAllAsync(CancellationToken);

        (await repository.CountAsync(CancellationToken)).Should().Be(0);
    }

    [TestMethod]
    public void GetDomainEntityType_OfATypeWithoutTheAttribute_IsNull()
    {
        typeof(TestJoinRow).GetDomainEntityType().Should().BeNull();
        typeof(TestThingEntity).GetDomainEntityType().Should().Be(typeof(ITestThing));
    }

    [TestMethod]
    public void Discovery_SkipsAbstractAndInterfaceStoredEntities()
    {
        IServiceProvider services = GetServices();

        using TestDbContext context = services.GetRequiredService<TestDbContext>();

        context.Model.FindEntityType(typeof(TestAbstractEntity)).Should().BeNull();
        context.Model.FindEntityType(typeof(ITestStoredContract)).Should().BeNull();
        context.Model.FindEntityType(typeof(TestThingEntity)).Should().NotBeNull();
    }

    [TestMethod]
    public void Discovery_ConfiguresAnAdditionalJoinRowWithoutARepository()
    {
        using IContainer container = BuildStorage(new StorageFoundationModule<TestDbContext>(typeof(Module).Assembly, typeof(TestJoinRow)));

        using TestDbContext context = container.Resolve<TestDbContext>();

        context.Model.FindEntityType(typeof(TestJoinRow)).Should().NotBeNull();
        container.IsRegistered<TestJoinRow>().Should().BeTrue();
    }

    [TestMethod]
    public void Discovery_WithNullAdditionalEntities_ConfiguresTheDiscoveredOnes()
    {
#pragma warning disable CS8625 // WHY: a null params array is the case under test; the module treats it as none.
        using IContainer container = BuildStorage(new StorageFoundationModule<TestDbContext>(typeof(Module).Assembly, null));
#pragma warning restore CS8625

        using TestDbContext context = container.Resolve<TestDbContext>();

        context.Model.FindEntityType(typeof(TestThingEntity)).Should().NotBeNull();
    }

    [TestMethod]
    public void Discovery_OfAnAdditionalEntityWithoutAConfiguration_FailsTheBuild()
    {
        Action act = () => BuildStorage(new StorageFoundationModule<TestDbContext>(typeof(Module).Assembly, typeof(TestUnconfiguredRow))).Dispose();

        act.Should().Throw<InvalidOperationException>().WithMessage("No configuration type found for entity type TestUnconfiguredRow");
    }

    private static ITestCachedThing NewCachedThing(string name)
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        return new TestCachedThing { Id = Guid.NewGuid(), CreatedAt = now, UpdatedAt = now, Name = name };
    }

    private static ITestDoc NewDoc(string title)
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        return new TestDoc { Id = Guid.NewGuid(), CreatedAt = now, UpdatedAt = now, Title = title };
    }

    private static RepositoryProbe<ITestCachedThing, TestCachedThingEntity> CachedProbe(IServiceProvider services)
        => new(
            services.GetRequiredService<IMapper<ITestCachedThing, TestCachedThingEntity>>(),
            services.GetRequiredService<Func<DbContext>>(),
            services.GetRequiredService<ITransaction>(),
            services.GetRequiredService<IEntityEventService>(),
            services.GetRequiredService<AmbientDbContext>(),
            services.GetRequiredService<IClock>(),
            services.GetRequiredService<IEntityCache<ITestCachedThing>>());

    private async Task AddBehindTheCacheAsync(IServiceProvider services, string name)
    {
        await using TestDbContext context = services.GetRequiredService<TestDbContext>();
        DateTimeOffset now = DateTimeOffset.UtcNow;
        context.Add(new TestCachedThingEntity { Id = Guid.NewGuid(), CreatedAt = now, UpdatedAt = now, Name = name });
        await context.SaveChangesAsync(CancellationToken);
    }

    private static void UseOptionsWith(ContainerBuilder builder, IInterceptor interceptor)
        => builder.Register<DbContextOptions<TestDbContext>>(_ => new DbContextOptionsBuilder<TestDbContext>()
                .UseInMemoryDatabase("core-storage-edge-" + Guid.NewGuid())
                .ConfigureWarnings(warnings => warnings.Ignore(InMemoryEventId.TransactionIgnoredWarning))
                .AddInterceptors(interceptor)
                .Options)
            .SingleInstance();

    /// <summary>A container of the storage module alone and the services its context needs.</summary>
    private static IContainer BuildStorage(StorageFoundationModule<TestDbContext> storage)
    {
        var builder = new ContainerBuilder();
        builder.RegisterInstance(new ManualClock()).As<IClock>();
        builder.RegisterInstance(Substitute.For<IEntityEventService>()).As<IEntityEventService>();
        builder.Register<DbContextOptions<TestDbContext>>(_ => new DbContextOptionsBuilder<TestDbContext>()
                .UseInMemoryDatabase("core-storage-discovery-" + Guid.NewGuid())
                .Options)
            .SingleInstance();
        builder.RegisterModule(storage);
        return builder.Build();
    }

    /// <summary>What a transaction body saw of the ambient context.</summary>
    private sealed record AmbientSnapshot(bool HasTransaction, int PostCommitActions);

    /// <summary>
    /// Fails the next save while armed the way a lost optimistic-concurrency race does: another
    /// writer moved the row's token on after this context read it.
    /// </summary>
    private sealed class ConcurrencyRace : SaveChangesInterceptor
    {
        public bool Armed { get; set; }

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
            => Armed
                ? throw new DbUpdateConcurrencyException("Simulated lost race: the row's UpdatedAt moved on.")
                : base.SavingChangesAsync(eventData, result, cancellationToken);
    }
}
