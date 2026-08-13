namespace Nordstein.Core.Domain.Exceptions;

public sealed class EntitiesNotFoundException : Exception
{
    public EntitiesNotFoundException(IEnumerable<Guid> ids, Type entityType)
        : base($"One or more {entityType.Name} with ids '{string.Join(", ", ids)}' were not found.")
    {
    }
}
