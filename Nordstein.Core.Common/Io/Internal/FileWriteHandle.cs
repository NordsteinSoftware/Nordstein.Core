namespace Nordstein.Core.Common.Io.Internal;

/// <summary>
/// A single durable write in progress. See <see cref="IFileWriteHandle"/>.
/// </summary>
internal sealed class FileWriteHandle : IFileWriteHandle
{
    private readonly string stagingPath;
    private readonly FileStream stream;

    private bool streamClosed;
    private bool published;
    private bool disposed;

    internal FileWriteHandle(string destinationDirectory)
    {
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
