namespace Nordstein.Core.Licensing;

/// <summary>
/// An immutable point-in-time view of the resolved license: tier, status, validity window,
/// the effective features and limits in force, and where the license came from. Tier, feature,
/// and limit names use the product's canonical vocabulary (see <see cref="ILicenseTierPolicy"/>).
/// <para>
/// <c>Offline</c> is true when the license JWT carries the <c>offline: true</c> claim — an
/// air-gapped, server-check-exempt key. For these the background service never contacts the
/// license server (so they cannot be revoked); <c>ExpiresAt</c> is the only thing that ends
/// them. Absent / non-<c>true</c> claim ⇒ false (a normal online license).
/// </para>
/// </summary>
public sealed record LicenseSnapshot(
    string Tier,
    LicenseStatus Status,
    DateTimeOffset? ExpiresAt,
    DateTimeOffset? GracePeriodEndsAt,
    string? CustomerEmail,
    string? Jti,
    IReadOnlySet<string> Features,
    IReadOnlyDictionary<string, long> Limits,
    LicenseSource Source = LicenseSource.None,
    string? InvalidReason = null,
    bool Offline = false)
{
    /// <summary>
    /// Builds the default fallback-tier snapshot used when no license JWT is configured.
    /// </summary>
    public static LicenseSnapshot Fallback(ILicenseTierPolicy policy)
    {
        ArgumentNullException.ThrowIfNull(policy);
        var definition = policy.GetDefinition(policy.FallbackTier);
        return new LicenseSnapshot(
            policy.FallbackTier,
            LicenseStatus.Free,
            ExpiresAt: null,
            GracePeriodEndsAt: null,
            CustomerEmail: null,
            Jti: null,
            definition.Features,
            definition.Limits);
    }

    /// <summary>
    /// Builds the snapshot used when a configured license fails validation: fallback-tier
    /// entitlements with <see cref="LicenseStatus.Invalid"/> and the rejection reason, so the
    /// deployment keeps running while the product can surface the problem.
    /// </summary>
    public static LicenseSnapshot Invalid(ILicenseTierPolicy policy, LicenseSource source, string reason)
        => Fallback(policy) with
        {
            Status = LicenseStatus.Invalid,
            Source = source,
            InvalidReason = reason,
        };
}
