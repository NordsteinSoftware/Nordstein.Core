namespace Nordstein.Core.Licensing.Internal;

/// <summary>
/// The result of a license server revocation check. Tier and limit names are the raw values
/// from the server response; the check service resolves them through the product policy.
/// </summary>
internal sealed record LicenseCheckResult(
    string Status,
    string? UpdatedTier,
    IReadOnlyDictionary<string, long>? UpdatedLimits,
    DateTimeOffset CheckedAt)
{
    public const string Valid = "valid";
    public const string Revoked = "revoked";
    public const string Unknown = "unknown";
}

/// <summary>
/// Client for the upstream license server's revocation-check endpoint.
/// </summary>
internal interface ILicenseServerClient
{
    /// <summary>
    /// Asks the license server whether the license with the given jti is still valid.
    /// Network/transport failures surface as a "unknown" (transient) result, never an unhandled throw.
    /// </summary>
    Task<LicenseCheckResult> CheckAsync(string jti, string version, CancellationToken cancellationToken);
}
