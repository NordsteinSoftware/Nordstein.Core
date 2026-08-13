namespace Nordstein.Core.Domain;

/// <summary>
/// Generates persistent domain entities.
/// </summary>
public interface IDomainEntityGenerator<TDomainEntity> : IDomainObjectGenerator<TDomainEntity>
    where TDomainEntity : IDomainEntity
{
    Task<TDomainEntity> GetOrCreateAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Generates an entity without adding it to the repository.
    /// </summary>
    Task<TDomainEntity> GenerateAsync(CancellationToken cancellationToken = default);
}
