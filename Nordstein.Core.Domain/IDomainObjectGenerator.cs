namespace Nordstein.Core.Domain;

/// <summary>
/// Generates domain objects, primarily for test data and seed scenarios.
/// </summary>
public interface IDomainObjectGenerator<TDomainObject> where TDomainObject : IDomainObject
{
    Task<TDomainObject> CreateAsync(CancellationToken cancellationToken = default);
}
