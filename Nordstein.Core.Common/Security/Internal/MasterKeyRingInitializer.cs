using System.Buffers;
using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using Nordstein.Core.Common.Cryptography;
using Nordstein.Core.Common.Io;
using Nordstein.Core.Common.Time;
using Nordstein.Core.Common.Validation;

namespace Nordstein.Core.Common.Security.Internal;

/// <summary>
/// Creates the ring file once. See <see cref="IMasterKeyRingInitializer"/>.
/// </summary>
/// <remarks>
/// <para>
/// Stateless: every call works on its own buffers, so one instance serves concurrent callers; two concurrent
/// initializations of one path are settled by the create-new open, never by a check.
/// </para>
/// <para>
/// The file is opened with <see cref="FileMode.CreateNew"/> (<c>O_CREAT | O_EXCL</c>), not published through
/// <see cref="IDurableFilePublisher"/>: its create-without-replace is a check followed by a rename that would
/// overwrite a ring another process published in between, and a replaced ring loses every value wrapped under it.
/// </para>
/// <para>
/// The key exists in a pinned array and, as base64, in one document buffer that never grows; both are zeroed
/// before the call returns or throws. The file stream is unbuffered, so no other copy is made in managed memory.
/// </para>
/// </remarks>
internal sealed class MasterKeyRingInitializer : IMasterKeyRingInitializer
{
    private const UnixFileMode DirectoryMode = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute;
    private const UnixFileMode FileMode0600 = UnixFileMode.UserRead | UnixFileMode.UserWrite;

    // A one-key document is about 400 bytes, and Utf8JsonWriter asks for at most 4 KiB at a time: the buffer
    // never has to grow, so it never leaves an unzeroed copy behind.
    private const int DocumentCapacity = 16 * 1024;

    private readonly MasterKeyRingOptions options;
    private readonly IClock clock;
    private readonly IPathContainment pathContainment;
    private readonly IDirectorySync directorySync;
    private readonly IAeadKeyWrap keyWrap;
    private readonly string[] excludedRoots;

