using Nordstein.Core.Common.Random;

namespace Nordstein.Core.Domain;

public abstract class DomainObjectGenerator<TDomainObject> : IDomainObjectGenerator<TDomainObject>
    where TDomainObject : IDomainObject
{
    protected readonly IRandom random;

    protected DomainObjectGenerator(IRandom random)
    {
        this.random = random;
    }

    public abstract Task<TDomainObject> CreateAsync(CancellationToken cancellationToken = default);
}
