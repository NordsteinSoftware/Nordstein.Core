namespace Nordstein.Core.Domain;

/// <summary>
/// Repository for an entity that supports soft deletion.
/// </summary>
/// <typeparam name="T">The archivable entity type.</typeparam>
public interface IArchivableRepository<T> : IRepository<T> where T : class, IArchivable
{
    /// <summary>
    /// Soft-deletes the entity with the given <paramref name="id"/> by setting
    /// <c>IsArchived = true</c>.
    /// </summary>
    /// <remarks>
    /// Archived entities are excluded from list and paged queries but remain findable by id via
    /// <see cref="IRepository{TDomainEntity}.FindAsync"/>.
    /// </remarks>
    /// <param name="id">The identifier of the entity to archive.</param>
    /// <param name="cancellationToken">Token to observe for cancellation.</param>
    /// <returns>
    /// <see langword="true"/> if the entity was found and archived; <see langword="false"/> if no
    /// entity with <paramref name="id"/> exists.
    /// </returns>
    Task<bool> ArchiveAsync(Guid id, CancellationToken cancellationToken = default);
}
