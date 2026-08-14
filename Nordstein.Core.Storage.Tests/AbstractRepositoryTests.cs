using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Nordstein.Core.Domain;
using Nordstein.Core.Domain.Exceptions;
using Nordstein.Core.Domain.Paging;
using Nordstein.Core.Testing;

namespace Nordstein.Core.Storage.Tests;

/// <summary>
/// Coverage of the generic query, bulk and mutation surface of
/// <see cref="AbstractRepository{TDomainEntity,TStoredEntity}"/> against an in-memory context.
/// </summary>
[TestClass]
public sealed class AbstractRepositoryTests : BaseTest<Module>
{
    private static IRepository<ITestThing> Repo(IServiceProvider services)
        => services.GetRequiredService<IRepository<ITestThing>>();

    private static ITestThing NewThing(string name, DateTimeOffset? createdAt = null)
    {
        DateTimeOffset now = createdAt ?? DateTimeOffset.UtcNow;
        return new TestThing { Id = Guid.NewGuid(), CreatedAt = now, UpdatedAt = now, Name = name };
    }

    [TestMethod]
    public async Task GetAllAsync_ReturnsEveryRow()
    {
        IServiceProvider services = GetServices();
        IRepository<ITestThing> repository = Repo(services);
        await repository.AddAsync(NewThing("a"), CancellationToken);
        await repository.AddAsync(NewThing("b"), CancellationToken);

        IReadOnlyList<ITestThing> all = await repository.GetAllAsync(CancellationToken);

        all.Select(t => t.Name).Should().BeEquivalentTo(["a", "b"]);
    }

    [TestMethod]
    public async Task GetPagedAsync_ReturnsPageWithTotal_NewestFirst()
    {
        IServiceProvider services = GetServices();
        IRepository<ITestThing> repository = Repo(services);
        var baseTime = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        await repository.AddAsync(NewThing("old", baseTime), CancellationToken);
        await repository.AddAsync(NewThing("new", baseTime.AddHours(1)), CancellationToken);

        PagedResult<ITestThing> page = await repository.GetPagedAsync(1, 1, CancellationToken);

        page.Total.Should().Be(2);
        page.Items.Should().ContainSingle();
        page.Items[0].Name.Should().Be("new"); // ordered by CreatedAt descending
    }

    [TestMethod]
    public async Task GetPagedAsync_ClampsOutOfRangeArguments()
    {
        IServiceProvider services = GetServices();
        IRepository<ITestThing> repository = Repo(services);
        await repository.AddAsync(NewThing("only"), CancellationToken);

        // page 0 and a huge page size are clamped rather than throwing or returning nothing.
        PagedResult<ITestThing> page = await repository.GetPagedAsync(0, 100_000, CancellationToken);

        page.Items.Should().ContainSingle();
        page.Total.Should().Be(1);
    }

    [TestMethod]
    public async Task GetManyAsync_ReturnsRequestedSubset()
    {
        IServiceProvider services = GetServices();
        IRepository<ITestThing> repository = Repo(services);
        ITestThing a = await repository.AddAsync(NewThing("a"), CancellationToken);
        await repository.AddAsync(NewThing("b"), CancellationToken);
        ITestThing c = await repository.AddAsync(NewThing("c"), CancellationToken);

        IReadOnlyList<ITestThing> many = await repository.GetManyAsync([a.Id, c.Id], cancellationToken: CancellationToken);

        many.Select(t => t.Name).Should().BeEquivalentTo(["a", "c"]);
    }

    [TestMethod]
    public async Task GetManyAsync_WhenMissingAndIgnoreMissing_DropsTheMissingIds()
    {
        IServiceProvider services = GetServices();
        IRepository<ITestThing> repository = Repo(services);
        ITestThing a = await repository.AddAsync(NewThing("a"), CancellationToken);

        IReadOnlyList<ITestThing> many = await repository.GetManyAsync(
            [a.Id, Guid.NewGuid()], ignoreMissing: true, CancellationToken);

        many.Should().ContainSingle();
    }

    [TestMethod]
    public async Task GetManyAsync_WhenMissingAndNotIgnored_Throws()
    {
        IServiceProvider services = GetServices();
        IRepository<ITestThing> repository = Repo(services);
        ITestThing a = await repository.AddAsync(NewThing("a"), CancellationToken);

        await FluentActions
            .Invoking(() => repository.GetManyAsync([a.Id, Guid.NewGuid()], cancellationToken: CancellationToken))
            .Should().ThrowAsync<EntitiesNotFoundException>();
    }

    [TestMethod]
    public async Task EnumerateAsync_StreamsEveryRow()
    {
        IServiceProvider services = GetServices();
        IRepository<ITestThing> repository = Repo(services);
        await repository.AddAsync(NewThing("a"), CancellationToken);
        await repository.AddAsync(NewThing("b"), CancellationToken);

        var names = new List<string>();
        await foreach (ITestThing thing in repository.EnumerateAsync(CancellationToken))
        {
            names.Add(thing.Name);
        }

        names.Should().BeEquivalentTo(["a", "b"]);
    }

