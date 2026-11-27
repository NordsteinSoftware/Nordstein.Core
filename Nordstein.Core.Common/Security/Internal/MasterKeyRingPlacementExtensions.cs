using Nordstein.Core.Common.Io;

namespace Nordstein.Core.Common.Security.Internal;

/// <summary>
/// The one "ring file not inside an excluded root" check, shared by <see cref="MasterKeyRingFile"/> (every load)
/// and <see cref="MasterKeyRingInitializer"/> (before and after the write), so the two cannot drift.
/// </summary>
internal static class MasterKeyRingPlacementExtensions
{
    /// <summary>
    /// Throws <see cref="MasterKeyRingFailure.PathInExcludedRoot"/> when <paramref name="ringPath"/> may lie inside
    /// one of <paramref name="excludedRoots"/> under either reading <see cref="IPathContainment"/> resolves, and
    /// <see cref="MasterKeyRingFailure.FileRejected"/> (cause kept) when a path cannot be resolved, so the caller
    /// fails closed either way. Pass the configured path, not a <see cref="Path.GetFullPath(string)"/> result.
    /// </summary>
    public static void AssertOutsideExcludedRoots(
        this IPathContainment containment, string ringPath, IReadOnlyList<string> excludedRoots)
    {
        foreach (string root in excludedRoots)
        {
            bool within;
            try
            {
                within = containment.MayBeWithin(ringPath, root);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
            {
                throw new MasterKeyRingException(
                    MasterKeyRingFailure.FileRejected,
                    $"The key ring path '{ringPath}' or the excluded root '{root}' could not be resolved: {exception.GetType().Name}.",
                    exception);
            }

            if (within)
            {
                throw new MasterKeyRingException(
                    MasterKeyRingFailure.PathInExcludedRoot,
                    $"The key ring file '{ringPath}' must not lie inside the excluded root '{root}'.");
            }
        }
    }
}
