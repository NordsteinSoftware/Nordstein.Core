using Nordstein.Core.Domain.Exceptions;
using Nordstein.Core.Domain.Paging;

namespace Nordstein.Core.Domain;

/// <summary>
/// Repository for persistent domain entities.
/// </summary>
/// <typeparam name="TDomainEntity">The entity type managed by this repository.</typeparam>
public interface IRepository<TDomainEntity> where TDomainEntity : IDomainEntity
{
    /// <summary>
    /// Returns the entity with the given <paramref name="id"/>, or <see langword="null"/> if no
    /// matching entity exists.
    /// </summary>
    /// <param name="id">The identifier to look up.</param>
    /// <param name="cancellationToken">Token to observe for cancellation.</param>
    /// <returns>
    /// The matching entity, or <see langword="null"/> if not found. Never throws on a missing entity.
    /// </returns>
    Task<TDomainEntity?> FindAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns <see langword="true"/> if an entity with the given <paramref name="id"/> exists.
    /// </summary>
    /// <param name="id">The identifier to check.</param>
    /// <param name="cancellationToken">Token to observe for cancellation.</param>
    Task<bool> ContainsAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the total number of entities in the repository.
    /// </summary>
    /// <param name="cancellationToken">Token to observe for cancellation.</param>
    Task<int> CountAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Streams entities one at a time; cancellation is honored between items.
    /// </summary>
    /// <remarks>
    /// Prefer <see cref="GetAllAsync"/> for small or bounded collections, or
    /// <see cref="GetPagedAsync"/> for user-facing paginated lists.
    /// </remarks>
    /// <param name="cancellationToken">Token to observe for cancellation.</param>
    /// <returns>An async sequence of entities.</returns>
    IAsyncEnumerable<TDomainEntity> EnumerateAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns all entities as an in-memory list.
    /// </summary>
    /// <remarks>
    /// Use only when the collection is known to be small and bounded; prefer
    /// <see cref="EnumerateAsync"/> for large or unbounded sets.
    /// </remarks>
    /// <param name="cancellationToken">Token to observe for cancellation.</param>
    Task<IReadOnlyList<TDomainEntity>> GetAllAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns a page of entities ordered by <c>CreatedAt</c> descending; archived entities are
    /// excluded.
    /// </summary>
    /// <remarks>
    /// <paramref name="page"/> is 1-based and is clamped to &gt;= 1.
    /// <paramref name="pageSize"/> is clamped to the range [1, 100].
    /// </remarks>
    /// <param name="page">The 1-based page number to retrieve.</param>
    /// <param name="pageSize">The maximum number of items per page.</param>
    /// <param name="cancellationToken">Token to observe for cancellation.</param>
    Task<PagedResult<TDomainEntity>> GetPagedAsync(
        int page,
        int pageSize,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the entities for the given <paramref name="primaryKeys"/>.
    /// </summary>
    /// <remarks>
    /// By default, throws <see cref="EntitiesNotFoundException"/> when any requested id is not
    /// found. Pass <paramref name="ignoreMissing"/> = <see langword="true"/> to silently omit
    /// missing ids from the result instead of throwing.
    /// </remarks>
    /// <param name="primaryKeys">The set of ids to retrieve.</param>
    /// <param name="ignoreMissing">
    /// When <see langword="true"/>, missing ids are skipped rather than causing an exception.
    /// </param>
    /// <param name="cancellationToken">Token to observe for cancellation.</param>
    /// <exception cref="EntitiesNotFoundException">
    /// One or more ids were not found and <paramref name="ignoreMissing"/> is <see langword="false"/>.
    /// </exception>
    Task<IReadOnlyList<TDomainEntity>> GetManyAsync(
        IReadOnlyCollection<Guid> primaryKeys,
        bool ignoreMissing = false,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns an arbitrary entity, or <see langword="null"/> if the repository is empty.
    /// </summary>
    /// <remarks>
    /// Useful for singleton-style "get or create" logic where any existing instance is acceptable.
    /// </remarks>
    /// <param name="cancellationToken">Token to observe for cancellation.</param>
    Task<TDomainEntity?> FindFirstAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Persists a new entity to the repository.
    /// </summary>
    /// <param name="entity">The entity to add.</param>
    /// <param name="cancellationToken">Token to observe for cancellation.</param>
    /// <returns>The persisted entity, as stored.</returns>
    /// <exception cref="EntityAlreadyExistsException">The entity already exists.</exception>
    Task<TDomainEntity> AddAsync(
        TDomainEntity entity,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Persists multiple new entities in a single operation.
    /// </summary>
    /// <remarks>
    /// No exception is thrown when <paramref name="entities"/> is empty; the call is a no-op in
    /// that case.
    /// </remarks>
    /// <param name="entities">The entities to add; may be empty.</param>
    /// <param name="cancellationToken">Token to observe for cancellation.</param>
    Task AddRangeAsync(
        IReadOnlyCollection<TDomainEntity> entities,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Persists changes to an existing entity.
    /// </summary>
    /// <param name="entity">The entity with updated state.</param>
    /// <param name="cancellationToken">Token to observe for cancellation.</param>
    /// <returns>The updated entity, as stored.</returns>
    /// <exception cref="EntityNotFoundException">The entity does not exist.</exception>
    Task<TDomainEntity> UpdateAsync(
        TDomainEntity entity,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Adds or updates the entity; safe regardless of whether the entity currently exists.
    /// </summary>
    /// <param name="entity">The entity to add or update.</param>
    /// <param name="cancellationToken">Token to observe for cancellation.</param>
    /// <returns>The persisted entity, as stored.</returns>
    Task<TDomainEntity> UpsertAsync(
        TDomainEntity entity,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Permanently deletes the entity with the given <paramref name="id"/>.
    /// </summary>
    /// <remarks>
    /// Returns <see langword="false"/> if no entity with <paramref name="id"/> was found; does not
    /// throw on a missing id.
    /// </remarks>
    /// <param name="id">The identifier of the entity to delete.</param>
    /// <param name="cancellationToken">Token to observe for cancellation.</param>
    /// <returns>
    /// <see langword="true"/> if the entity was found and deleted; <see langword="false"/> if it
    /// did not exist.
    /// </returns>
    Task<bool> RemoveAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Permanently deletes every entity in the repository.
    /// </summary>
    /// <remarks>
    /// This operation is irreversible. Use with care.
    /// </remarks>
    /// <param name="cancellationToken">Token to observe for cancellation.</param>
    Task RemoveAllAsync(CancellationToken cancellationToken = default);
}
