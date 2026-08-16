using Nordstein.Core.Domain.Exceptions;

namespace Nordstein.Core.Domain;

/// <summary>
/// Extension methods that add throwing-on-missing lookups to <see cref="IRepository{TDomainEntity}"/>.
/// </summary>
public static class RepositoryExtensions
{
    /// <summary>
    /// Returns the entity with the given <paramref name="id"/>; throws if not found.
    /// </summary>
    /// <remarks>
    /// Shorthand for <c>FindAsync</c> followed by a null-check throw; prefer this over
    /// <see cref="IRepository{TDomainEntity}.FindAsync"/> when the caller requires the entity
    /// to exist.
    /// </remarks>
    /// <typeparam name="TDomainEntity">The entity type managed by the repository.</typeparam>
    /// <param name="repository">The repository to query.</param>
    /// <param name="id">The identifier of the entity to retrieve.</param>
    /// <param name="cancellationToken">Token to observe for cancellation.</param>
    /// <returns>The entity with the given <paramref name="id"/>.</returns>
    /// <exception cref="EntityNotFoundException">
    /// No entity with <paramref name="id"/> exists in the repository.
    /// </exception>
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
