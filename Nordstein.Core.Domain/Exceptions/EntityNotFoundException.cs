namespace Nordstein.Core.Domain.Exceptions;

/// <summary>
/// Thrown when a single entity lookup by id finds no matching row.
/// </summary>
public sealed class EntityNotFoundException : Exception
{
    /// <summary>
    /// Creates the exception with a message identifying the entity type and id.
    /// </summary>
    /// <param name="id">The id that was looked up but not found.</param>
    /// <param name="entityType">The CLR type of the entity that was expected.</param>
    public EntityNotFoundException(Guid id, Type entityType)
        : base($"The {entityType.Name} with id '{id}' was not found.")
    {
    }
}
