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
    public Task<TDomainEntity> Map(TStoredEntity storedEntity, CancellationToken cancellationToken = default);
    public Task<TStoredEntity> Map(TDomainEntity domainEntity, CancellationToken cancellationToken = default);
}
