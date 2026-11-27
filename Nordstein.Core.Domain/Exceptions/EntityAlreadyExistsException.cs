namespace Nordstein.Core.Domain.Exceptions;

/// <summary>
/// Thrown when an Add operation finds a row with the same id already present, or when a write is
/// refused because another row already holds the same unique key.
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

    /// <summary>
    /// Creates the exception for a unique-key clash that the storage engine reported, such as a
    /// racing writer that won a unique index.
    /// </summary>
    /// <param name="entityType">
    /// The type of the entity that was being written; only its <see cref="System.Reflection.MemberInfo.Name"/> goes
    /// into the message.
    /// </param>
    /// <param name="innerException">
    /// The engine's exception, kept as <see cref="Exception.InnerException"/> for diagnosis; may be
    /// <see langword="null"/>. The message never quotes it.
    /// </param>
    /// <exception cref="ArgumentNullException"><paramref name="entityType"/> is <see langword="null"/>.</exception>
    public EntityAlreadyExistsException(Type entityType, Exception? innerException)
        : base(
            $"Entity of type '{(entityType ?? throw new ArgumentNullException(nameof(entityType))).Name}' with the same unique key already exists.",
            innerException)
    {
    }
}
