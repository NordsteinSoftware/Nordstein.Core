using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Nordstein.Core.Domain;
using Nordstein.Core.Testing;

namespace Nordstein.Core.Storage.Tests;

/// <summary>
/// Coverage of <see cref="ArchivableRepository{TDomainEntity,TStoredEntity}"/>: soft-delete, the
/// list-query exclusion of archived rows (while by-id lookups still resolve them), unarchive, and the
/// hard-delete refusal on an archive-only repository.
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
        reloaded.Should().NotBeNull();
        reloaded!.IsArchived.Should().BeTrue();
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
        (await repository.FindAsync(doc.Id, CancellationToken))!.IsArchived.Should().BeFalse();
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
    public async Task ArchiveAsync_OnArchiveOnlyRepository_StillWorks()
    {
        IServiceProvider services = GetServices();
        var repository = services.GetRequiredService<IArchivableRepository<ITestLockedDoc>>();
        ITestLockedDoc doc = await repository.AddAsync(NewLockedDoc("permanent"), CancellationToken);

        (await repository.ArchiveAsync(doc.Id, CancellationToken)).Should().BeTrue();
        (await repository.GetAllAsync(CancellationToken)).Should().BeEmpty();
    }
}
