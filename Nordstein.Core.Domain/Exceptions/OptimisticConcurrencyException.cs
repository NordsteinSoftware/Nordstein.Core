namespace Nordstein.Core.Domain.Exceptions;

/// <summary>
/// Thrown when a concurrent write is detected via the <c>UpdatedAt</c> optimistic-concurrency
/// token.
/// </summary>
/// <remarks>
/// The caller should reload the entity from the repository and retry the operation with the
/// fresh state.
/// </remarks>
public sealed class OptimisticConcurrencyException : Exception
{
    /// <summary>
    /// Creates the exception with a message identifying the entity type and id.
    /// </summary>
    /// <param name="id">The id of the entity that experienced the concurrency conflict.</param>
    /// <param name="entityType">The CLR type of the entity involved in the conflict.</param>
    public OptimisticConcurrencyException(Guid id, Type entityType)
        : base($"The {entityType.Name} with id '{id}' was modified by another process.")
    {
        EntityId = id;
        EntityType = entityType;
    }

    /// <summary>
    /// Creates the exception with a message identifying the entity type and id, wrapping an inner
    /// exception (typically an EF <c>DbUpdateConcurrencyException</c>).
    /// </summary>
    /// <param name="id">The id of the entity that experienced the concurrency conflict.</param>
    /// <param name="entityType">The CLR type of the entity involved in the conflict.</param>
    /// <param name="innerException">The underlying exception from the storage layer.</param>
    public OptimisticConcurrencyException(Guid id, Type entityType, Exception innerException)
        : base($"The {entityType.Name} with id '{id}' was modified by another process.", innerException)
    {
        EntityId = id;
        EntityType = entityType;
    }

    /// <summary>The id of the entity whose write lost the race, as passed to the constructor.</summary>
    public Guid EntityId { get; }

    /// <summary>
    /// The CLR type passed to the constructor (often the entity's public interface), so a caller can
    /// map the conflict per entity kind without parsing <see cref="Exception.Message"/>.
    /// </summary>
    public Type EntityType { get; }
}
