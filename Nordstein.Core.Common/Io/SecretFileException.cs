namespace Nordstein.Core.Common.Io;

/// <summary>
/// Thrown by <see cref="ISecretFileLoader.Load"/> when a file fails a custody check. The message
/// never contains the file's contents.
/// </summary>
public sealed class SecretFileException : IOException
{
    /// <summary>
    /// Initializes a new instance of the <see cref="SecretFileException"/> class.
    /// </summary>
    /// <param name="rejection">The check that failed.</param>
    /// <param name="path">The path of the file that was refused.</param>
    public SecretFileException(SecretFileRejection rejection, string path)
        : base($"The secret file '{path}' was refused: {rejection}.")
    {
        Rejection = rejection;
        Path = path;
    }

    /// <summary>
    /// Gets the custody check that failed.
    /// </summary>
    public SecretFileRejection Rejection { get; }

    /// <summary>
    /// Gets the path of the file that was refused.
    /// </summary>
    public string Path { get; }
}
