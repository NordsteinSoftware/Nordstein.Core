using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Nordstein.Core.Domain;
using Nordstein.Core.Testing;

namespace Nordstein.Core.Storage.Tests;

/// <summary>
/// Coverage of <see cref="ArchivableRepository{TDomainEntity,TStoredEntity}"/>: soft-delete, the
/// list-query exclusion of archived rows (while by-id lookups still resolve them), unarchive, the
/// hard-delete refusal on an archive-only repository, and the re-read after the archive lock.
/// </summary>
[TestClass]
public sealed class ArchivableRepositoryTests : BaseTest<Module>
{
    private static ITestDoc NewDoc(string title)
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        return new TestDoc { Id = Guid.NewGuid(), CreatedAt = now, UpdatedAt = now, IsArchived = false, Title = title };
    }

    private static ITestLockedDoc NewLockedDoc(string title)
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        return new TestLockedDoc { Id = Guid.NewGuid(), CreatedAt = now, UpdatedAt = now, IsArchived = false, Title = title };
    }

    [TestMethod]
    public async Task ArchiveAsync_HidesFromListButKeepsByIdLookup()
    {
        IServiceProvider services = GetServices();
        var repository = services.GetRequiredService<ITestDocRepository>();
        ITestDoc doc = await repository.AddAsync(NewDoc("keeper"), CancellationToken);

        (await repository.ArchiveAsync(doc.Id, CancellationToken)).Should().BeTrue();

        // Excluded from the list query...
        (await repository.GetAllAsync(CancellationToken)).Should().BeEmpty();
        (await repository.GetPagedAsync(1, 10, CancellationToken)).Total.Should().Be(0);
        // ...but still resolvable by id, now flagged archived.
        ITestDoc? reloaded = await repository.FindAsync(doc.Id, CancellationToken);
        Assert.IsNotNull(reloaded);
        reloaded.IsArchived.Should().BeTrue();
    }

    [TestMethod]
    public async Task ArchiveAsync_StampsUpdatedAtFromTheContainerClock()
    {
        IServiceProvider services = GetServices();
        var repository = services.GetRequiredService<ITestDocRepository>();
        MutableClock clock = services.GetRequiredService<MutableClock>();
        ITestDoc doc = await repository.AddAsync(NewDoc("stamped"), CancellationToken);
        clock.UtcNow = new DateTimeOffset(2031, 3, 4, 5, 6, 7, TimeSpan.Zero);

        await repository.ArchiveAsync(doc.Id, CancellationToken);

        ITestDoc reloaded = await repository.GetAsync(doc.Id, CancellationToken);
        reloaded.UpdatedAt.Should().Be(clock.UtcNow);
    }

    [TestMethod]
    public async Task Unarchive_StampsUpdatedAtFromTheContainerClock()
    {
        IServiceProvider services = GetServices();
        var repository = services.GetRequiredService<ITestDocRepository>();
        MutableClock clock = services.GetRequiredService<MutableClock>();
        ITestDoc doc = await repository.AddAsync(NewDoc("restamped"), CancellationToken);
        await repository.ArchiveAsync(doc.Id, CancellationToken);
        clock.UtcNow = new DateTimeOffset(2031, 3, 4, 5, 6, 7, TimeSpan.Zero);

        await repository.Unarchive(doc.Id, CancellationToken);

        ITestDoc reloaded = await repository.GetAsync(doc.Id, CancellationToken);
        reloaded.UpdatedAt.Should().Be(clock.UtcNow);
    }

    [TestMethod]
    public async Task ArchiveAsync_WhenAlreadyArchived_ReturnsFalse()
    {
        IServiceProvider services = GetServices();
        var repository = services.GetRequiredService<ITestDocRepository>();
        ITestDoc doc = await repository.AddAsync(NewDoc("keeper"), CancellationToken);
        await repository.ArchiveAsync(doc.Id, CancellationToken);

        (await repository.ArchiveAsync(doc.Id, CancellationToken)).Should().BeFalse();
    }

    [TestMethod]
    public async Task ArchiveAsync_ArchivedByAnotherWriterWhileLocking_ReturnsFalseAndSkipsRelations()
    {
        IServiceProvider services = GetServices();
        var repository = services.GetRequiredService<ITestDocRepository>();
        ArchiveLockProbe probe = services.GetRequiredService<ArchiveLockProbe>();
        ITestDoc doc = await repository.AddAsync(NewDoc("raced"), CancellationToken);
        probe.WhileLocking = () => ArchiveInOtherScopeAsync(services, doc.Id);

        // The caller read the row as active in this transaction, so the context tracks a stale copy.
        bool archived = await services.GetRequiredService<ITransaction>().InvokeAsync(async () =>
        {
            (await repository.FindAsync(doc.Id, CancellationToken)).Should().Match<ITestDoc>(read => !read.IsArchived);
            return await repository.ArchiveAsync(doc.Id, CancellationToken);
        }, CancellationToken);

        archived.Should().BeFalse("the row was archived by the writer the lock waited for");
        probe.RelationsArchived.Should().Be(1, "only the other writer cleaned up the relations");
    }

    [TestMethod]
    public async Task ArchiveAsync_DeletedByAnotherWriterWhileLocking_ReturnsFalseAndSkipsRelations()
    {
        IServiceProvider services = GetServices();
        var repository = services.GetRequiredService<ITestDocRepository>();
        ArchiveLockProbe probe = services.GetRequiredService<ArchiveLockProbe>();
        ITestDoc doc = await repository.AddAsync(NewDoc("vanishing"), CancellationToken);
        probe.WhileLocking = async () =>
        {
            using IServiceScope other = services.CreateScope();
            await other.ServiceProvider.GetRequiredService<ITestDocRepository>().RemoveAsync(doc.Id, CancellationToken);
        };

        bool archived = await services.GetRequiredService<ITransaction>().InvokeAsync(async () =>
        {
            await repository.FindAsync(doc.Id, CancellationToken);
            return await repository.ArchiveAsync(doc.Id, CancellationToken);
        }, CancellationToken);

        archived.Should().BeFalse("the row is gone by the time the lock is held");
        probe.RelationsArchived.Should().Be(0);
    }

    [TestMethod]
    public async Task ArchiveAsync_ActiveRow_TakesTheLockThenArchivesRelations()
    {
        IServiceProvider services = GetServices();
        var repository = services.GetRequiredService<ITestDocRepository>();
        ArchiveLockProbe probe = services.GetRequiredService<ArchiveLockProbe>();
        ITestDoc doc = await repository.AddAsync(NewDoc("plain"), CancellationToken);
        bool locked = false;
        probe.WhileLocking = () =>
        {
            probe.RelationsArchived.Should().Be(0, "the lock comes before the relation clean-up");
            locked = true;
            return Task.CompletedTask;
        };

        (await repository.ArchiveAsync(doc.Id, CancellationToken)).Should().BeTrue();

        locked.Should().BeTrue();
        probe.RelationsArchived.Should().Be(1);
    }

    [TestMethod]
    public async Task ArchiveAsync_WhenMissing_ReturnsFalse()
    {
        IServiceProvider services = GetServices();
        var repository = services.GetRequiredService<ITestDocRepository>();

        (await repository.ArchiveAsync(Guid.NewGuid(), CancellationToken)).Should().BeFalse();
    }

    [TestMethod]
    public async Task Unarchive_RestoresTheRowToListQueries()
    {
        IServiceProvider services = GetServices();
        var repository = services.GetRequiredService<ITestDocRepository>();
        ITestDoc doc = await repository.AddAsync(NewDoc("keeper"), CancellationToken);
        await repository.ArchiveAsync(doc.Id, CancellationToken);

        await repository.Unarchive(doc.Id, CancellationToken);

        (await repository.GetAllAsync(CancellationToken)).Should().ContainSingle();
        ITestDoc? reloaded = await repository.FindAsync(doc.Id, CancellationToken);
        Assert.IsNotNull(reloaded);
        reloaded.IsArchived.Should().BeFalse();
    }

    [TestMethod]
    public async Task Unarchive_WhenNotArchived_IsANoOp()
    {
        IServiceProvider services = GetServices();
        var repository = services.GetRequiredService<ITestDocRepository>();
        ITestDoc doc = await repository.AddAsync(NewDoc("live"), CancellationToken);

        await repository.Unarchive(doc.Id, CancellationToken); // never archived — nothing to do

        (await repository.GetAllAsync(CancellationToken)).Should().ContainSingle();
    }

    [TestMethod]
    public async Task RemoveAsync_WhenHardDeleteAllowed_DeletesTheRow()
    {
        IServiceProvider services = GetServices();
        var repository = services.GetRequiredService<ITestDocRepository>();
        ITestDoc doc = await repository.AddAsync(NewDoc("doomed"), CancellationToken);

        (await repository.RemoveAsync(doc.Id, CancellationToken)).Should().BeTrue();
        (await repository.FindAsync(doc.Id, CancellationToken)).Should().BeNull();
    }

    [TestMethod]
    public async Task RemoveAsync_OnArchiveOnlyRepository_Throws()
    {
        IServiceProvider services = GetServices();
        var repository = services.GetRequiredService<IRepository<ITestLockedDoc>>();
        ITestLockedDoc doc = await repository.AddAsync(NewLockedDoc("permanent"), CancellationToken);

        await FluentActions
            .Invoking(() => repository.RemoveAsync(doc.Id, CancellationToken))
            .Should().ThrowAsync<InvalidOperationException>();

        // The row is untouched — the refusal happens before any delete.
        (await repository.FindAsync(doc.Id, CancellationToken)).Should().NotBeNull();
    }

    [TestMethod]
    public async Task RemoveAllAsync_OnArchiveOnlyRepository_Throws()
    {
        IServiceProvider services = GetServices();
        var repository = services.GetRequiredService<IRepository<ITestLockedDoc>>();
        await repository.AddAsync(NewLockedDoc("permanent"), CancellationToken);

        // The bulk delete is the same escape hatch as RemoveAsync and must be refused the same way,
        // or archive-only history is wiped in one call.
        await FluentActions
            .Invoking(() => repository.RemoveAllAsync(CancellationToken))
            .Should().ThrowAsync<InvalidOperationException>();

        // Every row survives — the refusal happens before any delete.
        (await repository.CountAsync(CancellationToken)).Should().Be(1);
    }

    [TestMethod]
    public async Task ArchiveAsync_OnArchiveOnlyRepository_StillWorks()
    {
        IServiceProvider services = GetServices();
        var repository = services.GetRequiredService<IArchivableRepository<ITestLockedDoc>>();
        ITestLockedDoc doc = await repository.AddAsync(NewLockedDoc("permanent"), CancellationToken);

        (await repository.ArchiveAsync(doc.Id, CancellationToken)).Should().BeTrue();
        (await repository.GetAllAsync(CancellationToken)).Should().BeEmpty();
    }

    private async Task ArchiveInOtherScopeAsync(IServiceProvider services, Guid id)
    {
        using IServiceScope other = services.CreateScope();
        (await other.ServiceProvider.GetRequiredService<ITestDocRepository>().ArchiveAsync(id, CancellationToken))
            .Should().BeTrue();
    }
}
