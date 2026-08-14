namespace Nordstein.Core.Licensing;

/// <summary>
/// Provides the current resolved license and answers feature/limit queries in the product's
/// canonical string vocabulary. Products typically wrap this in a strongly-typed façade.
/// </summary>
public interface ILicenseService
{
    /// <summary>
    /// The current license snapshot. Never null; defaults to the fallback tier.
    /// </summary>
    LicenseSnapshot Current { get; }

    /// <summary>
    /// Raised whenever <see cref="Current"/> changes (e.g. a background check downgrades the
    /// tier). Handlers run synchronously on the thread applying the change, while the engine
    /// holds its internal gate — they must return quickly and must not block on other engine
    /// calls (e.g. waiting on <see cref="ForceRefreshAsync"/> would deadlock).
    /// </summary>
    event Action Changed;

    /// <summary>
    /// Returns true when the given canonical feature name is granted by the current license.
    /// The lookup is by canonical spelling, case-sensitive — use the exact names the
    /// <see cref="ILicenseTierPolicy"/> emits.
    /// </summary>
    bool HasFeature(string feature);

    /// <summary>
    /// Returns the effective value of the given canonical limit name; missing limits read as 0
    /// and <see cref="long.MaxValue"/> means unlimited. The lookup is by canonical spelling,
    /// case-sensitive — use the exact names the <see cref="ILicenseTierPolicy"/> emits.
    /// </summary>
    long GetLimit(string limit);

    /// <summary>
    /// Forces an immediate license server check, updating <see cref="Current"/> if it changed.
    /// </summary>
    Task ForceRefreshAsync(CancellationToken cancellationToken = default);
}
