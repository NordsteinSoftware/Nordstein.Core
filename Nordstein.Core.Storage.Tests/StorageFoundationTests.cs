using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Nordstein.Core.Domain;
using Nordstein.Core.Domain.Exceptions;
using Nordstein.Core.Testing;

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
        loaded.Should().NotBeNull();
        loaded!.Name.Should().Be("widget");
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

        var renamed = new TestThing
        {
            Id = added.Id,
            CreatedAt = added.CreatedAt,
            UpdatedAt = added.UpdatedAt,
            Name = "after",
        };
        ITestThing updated = await repository.UpdateAsync(renamed, CancellationToken);

        updated.Name.Should().Be("after");
        updated.UpdatedAt.Should().BeOnOrAfter(added.UpdatedAt);

        ITestThing? reloaded = await repository.FindAsync(added.Id, CancellationToken);
        reloaded!.Name.Should().Be("after");
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
        entityType.Should().NotBeNull();
        var updatedAt = entityType!.FindProperty(nameof(Entity.UpdatedAt));
        updatedAt.Should().NotBeNull();
        updatedAt!.IsConcurrencyToken.Should().BeTrue();
    }

    private static ITestThing NewThing(string name)
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        return new TestThing { Id = Guid.NewGuid(), CreatedAt = now, UpdatedAt = now, Name = name };
    }
}
