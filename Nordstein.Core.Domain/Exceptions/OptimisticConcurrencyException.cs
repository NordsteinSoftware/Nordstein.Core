namespace Nordstein.Core.Domain.Exceptions;

public sealed class OptimisticConcurrencyException : Exception
{
    public OptimisticConcurrencyException(Guid id, Type entityType)
        : base($"The {entityType.Name} with id '{id}' was modified by another process.")
    {
    }

    public OptimisticConcurrencyException(Guid id, Type entityType, Exception innerException)
        : base($"The {entityType.Name} with id '{id}' was modified by another process.", innerException)
    {
    }
}
