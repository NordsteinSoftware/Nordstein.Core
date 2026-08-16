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

    /// <summary>
    /// Initializes the attribute, associating the decorated stored entity class with its
    /// corresponding domain entity type.
    /// </summary>
    /// <param name="domainEntityType">
    /// The domain entity interface (implementing <c>IDomainEntity</c>) that this stored entity maps
    /// to. Must be an interface — the assembly-scanned discovery in
    /// <see cref="StorageFoundationModule{TContext}"/> uses this to pair stored types with their
    /// repository registrations.
    /// </param>
    public StoredDomainEntityAttribute(Type domainEntityType)
    {
        DomainEntityType = domainEntityType;
    }
}
