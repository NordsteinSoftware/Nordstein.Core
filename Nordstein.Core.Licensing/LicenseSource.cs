namespace Nordstein.Core.Licensing;

/// <summary>
/// Where the currently active license came from.
/// </summary>
public enum LicenseSource
{
    /// <summary>
    /// No license is configured; the deployment runs the fallback (free) tier.
    /// </summary>
    None,

    /// <summary>
    /// The license JWT was supplied via the environment (or configuration file).
    /// </summary>
    Environment,

    /// <summary>
    /// The license JWT was set at runtime and is persisted by the product (e.g. in its
    /// database). A stored license takes precedence over an environment-supplied one.
    /// </summary>
    Stored,

    /// <summary>
    /// A pre-resolved override snapshot (kiosk/demo deployments). Not user-manageable.
    /// </summary>
    Override,
}
