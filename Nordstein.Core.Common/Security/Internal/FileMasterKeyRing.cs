using System.Buffers.Binary;
using System.Globalization;
using Nordstein.Core.Common.Cryptography;

namespace Nordstein.Core.Common.Security.Internal;

/// <summary>
/// The file-backed master key ring. See <see cref="IMasterKeyRing"/>.
/// </summary>
/// <remarks>
/// A singleton whose constructor does no I/O. The first member access loads the ring through
/// <see cref="MasterKeyRingFile"/> exactly once (<see cref="LazyThreadSafetyMode.ExecutionAndPublication"/>,
/// which also caches a failure, so every later access rethrows it). After loading, the state is read-only and
/// <see cref="IAeadKeyWrap"/> is thread-safe, so concurrent wraps and unwraps need no lock.
/// </remarks>
internal sealed class FileMasterKeyRing : IMasterKeyRing
{
    private readonly IAeadKeyWrap keyWrap;
    private readonly Lazy<LoadedMasterKeyRing> ring;

    public FileMasterKeyRing(MasterKeyRingFile file, IAeadKeyWrap keyWrap)
    {
        ArgumentNullException.ThrowIfNull(file);
        ArgumentNullException.ThrowIfNull(keyWrap);
        this.keyWrap = keyWrap;
        ring = new Lazy<LoadedMasterKeyRing>(file.Load, LazyThreadSafetyMode.ExecutionAndPublication);
    }

    public Guid RingId => ring.Value.RingId;

    public int ActiveKeyVersion => ring.Value.ActiveKeyVersion;

    public MasterWrappedKey Wrap(ReadOnlySpan<byte> plaintext, ReadOnlySpan<byte> associatedData)
    {
        LoadedMasterKeyRing loaded = ring.Value;
        int version = loaded.ActiveKeyVersion;
        WrappedKey wrapped = keyWrap.Wrap(loaded.Keys[version], plaintext, Bind(associatedData, version));
        return new MasterWrappedKey(version, wrapped);
    }

    public byte[] Unwrap(MasterWrappedKey wrapped, ReadOnlySpan<byte> associatedData)
    {
        ArgumentNullException.ThrowIfNull(wrapped);
        if (wrapped.Wrapped is null)
        {
            throw new ArgumentException("The wrap has no nonce and ciphertext.", nameof(wrapped));
        }

        LoadedMasterKeyRing loaded = ring.Value;
        if (!loaded.Keys.TryGetValue(wrapped.KeyVersion, out byte[]? key))
        {
            throw new MasterKeyRingException(
                MasterKeyRingFailure.UnknownKeyVersion,
                $"The ring does not hold key version {wrapped.KeyVersion.ToString(CultureInfo.InvariantCulture)}.");
        }

        // AES-GCM would throw a bare ArgumentException for another nonce length: a damaged record is refused
        // like a tampered one.
        if (wrapped.Wrapped.Nonce.Length != MasterKeyRingFormat.NonceSize)
        {
            throw new MasterKeyRingException(MasterKeyRingFailure.UnwrapFailed, "The wrapped value has a malformed nonce.");
        }

        try
        {
            return keyWrap.Unwrap(key, wrapped.Wrapped, Bind(associatedData, wrapped.KeyVersion));
        }
        catch (AeadAuthenticationFailedException exception)
        {
            throw new MasterKeyRingException(
                MasterKeyRingFailure.UnwrapFailed, "The wrapped value did not authenticate under its key and binding.", exception);
        }
    }

    // The caller's associated data followed by BE32(version): the version has a fixed length at the end, so the
    // caller's bytes are recovered from the total length and no two (binding, version) pairs share an encoding.
    private static byte[] Bind(ReadOnlySpan<byte> associatedData, int version)
    {
        byte[] bound = new byte[checked(associatedData.Length + sizeof(int))];
        associatedData.CopyTo(bound);
        BinaryPrimitives.WriteInt32BigEndian(bound.AsSpan(associatedData.Length), version);
        return bound;
    }
}
