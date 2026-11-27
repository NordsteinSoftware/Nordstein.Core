namespace Nordstein.Core.Common.Io.Internal;

/// <summary>
/// Stateless factory for <see cref="FileWriteHandle"/>. See <see cref="IDurableFilePublisher"/>.
/// </summary>
internal sealed class DurableFilePublisher : IDurableFilePublisher
{
    private readonly IDirectorySync directorySync;

    public DurableFilePublisher(IDirectorySync directorySync)
    {
        this.directorySync = directorySync;
    }

    public IFileWriteHandle BeginWrite(string destinationDirectory)
    {
        ArgumentException.ThrowIfNullOrEmpty(destinationDirectory);
        return new FileWriteHandle(destinationDirectory, directorySync);
    }
}
