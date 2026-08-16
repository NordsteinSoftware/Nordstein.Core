using Nordstein.Core.Common.Random;

namespace Nordstein.Core.Domain;

/// <summary>
/// Abstract base for generators that both generate and persist domain entities.
/// </summary>
/// <remarks>
/// Extends <see cref="DomainObjectGenerator{TDomainObject}"/> with repository-backed persistence.
/// Provides <see cref="GetOrCreateAsync"/> for idempotent seeding and a default
/// <see cref="CreateAsync"/> implementation that generates an entity via <see cref="GenerateAsync"/>
/// and then persists it via the injected <see cref="IRepository{TDomainEntity}"/>.
/// </remarks>
/// <typeparam name="TDomainEntity">The entity type this generator produces.</typeparam>
public abstract class DomainEntityGenerator<TDomainEntity> :
    DomainObjectGenerator<TDomainEntity>,
    IDomainEntityGenerator<TDomainEntity>
    where TDomainEntity : IDomainEntity
{
    /// <summary>
    /// The repository used to persist generated entities.
    /// </summary>
    protected readonly IRepository<TDomainEntity> repository;

    /// <summary>
    /// Injects the repository for persistence and the random source for value generation.
    /// </summary>
    /// <param name="repository">The repository used to persist generated entities.</param>
    /// <param name="random">The deterministic random source for generating values.</param>
    protected DomainEntityGenerator(IRepository<TDomainEntity> repository, IRandom random) : base(random)
    {
        this.repository = repository;
    }

    /// <summary>
    /// Generates an entity via <see cref="GenerateAsync"/> and persists it via
    /// <see cref="IRepository{TDomainEntity}.AddAsync"/>.
    /// </summary>
    /// <param name="cancellationToken">Token to observe for cancellation.</param>
    /// <returns>The newly created and persisted entity.</returns>
    public override async Task<TDomainEntity> CreateAsync(CancellationToken cancellationToken = default)
    {
        TDomainEntity instance = await GenerateAsync(cancellationToken);
        return await repository.AddAsync(instance, cancellationToken);
    }

    /// <summary>
    /// Produces an unpersisted entity instance using domain-specific generation logic.
    /// </summary>
    /// <remarks>
    /// Called internally by <see cref="CreateAsync"/>. Can also be called directly when an
    /// in-memory-only instance is needed without writing to the repository.
    /// </remarks>
    /// <param name="cancellationToken">Token to observe for cancellation.</param>
    /// <returns>A new, unpersisted entity instance.</returns>
    public abstract Task<TDomainEntity> GenerateAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the first existing entity if any exists; otherwise calls <see cref="CreateAsync"/>
    /// to generate and persist a new one.
    /// </summary>
    /// <remarks>
    /// Useful for seeding idempotent reference data: the first invocation creates the entity;
    /// subsequent invocations return the persisted instance without creating a duplicate.
    /// </remarks>
    /// <param name="cancellationToken">Token to observe for cancellation.</param>
    /// <returns>
    /// An existing entity from the repository, or a freshly created entity if none was found.
    /// </returns>
    public async Task<TDomainEntity> GetOrCreateAsync(CancellationToken cancellationToken = default)
    {
        TDomainEntity? existing = await repository.FindFirstAsync(cancellationToken);
        return existing is not null ? existing : await CreateAsync(cancellationToken);
    }
}
