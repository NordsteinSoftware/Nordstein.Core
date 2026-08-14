namespace Nordstein.Core.Licensing.Tests;

/// <summary>
/// A minimal product-style tier policy for engine tests: a "Basic" fallback tier and a paid
/// "Premium" tier, with case-insensitive name resolution the way a real product (backed by
/// enum parsing) would provide it.
/// </summary>
internal sealed class TestTierPolicy : ILicenseTierPolicy
{
    public const string Basic = "Basic";
    public const string Premium = "Premium";

    public const string Analytics = "Analytics";
    public const string Sso = "Sso";
    public const string Export = "Export";

    public const string MaxProjects = "MaxProjects";
    public const string MaxUsers = "MaxUsers";

    private static readonly IReadOnlyList<string> Tiers = [Basic, Premium];
    private static readonly IReadOnlyList<string> Features = [Analytics, Sso, Export];
    private static readonly IReadOnlyList<string> Limits = [MaxProjects, MaxUsers];

    private static readonly TierDefinition BasicDefinition = new(
        new HashSet<string>(),
        new Dictionary<string, long>
        {
            [MaxProjects] = 1,
            [MaxUsers] = 1,
        });

    private static readonly TierDefinition PremiumDefinition = new(
        new HashSet<string> { Analytics, Sso, Export },
        new Dictionary<string, long>
        {
            [MaxProjects] = long.MaxValue,
            [MaxUsers] = long.MaxValue,
        });

    public string FallbackTier => Basic;

    public TierDefinition GetDefinition(string tier)
        => tier == Premium ? PremiumDefinition : BasicDefinition;

    public bool TryResolveTier(string? value, out string tier)
        => TryResolve(Tiers, value, out tier);

    public bool TryResolveFeature(string value, out string feature)
        => TryResolve(Features, value, out feature);

    public bool TryResolveLimit(string value, out string limit)
        => TryResolve(Limits, value, out limit);

    private static bool TryResolve(IReadOnlyList<string> known, string? value, out string resolved)
    {
        foreach (var candidate in known)
        {
            if (string.Equals(candidate, value, StringComparison.OrdinalIgnoreCase))
            {
                resolved = candidate;
                return true;
            }
        }

        resolved = string.Empty;
        return false;
    }
}
