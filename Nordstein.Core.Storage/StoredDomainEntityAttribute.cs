namespace Nordstein.Core.Storage;

/// <summary>
/// Associates a stored entity with the domain entity it maps to. The assembly-scoped discovery in
/// <see cref="StorageFoundationModule{TContext}"/> reads this to pair a stored type with its
/// domain interface (and therefore its repository).
/// </summary>
[AttributeUsage(AttributeTargets.Class)]
public sealed class StoredDomainEntityAttribute : Attribute
{
    /// <summary>The domain entity type this stored entity maps to.</summary>
    public Type DomainEntityType { get; }

    public StoredDomainEntityAttribute(Type domainEntityType)
    {
        DomainEntityType = domainEntityType;
    }
}
