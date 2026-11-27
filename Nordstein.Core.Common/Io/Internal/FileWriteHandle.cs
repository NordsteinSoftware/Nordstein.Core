namespace Nordstein.Core.Common.Io.Internal;

/// <summary>
/// A single durable write in progress. See <see cref="IFileWriteHandle"/>.
/// </summary>
internal sealed class FileWriteHandle : IFileWriteHandle
{
    private readonly string stagingPath;
    private readonly FileStream stream;
    private readonly IDirectorySync directorySync;

    private bool streamClosed;
    private bool published;
    private bool disposed;

    internal FileWriteHandle(string destinationDirectory, IDirectorySync directorySync)
    {
        this.directorySync = directorySync;
        stagingPath = System.IO.Path.Combine(destinationDirectory, $".{Guid.NewGuid():N}.tmp");

        try
        {
            stream = new FileStream(
                stagingPath,
                new FileStreamOptions
                {
                    Mode = FileMode.CreateNew,
                    Access = FileAccess.Write,
                    Share = FileShare.None,
                    Options = FileOptions.Asynchronous,
                });
        }
        catch (Exception exception) when (exception is IOException
                                              or UnauthorizedAccessException
                                              or DirectoryNotFoundException)
        {
            throw new StagingUnavailableException(destinationDirectory, exception);
        }
    }

    public Stream Content => stream;

    public Task PublishAsync(string destinationPath, CancellationToken cancellationToken = default)
        => PublishCoreAsync(destinationPath, overwrite: false, cancellationToken);

    public Task PublishReplacingAsync(string destinationPath, CancellationToken cancellationToken = default)
        => PublishCoreAsync(destinationPath, overwrite: true, cancellationToken);

    private async Task PublishCoreAsync(string destinationPath, bool overwrite, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        ArgumentException.ThrowIfNullOrEmpty(destinationPath);
        if (published)
        {
            throw new InvalidOperationException("This write handle has already been published.");
        }

        // Only a filesystem root has no parent directory; nothing can be published there, and
        // without a parent there is no directory to flush after the move.
        string? destinationDirectory = System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(destinationPath));
        if (destinationDirectory is null)
        {
            throw new ArgumentException(
                $"'{destinationPath}' is a filesystem root, not a file path.", nameof(destinationPath));
        }

        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
        stream.Flush(flushToDisk: true);
        await stream.DisposeAsync().ConfigureAwait(false);
        streamClosed = true;

        try
        {
            if (overwrite)
            {
                File.Move(stagingPath, destinationPath, overwrite: true);
            }
            else
            {
                File.Move(stagingPath, destinationPath);
            }
        }
        catch (IOException exception) when (!overwrite && File.Exists(destinationPath))
        {
            TryDeleteStaging();
            throw new DestinationAlreadyExistsException(destinationPath, exception);
        }
        catch
        {
            TryDeleteStaging();
            throw;
        }

        published = true;

        // The rename is only durable once the directory holding the new entry is flushed. A failure
        // here leaves the file published, so the handle no longer owns a staging file to delete.
        directorySync.Flush(destinationDirectory);
    }

    public async ValueTask DisposeAsync()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;

        if (!streamClosed)
        {
            await stream.DisposeAsync().ConfigureAwait(false);
            streamClosed = true;
        }

        if (!published)
        {
            TryDeleteStaging();
        }
    }

    private void TryDeleteStaging()
    {
        try
        {
            File.Delete(stagingPath);
        }
        catch
        {
            // Best-effort: a leaked staging file is recoverable and never a final artifact.
        }
    }
}
