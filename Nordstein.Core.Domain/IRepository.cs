using Nordstein.Core.Domain.Exceptions;
using Nordstein.Core.Domain.Paging;

namespace Nordstein.Core.Domain;

/// <summary>
/// Repository for persistent domain entities.
/// </summary>
public interface IRepository<TDomainEntity> where TDomainEntity : IDomainEntity
{
    Task<TDomainEntity?> FindAsync(Guid id, CancellationToken cancellationToken = default);

    Task<bool> ContainsAsync(Guid id, CancellationToken cancellationToken = default);

    Task<int> CountAsync(CancellationToken cancellationToken = default);

    IAsyncEnumerable<TDomainEntity> EnumerateAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<TDomainEntity>> GetAllAsync(CancellationToken cancellationToken = default);

    Task<PagedResult<TDomainEntity>> GetPagedAsync(
        int page,
        int pageSize,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<TDomainEntity>> GetManyAsync(
        IReadOnlyCollection<Guid> primaryKeys,
        bool ignoreMissing = false,
        CancellationToken cancellationToken = default);

    Task<TDomainEntity?> FindFirstAsync(CancellationToken cancellationToken = default);

    /// <exception cref="EntityAlreadyExistsException">The entity already exists.</exception>
    Task<TDomainEntity> AddAsync(
        TDomainEntity entity,
        CancellationToken cancellationToken = default);

    Task AddRangeAsync(
        IReadOnlyCollection<TDomainEntity> entities,
        CancellationToken cancellationToken = default);

    /// <exception cref="EntityNotFoundException">The entity does not exist.</exception>
    Task<TDomainEntity> UpdateAsync(
        TDomainEntity entity,
        CancellationToken cancellationToken = default);

    Task<TDomainEntity> UpsertAsync(
        TDomainEntity entity,
        CancellationToken cancellationToken = default);

    Task<bool> RemoveAsync(Guid id, CancellationToken cancellationToken = default);

    Task RemoveAllAsync(CancellationToken cancellationToken = default);
}