    public MasterKeyRingInitializer(
        MasterKeyRingOptions options,
        IClock clock,
        IPathContainment pathContainment,
        IDirectorySync directorySync,
        IAeadKeyWrap keyWrap)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(pathContainment);
        ArgumentNullException.ThrowIfNull(directorySync);
        ArgumentNullException.ThrowIfNull(keyWrap);
        options.Validate();
        this.options = options;
        this.clock = clock;
        this.pathContainment = pathContainment;
        this.directorySync = directorySync;
        this.keyWrap = keyWrap;
        excludedRoots = [.. options.ExcludedRoots];
    }

    public void Precheck()
    {
        AssertOutsideExcludedRoots();
        (string directory, string path) = Paths();

        // File.Exists is true for a dangling link on Unix; the link check keeps that true on every platform.
        if (File.Exists(path) || new FileInfo(path).LinkTarget is not null)
        {
            throw new MasterKeyRingException(MasterKeyRingFailure.AlreadyInitialized, $"A key ring already exists at '{path}'.");
        }

        if (Directory.Exists(directory) && Directory.EnumerateFileSystemEntries(directory).Any())
        {
            throw new MasterKeyRingException(
                MasterKeyRingFailure.AlreadyInitialized, $"The key directory '{directory}' is not empty.");
        }
    }

    public async Task<MasterKeyRingInitialized> InitializeAsync(Guid ringId, CancellationToken cancellationToken = default)
    {
        if (ringId == Guid.Empty)
        {
            throw new ArgumentException("A ring id is not the empty GUID.", nameof(ringId));
        }

        cancellationToken.ThrowIfCancellationRequested();
        Precheck();
        (string directory, string path) = Paths();
        bool createdDirectory = !Directory.Exists(directory);
        try
        {
            if (createdDirectory)
            {
                CreateKeyDirectory(directory);
            }
            else if (!OperatingSystem.IsWindows())
            {
                // An existing directory passed the emptiness check; the loader requires 0700.
                File.SetUnixFileMode(directory, DirectoryMode);
            }

            await WriteRingAsync(path, ringId, cancellationToken);
            AssertWrittenOutsideExcludedRoots(path, ringId);
            FlushDirectoryOfRing(directory, path);
        }
        catch when (createdDirectory)
        {
            // The directory this call made goes with the failure; a pre-existing one stays.
            TryDeleteEmptyDirectory(directory);
            throw;
        }

        return new MasterKeyRingInitialized(path, ringId, createdDirectory);
    }

    public void Remove(MasterKeyRingInitialized created)
    {
        ArgumentNullException.ThrowIfNull(created);
        if (!IsRingOf(created.Path, created.RingId))
        {
            throw new InvalidOperationException(
                $"The file at '{created.Path}' is not the key ring {created.RingId:D} this initializer wrote; nothing was deleted.");
        }

        File.Delete(created.Path);
        if (created.CreatedDirectory && Path.GetDirectoryName(Path.GetFullPath(created.Path)) is { } directory)
        {
            Directory.Delete(directory);
        }
    }

    // The configured paths, not Paths(): GetFullPath collapses "link/.." as text and would hide the reading the
    // operating system walks, which the loader checks too.
    private void AssertOutsideExcludedRoots()
        => pathContainment.AssertOutsideExcludedRoots(MasterKeyRingFormat.ConfiguredPath(options), excludedRoots);

    private (string Directory, string Path) Paths()
    {
        string directory = Path.GetFullPath(options.KeyDirectory);
        return (directory, Path.Combine(directory, options.FileName));
    }

    // Creates only the key directory itself, owner-only whatever the umask, then flushes its parent so the new
    // entry survives a crash. A missing parent is refused: Core does not invent a directory tree.
    private void CreateKeyDirectory(string directory)
    {
        string parent = Path.GetDirectoryName(directory)
            ?? throw new DirectoryNotFoundException($"The key directory '{directory}' has no parent.");
        if (!Directory.Exists(parent))
        {
            throw new DirectoryNotFoundException(
                $"The parent of the key directory '{directory}' does not exist; it is not created.");
        }

        if (OperatingSystem.IsWindows())
        {
            Directory.CreateDirectory(directory);
        }
        else
        {
            Directory.CreateDirectory(directory, DirectoryMode);
            File.SetUnixFileMode(directory, DirectoryMode);
        }

        directorySync.Flush(parent);
    }

    private async Task WriteRingAsync(string path, Guid ringId, CancellationToken cancellationToken)
    {
        byte[] key = GC.AllocateArray<byte>(MasterKeyRingFormat.KeySize, pinned: true);
        var document = new ArrayBufferWriter<byte>(DocumentCapacity);
        try
        {
            RandomNumberGenerator.Fill(key);
            WrappedKey canary = keyWrap.Wrap(key, MasterKeyRingFormat.CanaryPlaintext, []);

            // Read between the check and the open, on purpose: the tests' racing clocks rely on it.
            string createdAt = clock.UtcNow.ToString("O", CultureInfo.InvariantCulture);
            WriteDocument(document, ringId, createdAt, key, canary);
            await WriteNewAsync(path, document.WrittenMemory, cancellationToken);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);

            // ArrayBufferWriter.Clear zeroes the written bytes (ResetWrittenCount would not).
            document.Clear();
        }
    }

    private static void WriteDocument(
        IBufferWriter<byte> output, Guid ringId, string createdAt, ReadOnlySpan<byte> key, WrappedKey canary)
    {
        using var writer = new Utf8JsonWriter(output, new JsonWriterOptions { Indented = true });
        writer.WriteStartObject();
        writer.WriteString("format", MasterKeyRingFormat.Marker);
        writer.WriteString("ringId", ringId.ToString("D"));
        writer.WriteStartArray("keys");
        writer.WriteStartObject();
        writer.WriteNumber("version", 1);
        writer.WriteString("createdAt", createdAt);
        writer.WriteString("status", MasterKeyRingFormat.Active);
        writer.WriteBase64String("key", key);
        writer.WriteStartObject("canary");
        writer.WriteBase64String("nonce", canary.Nonce.Span);
        writer.WriteBase64String("ciphertext", canary.Ciphertext.Span);
        writer.WriteEndObject();
        writer.WriteEndObject();
        writer.WriteEndArray();
        writer.WriteEndObject();
        writer.Flush();
    }

    private static async Task WriteNewAsync(string path, ReadOnlyMemory<byte> content, CancellationToken cancellationToken)
    {
        var streamOptions = new FileStreamOptions
        {
            Mode = FileMode.CreateNew,
            Access = FileAccess.Write,
            Share = FileShare.None,
            BufferSize = 0,
            Options = FileOptions.WriteThrough,
        };
        if (!OperatingSystem.IsWindows())
        {
            streamOptions.UnixCreateMode = FileMode0600;
        }

        // The open stays outside the try: a create-new that fails because a ring appeared after the check (a
        // parallel initialization, a link) must propagate with nothing of ours to delete.
        FileStream stream;
        try
        {
            stream = new FileStream(path, streamOptions);
        }
        catch (IOException exception) when (File.Exists(path))
        {
            throw new MasterKeyRingException(
                MasterKeyRingFailure.AlreadyInitialized, $"A key ring already exists at '{path}'.", exception);
        }

        try
        {
            await using (stream)
            {
                if (!OperatingSystem.IsWindows())
                {
                    // On the open handle, so a restrictive umask cannot leave the file unreadable to its owner.
                    File.SetUnixFileMode(stream.SafeFileHandle, FileMode0600);
                }

                await stream.WriteAsync(content, cancellationToken);
                stream.Flush(flushToDisk: true);
            }
        }
        catch
        {
            // A partial ring this call created must not survive a failed write.
            File.Delete(path);
            throw;
        }
    }

    // The placement check again, after the write: a key directory swapped for a link into an excluded root
    // between the check and the open lands the ring there. The ring found is deleted only if it is the one this
    // call wrote, so a swap after the open never makes the cleanup delete another ring.
    private void AssertWrittenOutsideExcludedRoots(string path, Guid ringId)
    {
        try
        {
            AssertOutsideExcludedRoots();
        }
        catch (MasterKeyRingException)
        {
            if (IsRingOf(path, ringId))
            {
                File.Delete(path);
            }

            throw;
        }
    }

    // The ring's own flush made its bytes durable, not its name. A ring whose name may not survive a crash is not
    // handed out: it is deleted (this call created it) and the failure propagates.
    private void FlushDirectoryOfRing(string directory, string path)
    {
        try
        {
            directorySync.Flush(directory);
        }
        catch
        {
            File.Delete(path);
            throw;
        }
    }

    private static bool IsRingOf(string path, Guid ringId)
    {
        byte[] bytes;
        try
        {
            bytes = File.ReadAllBytes(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Not readable as a ring: not ours to delete.
            return false;
        }

        try
        {
            using JsonDocument document = JsonDocument.Parse(bytes);
            return document.RootElement.ValueKind == JsonValueKind.Object
                && document.RootElement.TryGetProperty("ringId", out JsonElement id)
                && id.ValueKind == JsonValueKind.String
                && Guid.TryParseExact(id.GetString(), "D", out Guid written)
                && written == ringId;
        }
        catch (JsonException)
        {
            return false;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(bytes);
        }
    }

    private static void TryDeleteEmptyDirectory(string directory)
    {
        try
        {
            Directory.Delete(directory);
        }
        catch (IOException)
        {
            // Not empty (another run's ring) or already gone: nothing of ours to remove.
        }
        catch (UnauthorizedAccessException)
        {
            // Not ours to remove either; the original failure is the one that matters.
        }
    }
}
