namespace Nordstein.Core.Domain.Exceptions;

public sealed class EntityNotFoundException : Exception
{
    public EntityNotFoundException(Guid id, Type entityType)
        : base($"The {entityType.Name} with id '{id}' was not found.")
    {
    }
}
