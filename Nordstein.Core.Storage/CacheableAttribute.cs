namespace Nordstein.Core.Storage;

/// <summary>
/// Marks a stored entity as eligible for in-memory caching by <see cref="IEntityCache{TDomainEntity}"/>.
/// Apply only to slow-changing reference data (few rows, infrequent writes); never to high-volume types.
/// </summary>
[AttributeUsage(AttributeTargets.Class)]
public sealed class CacheableAttribute : Attribute;
