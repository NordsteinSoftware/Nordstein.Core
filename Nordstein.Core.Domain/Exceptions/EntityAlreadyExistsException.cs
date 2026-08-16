namespace Nordstein.Core.Domain.Exceptions;

/// <summary>
/// Thrown when an Add operation finds a row with the same id already present.
/// </summary>
public sealed class EntityAlreadyExistsException : Exception
{
    /// <summary>
    /// Creates the exception with a message identifying the entity type and id.
    /// </summary>
    /// <param name="id">The id that already exists in the repository.</param>
    /// <param name="entityType">The CLR type of the entity that was being added.</param>
    public EntityAlreadyExistsException(Guid id, Type entityType)
        : base($"Entity of type '{entityType.Name}' with id '{id}' already exists.")
    {
    }
}
