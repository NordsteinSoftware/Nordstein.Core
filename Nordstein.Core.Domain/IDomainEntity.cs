namespace Nordstein.Core.Domain;

/// <summary>
/// Marker interface for a persistent domain object with identity and timestamps.
/// </summary>
public interface IDomainEntity : IDomainEntityData, IDomainObject;

/// <summary>
/// A persistent domain entity that can delegate persistence operations to its repository.
/// </summary>
public interface IDomainEntity<TSelf> : IDomainEntity where TSelf : IDomainEntity
{
    Task<TSelf> ReloadAsync(CancellationToken cancellationToken = default);

    Task<TSelf> AddAsync(CancellationToken cancellationToken = default);

    Task<TSelf> UpdateAsync(CancellationToken cancellationToken = default);

    Task<TSelf> UpsertAsync(CancellationToken cancellationToken = default);

    Task RemoveAsync(CancellationToken cancellationToken = default);
}
