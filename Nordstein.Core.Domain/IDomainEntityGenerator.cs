namespace Nordstein.Core.Domain;

/// <summary>
/// Generates persistent domain entities.
/// </summary>
/// <typeparam name="TDomainEntity">The entity type produced by this generator.</typeparam>
public interface IDomainEntityGenerator<TDomainEntity> : IDomainObjectGenerator<TDomainEntity>
    where TDomainEntity : IDomainEntity
{
    /// <summary>
    /// Returns an existing entity if one can be found; otherwise creates and persists a new one.
    /// </summary>
    /// <remarks>
    /// Useful for seeding idempotent reference data: the first call creates the entity; subsequent
    /// calls return the same persisted instance without duplication.
    /// </remarks>
    /// <param name="cancellationToken">Token to observe for cancellation.</param>
    /// <returns>
    /// An existing entity retrieved from the repository, or a freshly created and persisted entity
    /// if none exists.
    /// </returns>
    Task<TDomainEntity> GetOrCreateAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Generates an entity without adding it to the repository.
    /// </summary>
    Task<TDomainEntity> GenerateAsync(CancellationToken cancellationToken = default);
}
