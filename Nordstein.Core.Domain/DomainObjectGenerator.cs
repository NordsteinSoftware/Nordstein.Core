using Nordstein.Core.Common.Random;

namespace Nordstein.Core.Domain;

/// <summary>
/// Abstract base for test and seed data generators that produce domain objects.
/// </summary>
/// <remarks>
/// Subclasses implement <see cref="CreateAsync"/> to provide domain-specific creation logic.
/// An <see cref="IRandom"/> is injected so that generated values can be made deterministic by seed,
/// enabling reproducible test scenarios.
/// </remarks>
/// <typeparam name="TDomainObject">The type of domain object this generator produces.</typeparam>
public abstract class DomainObjectGenerator<TDomainObject> : IDomainObjectGenerator<TDomainObject>
    where TDomainObject : IDomainObject
{
    /// <summary>
    /// The shared deterministic random source used by subclasses to produce generated values.
    /// </summary>
    /// <remarks>
    /// Always use this field rather than <see cref="System.Random"/> directly so that test
    /// scenarios can control the seed and reproduce generated data.
    /// </remarks>
    protected readonly IRandom random;

    /// <summary>
    /// Stores the random source used by subclasses for value generation.
    /// </summary>
    /// <param name="random">The deterministic random source to inject.</param>
    protected DomainObjectGenerator(IRandom random)
    {
        this.random = random;
    }

    /// <inheritdoc/>
    public abstract Task<TDomainObject> CreateAsync(CancellationToken cancellationToken = default);
}
