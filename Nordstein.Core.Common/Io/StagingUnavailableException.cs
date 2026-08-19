namespace Nordstein.Core.Common.Io;

/// <summary>
/// Thrown when a durable write cannot begin because its staging file could not be created in the
/// destination directory (for example the directory does not exist or is not writable).
/// </summary>
public sealed class StagingUnavailableException : IOException
{
    /// <summary>
    /// Initializes a new instance of the <see cref="StagingUnavailableException"/> class.
    /// </summary>
    /// <param name="directory">The destination directory a staging file could not be created in.</param>
    /// <param name="innerException">The underlying I/O failure.</param>
    public StagingUnavailableException(string directory, Exception innerException)
        : base($"Unable to create a staging file in '{directory}'.", innerException)
    {
        Directory = directory;
    }

    /// <summary>
    /// Gets the destination directory a staging file could not be created in.
    /// </summary>
    public string Directory { get; }
}
