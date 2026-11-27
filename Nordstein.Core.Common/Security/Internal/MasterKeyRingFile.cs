using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using Nordstein.Core.Common.Cryptography;
using Nordstein.Core.Common.Io;
using Nordstein.Core.Common.Validation;

namespace Nordstein.Core.Common.Security.Internal;

/// <summary>
/// Reads and verifies the ring file for <see cref="FileMasterKeyRing"/>: placement, custody
/// (<see cref="ISecretFileLoader"/>), the strict format, a non-empty ring id, keys of 32 bytes whose canaries
/// authenticate, distinct versions from 1 and exactly one active key. Every failure is a
/// <see cref="MasterKeyRingException"/>. The constructor does no I/O; each <see cref="Load"/> reads the file anew.
/// </summary>
/// <remarks>
/// The file's bytes and every array the parser decoded (<see cref="TrackedBase64Converter"/>) are zeroed before
/// <see cref="Load"/> returns or throws, also when the parse fails after a key was decoded. The keys it returns are copies in pinned arrays, so the collector never leaves a moved copy behind.
/// </remarks>
internal sealed class MasterKeyRingFile
{
    private readonly ISecretFileLoader secretFileLoader;
    private readonly IPathContainment pathContainment;
    private readonly IAeadKeyWrap keyWrap;
    private readonly MasterKeyRingOptions options;
    private readonly string[] excludedRoots;

    public MasterKeyRingFile(
        ISecretFileLoader secretFileLoader, IPathContainment pathContainment, IAeadKeyWrap keyWrap, MasterKeyRingOptions options)
    {
        ArgumentNullException.ThrowIfNull(secretFileLoader);
        ArgumentNullException.ThrowIfNull(pathContainment);
        ArgumentNullException.ThrowIfNull(keyWrap);
        ArgumentNullException.ThrowIfNull(options);
        options.Validate();
        this.secretFileLoader = secretFileLoader;
        this.pathContainment = pathContainment;
        this.keyWrap = keyWrap;
        this.options = options;
        excludedRoots = [.. options.ExcludedRoots];
    }

    public LoadedMasterKeyRing Load()
    {
        string path = MasterKeyRingFormat.ConfiguredPath(options);
        pathContainment.AssertOutsideExcludedRoots(path, excludedRoots);

        byte[] bytes = Read(path);
        var decoded = new List<byte[]>();
        try
        {
            return Verify(Parse(bytes, decoded));
        }
        finally
        {
            // Every path: the file's bytes and every array the parser decoded, also when the parse failed
            // after a key was decoded. The verified keys live on in their pinned copies.
            CryptographicOperations.ZeroMemory(bytes);
            foreach (byte[] array in decoded)
            {
                CryptographicOperations.ZeroMemory(array);
            }
        }
    }

    private byte[] Read(string path)
    {
        try
        {
            return secretFileLoader.Load(path);
        }
        catch (SecretFileException exception)
        {
            throw new MasterKeyRingException(
                MasterKeyRingFailure.FileRejected, $"The key ring file was refused: {exception.Rejection}.", exception);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw new MasterKeyRingException(
                MasterKeyRingFailure.FileRejected,
                $"The key ring file '{path}' could not be read: {exception.GetType().Name}.",
                exception);
        }
    }

    private static MasterKeyRingDocument Parse(byte[] bytes, List<byte[]> decoded)
    {
        MasterKeyRingDocument? document;
        try
        {
            document = JsonSerializer.Deserialize<MasterKeyRingDocument>(bytes, MasterKeyRingFormat.ReadOptions(decoded));
        }
        catch (JsonException exception)
        {
            throw new MasterKeyRingException(
                MasterKeyRingFailure.Malformed, "The key ring file is not a key ring document.", exception);
        }

        return document is not null && string.Equals(document.Format, MasterKeyRingFormat.Marker, StringComparison.Ordinal)
            ? document
            : throw Malformed($"The key ring format marker is missing or not \"{MasterKeyRingFormat.Marker}\".");
    }

