namespace Nordstein.Core.Licensing;

/// <summary>
/// The feature set and limits granted by a particular license tier, in the product's canonical
/// string vocabulary (see <see cref="ILicenseTierPolicy"/>). The exact shape of feature names
/// and limit keys depends on the product's <see cref="ILicenseTierPolicy"/> implementation.
/// </summary>
/// <param name="Features">The set of feature names enabled under this tier.</param>
/// <param name="Limits">The named numeric limits in force under this tier.</param>
public sealed record TierDefinition(
    IReadOnlySet<string> Features,
    IReadOnlyDictionary<string, long> Limits);
