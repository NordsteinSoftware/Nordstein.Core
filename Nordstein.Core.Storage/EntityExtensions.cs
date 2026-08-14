namespace Nordstein.Core.Storage;

/// <summary>
/// Extensions for stored-entity types.
/// </summary>
public static class EntityExtensions
{
    /// <summary>
    /// Returns the domain entity type a stored entity type maps to (via
    /// <see cref="StoredDomainEntityAttribute"/>), or <c>null</c> when it declares none.
    /// </summary>
    public static Type? GetDomainEntityType(this Type storedEntityType)
        => storedEntityType
            .GetCustomAttributes(typeof(StoredDomainEntityAttribute), false)
            .OfType<StoredDomainEntityAttribute>()
            .FirstOrDefault()?.DomainEntityType;
}
