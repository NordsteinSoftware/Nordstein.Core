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
    }
}
