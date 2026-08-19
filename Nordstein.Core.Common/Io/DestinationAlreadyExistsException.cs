namespace Nordstein.Core.Common.Io;

/// <summary>
/// Thrown by a create-without-replace publish when the destination path already exists.
/// </summary>
public sealed class DestinationAlreadyExistsException : IOException
{
    /// <summary>
    /// Initializes a new instance of the <see cref="DestinationAlreadyExistsException"/> class.
    /// </summary>
    /// <param name="path">The destination path that already existed.</param>
    public DestinationAlreadyExistsException(string path)
        : base($"The destination '{path}' already exists.")
    {
        Path = path;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="DestinationAlreadyExistsException"/> class with
    /// an inner exception.
    /// </summary>
    /// <param name="path">The destination path that already existed.</param>
    /// <param name="innerException">The underlying I/O failure observed while publishing.</param>
    public DestinationAlreadyExistsException(string path, Exception innerException)
        : base($"The destination '{path}' already exists.", innerException)
    {
        Path = path;
    }

    /// <summary>
    /// Gets the destination path that already existed.
    /// </summary>
    public string Path { get; }
}
