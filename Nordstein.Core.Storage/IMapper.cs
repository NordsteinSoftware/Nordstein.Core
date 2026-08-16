using Nordstein.Core.Domain;

namespace Nordstein.Core.Storage;

/// <summary>
/// Maps between a domain entity and its stored (persistence) form in both directions. Each product
/// entity supplies an implementation; the generic repositories depend only on this contract.
/// </summary>
public interface IMapper<TDomainEntity, TStoredEntity>
    where TDomainEntity : IDomainEntity
    where TStoredEntity : class, IEntity
{
    /// <summary>
    /// Maps the EF storage entity <paramref name="storedEntity"/> to its domain entity representation.
    /// </summary>
    /// <param name="storedEntity">The stored entity loaded from the database.</param>
    /// <param name="cancellationToken">Propagates notification that the operation should be cancelled.</param>
    /// <returns>The domain entity produced from the stored form.</returns>
    /// <remarks>
    /// Called by repositories after loading a <typeparamref name="TStoredEntity"/> from the database.
    /// Implementations may perform additional async lookups (e.g. resolving related entities)
    /// through the ambient context; they must not start a new transaction.
    /// </remarks>
    public Task<TDomainEntity> Map(TStoredEntity storedEntity, CancellationToken cancellationToken = default);

    /// <summary>
    /// Maps the domain entity <paramref name="domainEntity"/> to its EF storage entity representation.
    /// </summary>
    /// <param name="domainEntity">The domain entity to persist.</param>
    /// <param name="cancellationToken">Propagates notification that the operation should be cancelled.</param>
    /// <returns>The storage entity ready to be written to the database.</returns>
    /// <remarks>
    /// Called by repositories before saving a <typeparamref name="TDomainEntity"/> to the database.
    /// Implementations must not call <c>SaveChangesAsync</c>; the repository owns the save.
    /// </remarks>
    public Task<TStoredEntity> Map(TDomainEntity domainEntity, CancellationToken cancellationToken = default);
}
