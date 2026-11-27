using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Nordstein.Core.Domain;
using Nordstein.Core.Domain.Exceptions;
using Nordstein.Core.Testing;
using Nordstein.Core.Testing.Time;

namespace Nordstein.Core.Storage.Tests;

/// <summary>
/// End-to-end coverage of the extracted foundation: assembly-scoped discovery wires a product's
/// entity/config/repository, and the generic <see cref="AbstractRepository{TDomainEntity,TStoredEntity}"/>
/// round-trips through a <see cref="NordsteinDbContext"/> and its ambient-transaction seam.
/// </summary>
[TestClass]
public sealed class StorageFoundationTests : BaseTest<Module>
{
    [TestMethod]
    public void Discovery_RegistersTheRepositoryAndTransactionSeam()
    {
        IServiceProvider services = GetServices();

        services.GetService<IRepository<ITestThing>>().Should().NotBeNull();
        services.GetService<ITransaction>().Should().NotBeNull();
        services.GetService<AmbientDbContext>().Should().NotBeNull();
    }

    [TestMethod]
    public async Task AddAsync_ThenFind_RoundTripsThroughTheFoundation()
    {
        IServiceProvider services = GetServices();
        IRepository<ITestThing> repository = services.GetRequiredService<IRepository<ITestThing>>();

        ITestThing added = await repository.AddAsync(NewThing("widget"), CancellationToken);

        ITestThing? loaded = await repository.FindAsync(added.Id, CancellationToken);
        Assert.IsNotNull(loaded);
        loaded.Name.Should().Be("widget");
        (await repository.CountAsync(CancellationToken)).Should().Be(1);
    }

    [TestMethod]
    public async Task AddAsync_WithDuplicateId_Throws()
    {
        IServiceProvider services = GetServices();
        IRepository<ITestThing> repository = services.GetRequiredService<IRepository<ITestThing>>();
        ITestThing thing = NewThing("first");
        await repository.AddAsync(thing, CancellationToken);

        await FluentActions
            .Invoking(() => repository.AddAsync(thing, CancellationToken))
            .Should().ThrowAsync<EntityAlreadyExistsException>();
    }

    [TestMethod]
    public async Task UpdateAsync_WithCurrentToken_PersistsAndStampsUpdatedAt()
    {
        IServiceProvider services = GetServices();
        IRepository<ITestThing> repository = services.GetRequiredService<IRepository<ITestThing>>();
        ITestThing added = await repository.AddAsync(NewThing("before"), CancellationToken);
        ManualClock clock = services.GetRequiredService<ManualClock>();
        clock.SetUtcNow(added.UpdatedAt.AddSeconds(1));

        var renamed = new TestThing
        {
            Id = added.Id,
            CreatedAt = added.CreatedAt,
            UpdatedAt = added.UpdatedAt,
            Name = "after",
        };
        ITestThing updated = await repository.UpdateAsync(renamed, CancellationToken);

        updated.Name.Should().Be("after");
        updated.UpdatedAt.Should().Be(added.UpdatedAt.AddSeconds(1));

        ITestThing? reloaded = await repository.FindAsync(added.Id, CancellationToken);
        Assert.IsNotNull(reloaded);
        reloaded.Name.Should().Be("after");
    }

    [TestMethod]
    public void Constructor_NullClock_Throws()
    {
        IServiceProvider services = GetServices();

        // guard test
        Action act = () => _ = new TestThingRepository(
            services.GetRequiredService<IMapper<ITestThing, TestThingEntity>>(),
            services.GetRequiredService<Func<DbContext>>(),
            services.GetRequiredService<ITransaction>(),
            services.GetRequiredService<Nordstein.Core.Domain.Events.IEntityEventService>(),
            services.GetRequiredService<AmbientDbContext>(),
            null!);

        act.Should().Throw<ArgumentNullException>().WithParameterName("clock");
    }

    [TestMethod]
    public async Task UpdateAsync_StampsUpdatedAtFromTheContainerClock()
    {
        IServiceProvider services = GetServices();
        IRepository<ITestThing> repository = services.GetRequiredService<IRepository<ITestThing>>();
        ManualClock clock = services.GetRequiredService<ManualClock>();
        ITestThing added = await repository.AddAsync(NewThing("before"), CancellationToken);
        clock.SetUtcNow(new DateTimeOffset(2031, 3, 4, 5, 6, 7, TimeSpan.Zero));

        var renamed = new TestThing { Id = added.Id, CreatedAt = added.CreatedAt, UpdatedAt = added.UpdatedAt, Name = "after" };
        await repository.UpdateAsync(renamed, CancellationToken);

        ITestThing reloaded = await repository.GetAsync(added.Id, CancellationToken);
        reloaded.UpdatedAt.Should().Be(clock.UtcNow);
    }

    [TestMethod]
    public async Task UpdateAsync_WithStaleToken_ThrowsOptimisticConcurrency()
    {
        IServiceProvider services = GetServices();
        IRepository<ITestThing> repository = services.GetRequiredService<IRepository<ITestThing>>();
        ITestThing added = await repository.AddAsync(NewThing("v1"), CancellationToken);

        var stale = new TestThing
        {
            Id = added.Id,
            CreatedAt = added.CreatedAt,
            // A token one second in the past never matches the persisted version.
            UpdatedAt = added.UpdatedAt.AddSeconds(-1),
            Name = "v2",
        };

        await FluentActions
            .Invoking(() => repository.UpdateAsync(stale, CancellationToken))
            .Should().ThrowAsync<OptimisticConcurrencyException>();
    }

    [TestMethod]
    public async Task RemoveAsync_DeletesTheRow()
    {
        IServiceProvider services = GetServices();
        IRepository<ITestThing> repository = services.GetRequiredService<IRepository<ITestThing>>();
        ITestThing added = await repository.AddAsync(NewThing("doomed"), CancellationToken);

        (await repository.RemoveAsync(added.Id, CancellationToken)).Should().BeTrue();
        (await repository.FindAsync(added.Id, CancellationToken)).Should().BeNull();
    }

    [TestMethod]
    public void ModelConvention_MarksUpdatedAtAsAConcurrencyToken()
    {
        IServiceProvider services = GetServices();
        var context = services.GetRequiredService<TestDbContext>();

        var entityType = context.Model.FindEntityType(typeof(TestThingEntity));
        Assert.IsNotNull(entityType);
        var updatedAt = entityType.FindProperty(nameof(Entity.UpdatedAt));
        Assert.IsNotNull(updatedAt);
        updatedAt.IsConcurrencyToken.Should().BeTrue();
    }

    private static ITestThing NewThing(string name)
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        return new TestThing { Id = Guid.NewGuid(), CreatedAt = now, UpdatedAt = now, Name = name };
    }
}
