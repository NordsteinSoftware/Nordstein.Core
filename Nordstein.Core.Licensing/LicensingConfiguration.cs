namespace Nordstein.Core.Licensing;

/// <summary>
/// Configuration for the licensing engine, supplied by the consuming product's composition
/// root. The issuer, audience, and trusted public keys are product identity — the engine only
/// enforces them.
/// </summary>
public sealed record LicensingConfiguration
{
    /// <summary>
    /// The exact <c>iss</c> value a license JWT must carry to be accepted.
    /// </summary>
    public required string Issuer { get; init; }

    /// <summary>
    /// The exact <c>aud</c> value a license JWT must carry to be accepted.
    /// </summary>
    public required string Audience { get; init; }

    /// <summary>
    /// Base URL of the upstream license server used for periodic revocation checks.
    /// </summary>
    public required string ServerUrl { get; init; }

    /// <summary>
    /// The base64 SPKI public keys accepted for verifying license JWT signatures.
    /// </summary>
    public required IReadOnlyList<string> PublicKeys { get; init; }

    /// <summary>
    /// The raw license JWT (from the environment), or null when running the fallback tier.
    /// </summary>
    public string? LicenseJwt { get; init; }

    /// <summary>
    /// A pre-resolved license snapshot that bypasses JWT validation entirely. When set, the
    /// licensing engine adopts it verbatim and performs no online verification. Used by
    /// kiosk/demo deployments to run at a fixed tier without a real signed license.
    /// </summary>
    public LicenseSnapshot? OverrideSnapshot { get; init; }

    /// <summary>
    /// Whether the background service contacts the license server for periodic revocation/grace
    /// checks. When false the startup snapshot (from JWT validation or the override) is kept as-is
    /// and no network calls are made — used by local dev builds to avoid needing the license server.
    /// </summary>
    public bool ServerCheckEnabled { get; init; } = true;

    /// <summary>
    /// How often the background check service contacts the license server.
    /// </summary>
    public int CheckIntervalHours { get; init; } = 24;

    /// <summary>
    /// How long an air-gapped/offline deployment may continue at full tier before
    /// degrading, when the license server is unreachable.
    /// </summary>
    public int OfflineGracePeriodDays { get; init; } = 7;

    /// <summary>
    /// Filesystem path where the last-known-good server check result is cached.
    /// </summary>
    public required string CacheFilePath { get; init; }
}
