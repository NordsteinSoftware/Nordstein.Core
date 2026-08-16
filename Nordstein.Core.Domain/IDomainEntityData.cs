namespace Nordstein.Core.Domain;

/// <summary>
/// Data common to all persistent domain entities.
/// </summary>
public interface IDomainEntityData
{
    /// <summary>
    /// Unique identifier of the entity; never <see cref="Guid.Empty"/>.
    /// </summary>
    Guid Id { get; }

    /// <summary>
    /// UTC instant the entity was first persisted; always in the past.
    /// </summary>
    DateTimeOffset CreatedAt { get; }

    /// <summary>
    /// UTC instant of the most recent write; always &gt;= <see cref="CreatedAt"/>.
    /// </summary>
    DateTimeOffset UpdatedAt { get; }

    /// <summary>
    /// <see langword="true"/> when the entity has been soft-deleted.
    /// </summary>
    /// <remarks>
    /// The default implementation returns <see langword="false"/> for entity types that do not
    /// implement <see cref="IArchivable"/>.
    /// </remarks>
    bool IsArchived => false;
}
