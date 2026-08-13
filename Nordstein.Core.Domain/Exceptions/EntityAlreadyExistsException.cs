namespace Nordstein.Core.Domain.Exceptions;

public sealed class EntityAlreadyExistsException : Exception
{
    public EntityAlreadyExistsException(Guid id, Type entityType)
        : base($"Entity of type '{entityType.Name}' with id '{id}' already exists.")
    {
    }
}
