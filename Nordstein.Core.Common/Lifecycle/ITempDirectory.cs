namespace Nordstein.Core.Common.Lifecycle;

/// <summary>
/// Represents a temporary directory on the filesystem that is automatically deleted when disposed.
/// </summary>
/// <remarks>
/// Obtain an instance through the DI-registered <see cref="Create"/> delegate.
/// The directory and all its contents are removed on <see cref="IDisposable.Dispose"/>.
/// </remarks>
public interface ITempDirectory : IDisposable
{
    /// <summary>
    /// Factory delegate registered in the DI container for creating <see cref="ITempDirectory"/> instances.
    /// </summary>
    /// <param name="parentDirectory">
    /// The directory under which the temporary directory is created.
    /// When <c>null</c>, the system's default temporary directory (e.g. <c>/tmp</c>) is used.
    /// </param>
    /// <param name="prefix">
    /// An optional prefix prepended to the generated directory name to aid identification in logs or
    /// diagnostics. When <c>null</c>, no prefix is used.
    /// </param>
    /// <returns>A new <see cref="ITempDirectory"/> whose backing directory already exists on disk.</returns>
    delegate ITempDirectory Create(
        string? parentDirectory = null,
        string? prefix = null);

    /// <summary>
    /// Gets the full filesystem path of the temporary directory.
    /// </summary>
    string Path { get; }

    /// <summary>
    /// Combines <see cref="Path"/> with a relative path segment.
    /// </summary>
    /// <param name="path">A relative path to combine with the temporary directory's root path.</param>
    /// <returns>
    /// The combined path, equivalent to <see cref="System.IO.Path.Combine(string, string)"/>
    /// called with <see cref="Path"/> and <paramref name="path"/>.
    /// </returns>
    string Combine(string path);
}
