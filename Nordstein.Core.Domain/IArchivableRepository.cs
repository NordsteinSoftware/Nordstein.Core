namespace Nordstein.Core.Domain;

/// <summary>
/// Repository for an entity that supports soft deletion.
/// </summary>
public interface IArchivableRepository<T> : IRepository<T> where T : class, IArchivable
{
    Task<bool> ArchiveAsync(Guid id, CancellationToken cancellationToken = default);
}
