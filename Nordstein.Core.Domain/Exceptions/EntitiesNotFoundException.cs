namespace Nordstein.Core.Domain.Exceptions;

/// <summary>
/// Thrown by <c>GetManyAsync</c> when one or more requested ids have no matching row and
/// <c>ignoreMissing</c> is <see langword="false"/>.
/// </summary>
public sealed class EntitiesNotFoundException : Exception
{
    /// <summary>
    /// Creates the exception with a message listing all missing ids and the entity type.
    /// </summary>
    /// <param name="ids">The ids that were not found in the repository.</param>
    /// <param name="entityType">The CLR type of the entities that were expected.</param>
    public EntitiesNotFoundException(IEnumerable<Guid> ids, Type entityType)
        : base($"One or more {entityType.Name} with ids '{string.Join(", ", ids)}' were not found.")
    {
    }
}
