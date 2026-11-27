using System.Buffers;
using System.Security.Cryptography;

namespace Nordstein.Core.Common.Io.Internal;

/// <summary>
/// Spools a stream into an owner-only, create-no-replace file. See <see cref="IPrivateFileSpool"/>.
/// </summary>
internal sealed class PrivateFileSpool : IPrivateFileSpool
{
    private const int BufferSize = 1 << 16;
    private const UnixFileMode OwnerOnlyDirectory = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute;
    private const UnixFileMode OwnerOnlyFile = UnixFileMode.UserRead | UnixFileMode.UserWrite;

    // "The file exists": EEXIST is 17 on Linux, macOS and FreeBSD (.NET puts the raw errno in HResult);
    // on Windows ERROR_FILE_EXISTS and ERROR_ALREADY_EXISTS as HRESULTs.
    private const int UnixFileExists = 17;
    private const int WindowsFileExists = unchecked((int)0x80070050);
    private const int WindowsAlreadyExists = unchecked((int)0x800700B7);

    public async Task<SpooledContent> SpoolAsync(Stream source, string path, long maxBytes, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentException.ThrowIfNullOrEmpty(path);
        ArgumentOutOfRangeException.ThrowIfNegative(maxBytes);
        string fullPath = Path.GetFullPath(path);
        string? directory = Path.GetDirectoryName(fullPath);
        if (directory is null || Path.GetFileName(fullPath).Length == 0)
        {
            throw new ArgumentException($"'{path}' names a directory, not a file.", nameof(path));
        }

        cancellationToken.ThrowIfCancellationRequested();
        EnsureDirectory(directory);

        // The open is outside the try on purpose: a file already at the path is someone else's, and
        // deleting it on the failure path would destroy it.
        FileStream file = CreatePrivateFile(fullPath, path);
        try
        {
            await using (file)
            {
                return await CopyAsync(source, file, maxBytes, cancellationToken);
            }
        }
        catch
        {
            TryDelete(fullPath);
            throw;
        }
    }

    private static async Task<SpooledContent> CopyAsync(
        Stream source, FileStream file, long maxBytes, CancellationToken cancellationToken)
    {
        using var hasher = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        byte[] buffer = ArrayPool<byte>.Shared.Rent(BufferSize);
        try
        {
            long length = 0;
            int read;
            while ((read = await source.ReadAsync(buffer, cancellationToken)) > 0)
            {
                length += read;
                if (length > maxBytes)
                {
                    // Refused the moment the cap is crossed: the rest is never read or written.
                    throw new SpoolLimitExceededException(maxBytes);
                }

                hasher.AppendData(buffer, 0, read);
                await file.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
            }

            await file.FlushAsync(cancellationToken);
            return new SpooledContent(hasher.GetHashAndReset(), length);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    private static void EnsureDirectory(string directory)
    {
        if (Directory.Exists(directory))
        {
            return;
        }

        if (OperatingSystem.IsWindows())
        {
            Directory.CreateDirectory(directory);
        }
        else
        {
            // Owner-only by the mkdir itself: create-then-chmod leaves a window in which the names are listable.
            Directory.CreateDirectory(directory, OwnerOnlyDirectory);
        }
    }

    private static FileStream CreatePrivateFile(string fullPath, string path)
    {
        var options = new FileStreamOptions
        {
            Mode = FileMode.CreateNew,
            Access = FileAccess.Write,
            Share = FileShare.None,
            BufferSize = BufferSize,
            Options = FileOptions.Asynchronous,
        };
        if (!OperatingSystem.IsWindows())
        {
            options.UnixCreateMode = OwnerOnlyFile;
        }

        try
        {
            return new FileStream(fullPath, options);
        }
        catch (IOException exception) when (IsFileExists(exception))
        {
            throw new DestinationAlreadyExistsException(path, exception);
        }
    }

    private static bool IsFileExists(IOException exception)
        => exception.GetType() == typeof(IOException) && (OperatingSystem.IsWindows()
            ? exception.HResult is WindowsFileExists or WindowsAlreadyExists
            : exception.HResult == UnixFileExists);

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
            // Best effort on a failure path: the original exception is the one to surface.
        }
        catch (UnauthorizedAccessException)
        {
            // As above.
        }
    }
}
