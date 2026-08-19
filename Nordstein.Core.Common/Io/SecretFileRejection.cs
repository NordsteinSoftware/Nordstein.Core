namespace Nordstein.Core.Common.Io;

/// <summary>
/// Why <see cref="ISecretFileLoader"/> refused to load a file, ordered from the cheapest check to
/// the deepest.
/// </summary>
public enum SecretFileRejection
{
    /// <summary>The file passed every check; not a rejection.</summary>
    None = 0,

    /// <summary>No readable file exists at the path (missing, or a directory).</summary>
    Missing,

    /// <summary>The path is a symbolic link; links are refused to prevent link-swap attacks.</summary>
    IsSymlink,

    /// <summary>
    /// On Unix, the file is not mode <c>0600</c> or its parent directory is not mode <c>0700</c>.
    /// Not evaluated on Windows, where Unix permission bits do not apply.
    /// </summary>
    WrongMode,
}
