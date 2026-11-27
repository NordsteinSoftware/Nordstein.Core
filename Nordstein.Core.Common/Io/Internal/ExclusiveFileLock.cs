namespace Nordstein.Core.Common.Io.Internal;

/// <summary>
/// Locks a file through a <see cref="FileShare.None"/> stream. See <see cref="IExclusiveFileLock"/>.
/// </summary>
internal sealed class ExclusiveFileLock : IExclusiveFileLock
{
    public IAsyncDisposable? TryAcquire(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        try
        {
            return new Lease(new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None));
        }
        catch (IOException exception) when (exception.IsHeldLock())
        {
            return null;
        }
    }

    private sealed class Lease : IAsyncDisposable
    {
        private readonly FileStream stream;

        public Lease(FileStream stream)
        {
            this.stream = stream;
        }

        // FileStream disposal is idempotent, so a second dispose releases nothing twice.
        public ValueTask DisposeAsync() => stream.DisposeAsync();
    }
}
