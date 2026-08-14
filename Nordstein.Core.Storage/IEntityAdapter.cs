using Nordstein.Core.Domain;

namespace Nordstein.Core.Storage;

/// <summary>
/// Declares the factory delegate a mapper uses to build a domain object from its stored form.
/// </summary>
public interface IEntityAdapter<TDomainObject, TStored>
    where TDomainObject : IDomainObject
{
    delegate TDomainObject Factory(TStored domainObject);
}
