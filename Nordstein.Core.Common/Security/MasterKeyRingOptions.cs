using System.ComponentModel.DataAnnotations;

namespace Nordstein.Core.Common.Security;

/// <summary>
/// Where the master key ring lives and where it must never live. The host registers one instance; Core
/// registers none, so <see cref="IMasterKeyRing"/> and <see cref="IMasterKeyRingInitializer"/> resolve only in a
/// container that has one.
/// </summary>
/// <remarks>
/// Immutable once built and thread-safe. <see cref="Validate"/> runs when the ring or the initializer is
/// resolved, so invalid options fail resolution with a <see cref="ValidationException"/>; resolving does no
/// I/O. Use absolute paths: a relative one resolves against the process's current directory.
/// </remarks>
public sealed record MasterKeyRingOptions : IValidatableObject
{
    /// <summary>
    /// The default <see cref="FileName"/>.
    /// </summary>
    public const string DefaultFileName = "master-key-ring.json";

    /// <summary>
    /// The directory that holds the ring file: mode <c>0700</c> on Unix, holding nothing else.
    /// </summary>
    public required string KeyDirectory { get; init; }

    /// <summary>
    /// The ring file's name inside <see cref="KeyDirectory"/>: one path segment. <see cref="DefaultFileName"/> by
    /// default.
    /// </summary>
    public string FileName { get; init; } = DefaultFileName;

    /// <summary>
    /// Directories the ring file must never be inside, after both are resolved through links and compared
    /// ignoring case and normalization (<see cref="Io.IPathContainment"/>): anything that is backed up, served
    /// or shared, so a copy of it never carries the master keys. Empty by default.
    /// </summary>
    public IReadOnlyList<string> ExcludedRoots { get; init; } = [];

    /// <summary>
    /// Refuses a key directory that is blank or holds U+0000, a file name that is blank, <c>.</c>, <c>..</c>
    /// or holds a directory separator or U+0000, and a missing list or an excluded root that is blank or holds
    /// U+0000.
    /// </summary>
    /// <param name="validationContext">The validation context (unused).</param>
    /// <returns>The broken rules; empty when every rule holds.</returns>
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (string.IsNullOrWhiteSpace(KeyDirectory) || KeyDirectory.Contains('\0', StringComparison.Ordinal))
        {
            yield return new ValidationResult(
                $"{nameof(KeyDirectory)} must not be blank or hold U+0000", [nameof(KeyDirectory)]);
        }

        if (string.IsNullOrWhiteSpace(FileName)
            || FileName is "." or ".."
            || FileName.IndexOfAny(['/', '\\', '\0']) >= 0)
        {
            yield return new ValidationResult(
                $"{nameof(FileName)} must be one path segment: not blank, '.' or '..', and without a separator or U+0000",
                [nameof(FileName)]);
        }

        if (ExcludedRoots is null
            || ExcludedRoots.Any(root => string.IsNullOrWhiteSpace(root) || root.Contains('\0', StringComparison.Ordinal)))
        {
            yield return new ValidationResult(
                $"{nameof(ExcludedRoots)} must be a list of paths that are not blank and hold no U+0000",
                [nameof(ExcludedRoots)]);
        }
    }
}
