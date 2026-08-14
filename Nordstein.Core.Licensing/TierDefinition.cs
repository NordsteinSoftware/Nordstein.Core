namespace Nordstein.Core.Licensing;

/// <summary>
/// The feature set and limits granted by a particular license tier, in the product's canonical
/// string vocabulary (see <see cref="ILicenseTierPolicy"/>).
/// </summary>
public sealed record TierDefinition(
    IReadOnlySet<string> Features,
    IReadOnlyDictionary<string, long> Limits);
