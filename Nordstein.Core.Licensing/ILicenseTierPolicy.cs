namespace Nordstein.Core.Licensing;

/// <summary>
/// The product-supplied licensing vocabulary and tier policy. The engine is agnostic of what a
/// tier, feature, or limit means; the product defines the canonical names (which are also the
/// JWT claim values — <c>tier</c>, <c>feat</c>, and <c>lim</c> — so they are a wire-format
/// contract) and which entitlements each tier grants.
/// </summary>
public interface ILicenseTierPolicy
{
    /// <summary>
    /// The canonical name of the tier a deployment runs on without (or with an invalid, revoked,
    /// or expired) license, e.g. "Free".
    /// </summary>
    string FallbackTier { get; }

    /// <summary>
    /// Returns the entitlements granted by the given canonical tier. Unknown tiers return the
    /// <see cref="FallbackTier"/> definition rather than throwing.
    /// </summary>
    TierDefinition GetDefinition(string tier);

    /// <summary>
    /// Maps a raw tier value (e.g. from a JWT claim or a license-server response, matched
    /// case-insensitively) onto its canonical name. False when the value names no known tier.
    /// The value is nullable — deliberately, unlike the feature/limit overloads — because the
    /// <c>tier</c> claim can be absent from a token entirely.
    /// </summary>
    bool TryResolveTier(string? value, out string tier);

    /// <summary>
    /// Maps a raw feature value onto its canonical name. False when the value names no known
    /// feature.
    /// </summary>
    bool TryResolveFeature(string value, out string feature);

    /// <summary>
    /// Maps a raw limit name onto its canonical name. False when the value names no known limit.
    /// </summary>
    bool TryResolveLimit(string value, out string limit);
}
