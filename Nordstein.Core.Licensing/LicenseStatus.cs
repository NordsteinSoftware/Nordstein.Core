namespace Nordstein.Core.Licensing;

/// <summary>
/// The runtime status of the active license.
/// </summary>
public enum LicenseStatus
{
    /// <summary>
    /// No license is configured; the deployment runs the fallback (free) tier.
    /// </summary>
    Free,

    /// <summary>
    /// A validated license is in force.
    /// </summary>
    Active,

    /// <summary>
    /// The license server has been unreachable past the first offline-grace stage; the license
    /// keeps its entitlements until <see cref="LicenseSnapshot.GracePeriodEndsAt"/>.
    /// </summary>
    Grace,

    /// <summary>
    /// The license ended — revoked by the server, or (for an offline-only key) past its
    /// expiry — and the deployment runs with fallback-tier entitlements.
    /// </summary>
    Expired,

    /// <summary>
    /// A license was configured but failed validation (malformed, bad signature, expired, …).
    /// The deployment runs with fallback-tier entitlements until the license is corrected.
    /// </summary>
    Invalid,
}
