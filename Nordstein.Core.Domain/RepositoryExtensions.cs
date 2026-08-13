using Nordstein.Core.Domain.Exceptions;

namespace Nordstein.Core.Domain;

public static class RepositoryExtensions
{
    public static async Task<TDomainEntity> GetAsync<TDomainEntity>(
        this IRepository<TDomainEntity> repository,
        Guid id,
        CancellationToken cancellationToken = default)
        where TDomainEntity : IDomainEntity
    {
        TDomainEntity? entity = await repository.FindAsync(id, cancellationToken);
        return entity ?? throw new EntityNotFoundException(id, typeof(TDomainEntity));
    }
}
