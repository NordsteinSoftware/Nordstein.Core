namespace Nordstein.Core.Domain;

/// <summary>
/// Generates domain objects, primarily for test data and seed scenarios.
/// </summary>
/// <typeparam name="TDomainObject">The type of domain object produced by this generator.</typeparam>
public interface IDomainObjectGenerator<TDomainObject> where TDomainObject : IDomainObject
{
    /// <summary>
    /// Generates and persists a new domain object using domain-specific creation logic.
    /// </summary>
    /// <param name="cancellationToken">Token to observe for cancellation.</param>
    /// <returns>The newly created domain object.</returns>
    Task<TDomainObject> CreateAsync(CancellationToken cancellationToken = default);
}