    private LoadedMasterKeyRing Verify(MasterKeyRingDocument document)
    {
        Guid ringId = Guid.TryParseExact(document.RingId, "D", out Guid parsed) && parsed != Guid.Empty
            ? parsed
            : throw Malformed("The ring id is missing, empty or not a GUID in the 36-character form.");
        IReadOnlyList<MasterKeyDocument?> entries = document.Keys ?? throw Malformed("The ring has no keys member.");

        var keys = new Dictionary<int, byte[]>();
        try
        {
            int activeCount = 0;
            int activeVersion = 0;
            foreach (MasterKeyDocument? entry in entries)
            {
                (int version, bool active) = Accept(entry, keys);
                if (active)
                {
                    activeCount++;
                    activeVersion = version;
                }
            }

            return activeCount switch
            {
                0 => throw new MasterKeyRingException(MasterKeyRingFailure.NoActiveKey, "The ring has no active key."),
                1 => new LoadedMasterKeyRing(ringId, activeVersion, keys),
                _ => throw new MasterKeyRingException(
                    MasterKeyRingFailure.MultipleActiveKeys, "The ring has more than one active key."),
            };
        }
        catch
        {
            foreach (byte[] key in keys.Values)
            {
                CryptographicOperations.ZeroMemory(key);
            }

            throw;
        }
    }

    // Checks one entry and adds a pinned copy of its key; returns its version and whether it is active.
    private (int Version, bool Active) Accept(MasterKeyDocument? entry, Dictionary<int, byte[]> keys)
    {
        if (entry is null)
        {
            throw Malformed("A key entry is null.");
        }

        int version = entry.Version;
        string name = version.ToString(CultureInfo.InvariantCulture);
        if (version < 1)
        {
            throw Malformed($"Key version {name} is below 1.");
        }

        if (keys.ContainsKey(version))
        {
            throw Malformed($"Two keys share version {name}.");
        }

        bool active = entry.Status switch
        {
            MasterKeyRingFormat.Active => true,
            MasterKeyRingFormat.DecryptOnly => false,
            _ => throw Malformed(
                $"Key {name} has no status or an unknown one; it is \"{MasterKeyRingFormat.Active}\" or \"{MasterKeyRingFormat.DecryptOnly}\"."),
        };

        byte[] key = entry.Key ?? throw Malformed($"Key {name} has no key bytes.");
        if (key.Length != MasterKeyRingFormat.KeySize)
        {
            throw new MasterKeyRingException(
                MasterKeyRingFailure.WrongKeyLength,
                $"Key {name} is {key.Length.ToString(CultureInfo.InvariantCulture)} bytes, not {MasterKeyRingFormat.KeySize}.");
        }

        VerifyCanary(entry.Canary, key, name);

        byte[] pinned = GC.AllocateArray<byte>(MasterKeyRingFormat.KeySize, pinned: true);
        key.CopyTo(pinned, 0);
        keys.Add(version, pinned);
        return (version, active);
    }

    private void VerifyCanary(MasterKeyCanaryDocument? canary, byte[] key, string name)
    {
        if (canary?.Nonce is not { Length: MasterKeyRingFormat.NonceSize } nonce
            || canary.Ciphertext is not { Length: >= MasterKeyRingFormat.TagSize } ciphertext)
        {
            throw Malformed($"Key {name} has no canary, or one of the wrong shape.");
        }

        byte[] plaintext;
        try
        {
            plaintext = keyWrap.Unwrap(key, new WrappedKey(nonce, ciphertext), []);
        }
        catch (AeadAuthenticationFailedException exception)
        {
            throw new MasterKeyRingException(
                MasterKeyRingFailure.CanaryFailed, $"The canary of key {name} did not authenticate under its key.", exception);
        }

        bool matches = plaintext.AsSpan().SequenceEqual(MasterKeyRingFormat.CanaryPlaintext);
        CryptographicOperations.ZeroMemory(plaintext);
        if (!matches)
        {
            throw new MasterKeyRingException(
                MasterKeyRingFailure.CanaryFailed, $"The canary of key {name} decrypted to the wrong text.");
        }
    }

    private static MasterKeyRingException Malformed(string message) => new(MasterKeyRingFailure.Malformed, message);
}

/// <summary>
/// A verified ring: its id, the active version and every key by version (pinned arrays, read-only after load).
/// </summary>
internal sealed record LoadedMasterKeyRing(Guid RingId, int ActiveKeyVersion, IReadOnlyDictionary<int, byte[]> Keys);
