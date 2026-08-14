using Microsoft.Extensions.Logging;

namespace Nordstein.Core.Licensing.Internal;

/// <summary>
/// Resolves the license snapshot from the static configuration (override snapshot, environment
/// JWT, or nothing). An invalid configured JWT never throws — it degrades to fallback-tier
/// entitlements with <see cref="LicenseStatus.Invalid"/> so the host always boots; a stored
/// license set at runtime can then replace it.
/// </summary>
internal sealed class ConfiguredLicenseResolver
{
    private readonly LicensingConfiguration configuration;
    private readonly IJwtLicenseValidator validator;
    private readonly ILicenseTierPolicy policy;
    private readonly ILogger<ConfiguredLicenseResolver> logger;

    public ConfiguredLicenseResolver(
        LicensingConfiguration configuration,
        IJwtLicenseValidator validator,
        ILicenseTierPolicy policy,
        ILogger<ConfiguredLicenseResolver> logger)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(validator);
        ArgumentNullException.ThrowIfNull(policy);
        this.configuration = configuration;
        this.validator = validator;
        this.policy = policy;
        this.logger = logger;
    }

    public LicenseSnapshot Resolve()
    {
        if (configuration.OverrideSnapshot is { } overrideSnapshot)
        {
            logger.LogInformation(
                "License override active: tier {Tier} (no online verification)",
                overrideSnapshot.Tier);
            return overrideSnapshot;
        }

        var jwt = configuration.LicenseJwt?.Trim();
        if (string.IsNullOrEmpty(jwt))
        {
            logger.LogInformation("No license configured; running in {Tier} tier", policy.FallbackTier);
            return LicenseSnapshot.Fallback(policy);
        }

        try
        {
            var snapshot = validator.Validate(jwt) with { Source = LicenseSource.Environment };
            logger.LogInformation(
                "License validated: tier {Tier}, customer {Customer}",
                snapshot.Tier,
                snapshot.CustomerEmail);
            return snapshot;
        }
        catch (InvalidLicenseException ex)
        {
            logger.LogWarning(
                ex,
                "The configured license is invalid ({Reason}); running with {Tier}-tier entitlements until it is corrected",
                ex.Reason,
                policy.FallbackTier);
            return LicenseSnapshot.Invalid(policy, LicenseSource.Environment, ex.Message);
        }
    }
}