    [TestMethod]
    public async Task FindFirstAsync_ReturnsTheOldestRow_OrNullWhenEmpty()
    {
        IServiceProvider services = GetServices();
        IRepository<ITestThing> repository = Repo(services);
        (await repository.FindFirstAsync(CancellationToken)).Should().BeNull();

        var baseTime = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        await repository.AddAsync(NewThing("new", baseTime.AddHours(1)), CancellationToken);
        await repository.AddAsync(NewThing("old", baseTime), CancellationToken);

        ITestThing? first = await repository.FindFirstAsync(CancellationToken);
        first!.Name.Should().Be("old"); // ordered by CreatedAt ascending
    }

    [TestMethod]
    public async Task ContainsAsync_ReflectsPresence()
    {
        IServiceProvider services = GetServices();
        IRepository<ITestThing> repository = Repo(services);
        ITestThing a = await repository.AddAsync(NewThing("a"), CancellationToken);

        (await repository.ContainsAsync(a.Id, CancellationToken)).Should().BeTrue();
        (await repository.ContainsAsync(Guid.NewGuid(), CancellationToken)).Should().BeFalse();
    }

    [TestMethod]
    public async Task FindAsync_WhenAbsent_ReturnsNull()
    {
        IServiceProvider services = GetServices();
        (await Repo(services).FindAsync(Guid.NewGuid(), CancellationToken)).Should().BeNull();
    }

    [TestMethod]
    public async Task AddRangeAsync_WithNoEntities_IsANoOp()
    {
        IServiceProvider services = GetServices();
        IRepository<ITestThing> repository = Repo(services);

        await repository.AddRangeAsync([], CancellationToken);

        (await repository.CountAsync(CancellationToken)).Should().Be(0);
    }

    [TestMethod]
    public async Task AddRangeAsync_AddsEveryEntity()
    {
        IServiceProvider services = GetServices();
        IRepository<ITestThing> repository = Repo(services);

        await repository.AddRangeAsync([NewThing("a"), NewThing("b"), NewThing("c")], CancellationToken);

        (await repository.CountAsync(CancellationToken)).Should().Be(3);
    }

    [TestMethod]
    public async Task AddRangeAsync_WithAnExistingId_Throws()
    {
        IServiceProvider services = GetServices();
        IRepository<ITestThing> repository = Repo(services);
        ITestThing existing = await repository.AddAsync(NewThing("a"), CancellationToken);

        await FluentActions
            .Invoking(() => repository.AddRangeAsync([existing, NewThing("b")], CancellationToken))
            .Should().ThrowAsync<EntityAlreadyExistsException>();
    }

    [TestMethod]
    public async Task UpsertAsync_InsertsWhenAbsentAndUpdatesWhenPresent()
    {
        IServiceProvider services = GetServices();
        IRepository<ITestThing> repository = Repo(services);
        ITestThing thing = NewThing("v1");

        ITestThing inserted = await repository.UpsertAsync(thing, CancellationToken);
        inserted.Name.Should().Be("v1");
        (await repository.CountAsync(CancellationToken)).Should().Be(1);

        var updated = new TestThing
        {
            Id = inserted.Id,
            CreatedAt = inserted.CreatedAt,
            UpdatedAt = inserted.UpdatedAt,
            Name = "v2",
        };
        ITestThing upserted = await repository.UpsertAsync(updated, CancellationToken);

        upserted.Name.Should().Be("v2");
        (await repository.CountAsync(CancellationToken)).Should().Be(1);
    }

    [TestMethod]
    public async Task RemoveAllAsync_ClearsEveryRow()
    {
        IServiceProvider services = GetServices();
        IRepository<ITestThing> repository = Repo(services);
        await repository.AddAsync(NewThing("a"), CancellationToken);
        await repository.AddAsync(NewThing("b"), CancellationToken);

        await repository.RemoveAllAsync(CancellationToken);

        (await repository.CountAsync(CancellationToken)).Should().Be(0);
    }

    [TestMethod]
    public async Task UpdateAsync_WhenAbsent_Throws()
    {
        IServiceProvider services = GetServices();
        IRepository<ITestThing> repository = Repo(services);

        await FluentActions
            .Invoking(() => repository.UpdateAsync(NewThing("ghost"), CancellationToken))
            .Should().ThrowAsync<EntityNotFoundException>();
    }

    [TestMethod]
    public async Task RemoveAsync_WhenAbsent_ReturnsFalse()
    {
        IServiceProvider services = GetServices();
        (await Repo(services).RemoveAsync(Guid.NewGuid(), CancellationToken)).Should().BeFalse();
    }
}
