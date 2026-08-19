namespace Nordstein.Core.Common.Io.Internal;

/// <summary>
/// Stateless factory for <see cref="FileWriteHandle"/>. See <see cref="IDurableFilePublisher"/>.
/// </summary>
internal sealed class DurableFilePublisher : IDurableFilePublisher
{
    public IFileWriteHandle BeginWrite(string destinationDirectory)
    {
        ArgumentException.ThrowIfNullOrEmpty(destinationDirectory);
        return new FileWriteHandle(destinationDirectory);
    }
}
