namespace Nordstein.Core.Common.Security;

/// <summary>
/// The process's master keys, loaded once from a custody-checked key file: wraps values (data keys, stored
/// secrets) under the active master key and unwraps them under the key version they name. Master-key bytes
/// never cross this interface, so a later key-management-service adapter can implement it unchanged.
/// </summary>
/// <remarks>
/// <para>
/// <b>Loading.</b> Resolving the ring does no I/O. The first member access loads and verifies the key file
/// (<see cref="MasterKeyRingOptions"/>) exactly once, also under concurrent first accesses: the path must lie
/// outside every <see cref="MasterKeyRingOptions.ExcludedRoots"/> entry, the file must pass
/// <see cref="Io.ISecretFileLoader"/> (no symbolic link; on Unix mode <c>0600</c> in a <c>0700</c> directory),
/// parse as the ring format, and hold 32-byte keys whose canaries authenticate and exactly one active key. Any
/// failure throws <see cref="MasterKeyRingException"/> from that access and from every later one with the same
/// <see cref="MasterKeyRingException.Failure"/>: the ring never falls back to a new key, and a repaired file is
/// read only by a new process. Touch <see cref="RingId"/> during startup so a bad ring stops the host before
/// it serves anything.
/// </para>
/// <para>
/// <b>Binding.</b> <see cref="Wrap"/> is AES-256-GCM (<see cref="Cryptography.IAeadKeyWrap"/>) under the active
/// key with a fresh CSPRNG nonce. Its associated data is the caller's bytes followed by the key version as a
/// four-byte big-endian integer, so a wrapped value moved to another version, or to another context, fails to
/// unwrap. The caller's bytes should start with a constant naming the kind of value (<c>acme-setting-v1</c>)
/// and then identify the slot it belongs to, so that no value wrapped for one purpose unwraps for another.
/// </para>
/// <para>
/// <b>Rotation is a non-goal.</b> The ring never adds, activates, demotes or retires a key and never re-wraps;
/// it reads a file that may hold older decrypt-only keys beside the one active key, and wraps under the active
/// one only.
/// </para>
/// <para>Thread-safe; <see cref="Module"/> registers one instance per container where the host registered
/// <see cref="MasterKeyRingOptions"/>.</para>
/// </remarks>
public interface IMasterKeyRing
{
    /// <summary>
    /// The identifier written into the ring when it was created; not secret. A host binds its other durable
    /// artefacts to it, so a ring from another installation is noticed.
    /// </summary>
    /// <exception cref="MasterKeyRingException">The ring did not load.</exception>
    Guid RingId { get; }

    /// <summary>
    /// The version of the single active key, the one <see cref="Wrap"/> uses; at least 1.
    /// </summary>
    /// <exception cref="MasterKeyRingException">The ring did not load.</exception>
    int ActiveKeyVersion { get; }

    /// <summary>
    /// Wraps <paramref name="plaintext"/> under the active key, binding <paramref name="associatedData"/> and
    /// the key version.
    /// </summary>
    /// <param name="plaintext">The value to protect, any length; the caller zeroes its buffer.</param>
    /// <param name="associatedData">The caller's binding (may be empty); <see cref="Unwrap"/> needs the same bytes.</param>
    /// <returns>The key version, nonce and ciphertext with its tag; none of them is secret.</returns>
    /// <exception cref="MasterKeyRingException">The ring did not load.</exception>
    MasterWrappedKey Wrap(ReadOnlySpan<byte> plaintext, ReadOnlySpan<byte> associatedData);

    /// <summary>
    /// Reverses <see cref="Wrap"/> under the key version <paramref name="wrapped"/> names, active or
    /// decrypt-only.
    /// </summary>
    /// <param name="wrapped">The stored wrap.</param>
    /// <param name="associatedData">The same binding the value was wrapped with.</param>
    /// <returns>A new buffer with the plaintext; the caller zeroes it after use.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="wrapped"/> is <see langword="null"/>.</exception>
    /// <exception cref="MasterKeyRingException">
    /// The ring did not load; the version is not in the ring
    /// (<see cref="MasterKeyRingFailure.UnknownKeyVersion"/>); or the nonce is not 12 bytes, or the wrap does not
    /// authenticate under that key and binding (<see cref="MasterKeyRingFailure.UnwrapFailed"/>: a wrong binding
    /// or a tampered record, deliberately not told apart).
    /// </exception>
    byte[] Unwrap(MasterWrappedKey wrapped, ReadOnlySpan<byte> associatedData);
}
