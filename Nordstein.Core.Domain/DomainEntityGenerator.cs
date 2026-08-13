using Nordstein.Core.Common.Random;

namespace Nordstein.Core.Domain;

public abstract class DomainEntityGenerator<TDomainEntity> :
    DomainObjectGenerator<TDomainEntity>,
    IDomainEntityGenerator<TDomainEntity>
    where TDomainEntity : IDomainEntity
{
    protected readonly IRepository<TDomainEntity> repository;

    protected DomainEntityGenerator(IRepository<TDomainEntity> repository, IRandom random) : base(random)
    {
        this.repository = repository;
    }

    public override async Task<TDomainEntity> CreateAsync(CancellationToken cancellationToken = default)
    {
        TDomainEntity instance = await GenerateAsync(cancellationToken);
        return await repository.AddAsync(instance, cancellationToken);
    }

    public abstract Task<TDomainEntity> GenerateAsync(CancellationToken cancellationToken = default);

    public async Task<TDomainEntity> GetOrCreateAsync(CancellationToken cancellationToken = default)
    {
        TDomainEntity? existing = await repository.FindFirstAsync(cancellationToken);
        return existing is not null ? existing : await CreateAsync(cancellationToken);
    }
}
