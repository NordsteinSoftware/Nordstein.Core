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
/// <param name="Tier">The license tier name in the product's canonical vocabulary.</param>
/// <param name="Status">The current validity status of the license.</param>
/// <param name="ExpiresAt">When the license expires, or <see langword="null"/> for perpetual licenses.</param>
/// <param name="GracePeriodEndsAt">When the offline grace period ends, or <see langword="null"/> if no grace period is in effect.</param>
/// <param name="CustomerEmail">The email address of the license holder, or <see langword="null"/> if not present in the JWT.</param>
/// <param name="Jti">The unique JWT ID (<c>jti</c> claim) of the license token, or <see langword="null"/> if absent.</param>
/// <param name="Features">The set of feature names enabled under this license.</param>
/// <param name="Limits">The named numeric limits in force under this license.</param>
/// <param name="Source">Where the license was resolved from; defaults to <see cref="LicenseSource.None"/>.</param>
/// <param name="InvalidReason">The human-readable reason a license was rejected; <see langword="null"/> when the license is valid.</param>
/// <param name="Offline">Whether this is an air-gapped, server-check-exempt license; <see langword="false"/> for normal online licenses.</param>
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
    /// Creates a valid offline fallback snapshot using the policy's fallback tier; used when
    /// the server check fails but an offline grace period applies.
    /// </summary>
    /// <param name="policy">The tier policy supplying the fallback tier name and its definition; must not be <see langword="null"/>.</param>
    /// <returns>A new <see cref="LicenseSnapshot"/> at the fallback tier with <see cref="LicenseStatus.Free"/>.</returns>
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
    /// deployment keeps running while the product can surface the problem. The tier used is
    /// the policy's default restricted tier.
    /// </summary>
    /// <param name="policy">The tier policy supplying the fallback tier name and its definition; must not be <see langword="null"/>.</param>
    /// <param name="source">The source the invalid license was resolved from.</param>
    /// <param name="reason">A human-readable description of why the license was rejected.</param>
    /// <returns>A new <see cref="LicenseSnapshot"/> at the fallback tier with <see cref="LicenseStatus.Invalid"/>.</returns>
    public static LicenseSnapshot Invalid(ILicenseTierPolicy policy, LicenseSource source, string reason)
        => Fallback(policy) with
        {
            Status = LicenseStatus.Invalid,
            Source = source,
            InvalidReason = reason,
        };
}
