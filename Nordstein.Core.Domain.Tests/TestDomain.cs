using System.ComponentModel.DataAnnotations;
using Nordstein.Core.Common.Random;

namespace Nordstein.Core.Domain.Tests;

internal interface ITestEntity : IArchivable;

internal sealed record TestEntity : DomainEntity<ITestEntity>, ITestEntity
{
    public TestEntity(IRepository<ITestEntity> repository) : base(repository)
    {
    }

    public TestEntity(IDomainEntityData existing, IRepository<ITestEntity> repository) : base(existing, repository)
    {
    }
}

internal sealed class TestEntityGenerator : DomainEntityGenerator<ITestEntity>
{
    public TestEntityGenerator(IRepository<ITestEntity> repository, IRandom random) : base(repository, random)
    {
    }

    public override Task<ITestEntity> GenerateAsync(CancellationToken cancellationToken = default)
        => Task.FromResult<ITestEntity>(new TestEntity(repository));
}

internal sealed class TestEntityRepository : IRepository<ITestEntity>
{
    private readonly List<ITestEntity> entities = [];

    public Task<ITestEntity?> FindAsync(Guid id, CancellationToken cancellationToken = default)
        => Task.FromResult(entities.FirstOrDefault(entity => entity.Id == id));

    public Task<bool> ContainsAsync(Guid id, CancellationToken cancellationToken = default)
        => Task.FromResult(entities.Any(entity => entity.Id == id));

    public Task<int> CountAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(entities.Count);

    public async IAsyncEnumerable<ITestEntity> EnumerateAsync(
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        foreach (ITestEntity entity in entities)
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return entity;
        }

        await Task.CompletedTask;
    }

    public Task<IReadOnlyList<ITestEntity>> GetAllAsync(CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<ITestEntity>>(entities.ToArray());

    public Task<Paging.PagedResult<ITestEntity>> GetPagedAsync(
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
        => Task.FromResult(new Paging.PagedResult<ITestEntity>(entities.ToArray(), entities.Count, page, pageSize));

    public Task<IReadOnlyList<ITestEntity>> GetManyAsync(
        IReadOnlyCollection<Guid> primaryKeys,
        bool ignoreMissing = false,
        CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<ITestEntity>>(
            entities.Where(entity => primaryKeys.Contains(entity.Id)).ToArray());

    public Task<ITestEntity?> FindFirstAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(entities.FirstOrDefault());

    public Task<ITestEntity> AddAsync(ITestEntity entity, CancellationToken cancellationToken = default)
    {
        entities.Add(entity);
        return Task.FromResult(entity);
    }

    public Task AddRangeAsync(
        IReadOnlyCollection<ITestEntity> newEntities,
        CancellationToken cancellationToken = default)
    {
        entities.AddRange(newEntities);
        return Task.CompletedTask;
    }

    public Task<ITestEntity> UpdateAsync(ITestEntity entity, CancellationToken cancellationToken = default)
    {
        int index = entities.FindIndex(existing => existing.Id == entity.Id);
        if (index >= 0)
        {
            entities[index] = entity;
        }

        return Task.FromResult(entity);
    }

    public Task<ITestEntity> UpsertAsync(ITestEntity entity, CancellationToken cancellationToken = default)
    {
        int index = entities.FindIndex(existing => existing.Id == entity.Id);
        if (index >= 0)
        {
            entities[index] = entity;
        }
        else
        {
            entities.Add(entity);
        }

        return Task.FromResult(entity);
    }

    public Task<bool> RemoveAsync(Guid id, CancellationToken cancellationToken = default)
        => Task.FromResult(entities.RemoveAll(entity => entity.Id == id) > 0);

    public Task RemoveAllAsync(CancellationToken cancellationToken = default)
    {
        entities.Clear();
        return Task.CompletedTask;
    }
}

internal sealed record ExistingEntityData(
    Guid Id,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    bool IsArchived) : IDomainEntityData;
