using Microsoft.Extensions.Logging;

namespace Nordstein.Core.Licensing.Internal;

internal sealed class LicenseActivator : ILicenseActivator
{
    private readonly IJwtLicenseValidator validator;
    private readonly ConfiguredLicenseResolver resolver;
    private readonly LicenseService licenseService;
    private readonly ILicenseTierPolicy policy;
    private readonly ILogger<LicenseActivator> logger;

    public LicenseActivator(
        IJwtLicenseValidator validator,
        ConfiguredLicenseResolver resolver,
        LicenseService licenseService,
        ILicenseTierPolicy policy,
        ILogger<LicenseActivator> logger)
    {
        this.validator = validator;
        this.resolver = resolver;
        this.licenseService = licenseService;
        this.policy = policy;
        this.logger = logger;
    }

    public LicenseSnapshot Validate(string licenseJwt)
        => validator.Validate(licenseJwt);

    public LicenseSnapshot Activate(string licenseJwt, LicenseSource source)
    {
        var snapshot = validator.Validate(licenseJwt) with { Source = source };
        licenseService.ApplySnapshot(snapshot);
        logger.LogInformation(
            "License activated at runtime: tier {Tier}, customer {Customer}, source {Source}",
            snapshot.Tier,
            snapshot.CustomerEmail,
            source);
        return snapshot;
    }

    public LicenseSnapshot ActivateOrInvalid(string licenseJwt, LicenseSource source)
    {
        try
        {
            return Activate(licenseJwt, source);
        }
        catch (InvalidLicenseException ex)
        {
            logger.LogWarning(
                ex,
                "The {Source} license is invalid ({Reason}); running with {Tier}-tier entitlements until it is corrected",
                source,
                ex.Reason,
                policy.FallbackTier);
            var snapshot = LicenseSnapshot.Invalid(policy, source, ex.Message);
            licenseService.ApplySnapshot(snapshot);
            return snapshot;
        }
    }

    public LicenseSnapshot ActivateConfigured()
    {
        var snapshot = resolver.Resolve();
        licenseService.ApplySnapshot(snapshot);
        return snapshot;
    }
}
