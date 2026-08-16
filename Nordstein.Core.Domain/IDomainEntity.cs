namespace Nordstein.Core.Domain;

/// <summary>
/// Marker interface for a persistent domain object with identity and timestamps.
/// </summary>
public interface IDomainEntity : IDomainEntityData, IDomainObject;

/// <summary>
/// A persistent domain entity that can delegate persistence operations to its repository.
/// </summary>
/// <typeparam name="TSelf">The concrete entity type.</typeparam>
public interface IDomainEntity<TSelf> : IDomainEntity where TSelf : IDomainEntity
{
    /// <summary>
    /// Fetches the latest state of the entity from the repository.
    /// </summary>
    /// <param name="cancellationToken">Token to observe for cancellation.</param>
    /// <returns>The freshly loaded entity instance.</returns>
    /// <exception cref="Exceptions.EntityNotFoundException">
    /// The entity no longer exists in the repository.
    /// </exception>
    Task<TSelf> ReloadAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Persists this entity to the repository as a new record.
    /// </summary>
    /// <param name="cancellationToken">Token to observe for cancellation.</param>
    /// <returns>The persisted entity, as returned by the repository.</returns>
    /// <exception cref="Exceptions.EntityAlreadyExistsException">
    /// An entity with the same id already exists.
    /// </exception>
    Task<TSelf> AddAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Persists the entity's current state to the repository.
    /// </summary>
    /// <param name="cancellationToken">Token to observe for cancellation.</param>
    /// <returns>The updated entity, as returned by the repository.</returns>
    /// <exception cref="Exceptions.EntityNotFoundException">
    /// The entity no longer exists in the repository.
    /// </exception>
    Task<TSelf> UpdateAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Adds or updates the entity in the repository; safe regardless of whether it currently exists.
    /// </summary>
    /// <param name="cancellationToken">Token to observe for cancellation.</param>
    /// <returns>The persisted entity, as returned by the repository.</returns>
    Task<TSelf> UpsertAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Permanently deletes this entity from the repository.
    /// </summary>
    /// <remarks>
    /// For soft deletion, implement <see cref="IArchivable"/> and call
    /// <see cref="IArchivableRepository{T}.ArchiveAsync"/> instead.
    /// </remarks>
    /// <param name="cancellationToken">Token to observe for cancellation.</param>
    Task RemoveAsync(CancellationToken cancellationToken = default);
}
