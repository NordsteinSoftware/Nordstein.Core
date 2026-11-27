namespace Nordstein.Core.Common.Security;

/// <summary>
/// Why the master key ring refused to load, to wrap, to unwrap or to be created. Every value is fail-closed:
/// the operation did not happen. The numbers are stable.
/// </summary>
public enum MasterKeyRingFailure
{
    /// <summary>
    /// The key file was refused before it was parsed: missing, a symbolic link, too open a mode on Unix, not
    /// readable, or a path that cannot be resolved (a link loop, an unsearchable directory, a U+0000).
    /// </summary>
    FileRejected = 1,

    /// <summary>
    /// The file is not the ring format: not JSON, an unknown or duplicated member, a wrong format marker, a
    /// missing or empty ring id, a key version below 1 or used twice, an unknown status, bytes that are not
    /// base64, or a canary of the wrong shape.
    /// </summary>
    Malformed = 2,

    /// <summary>
    /// No key is marked active.
    /// </summary>
    NoActiveKey = 3,

    /// <summary>
    /// More than one key is marked active.
    /// </summary>
    MultipleActiveKeys = 4,

    /// <summary>
    /// A key is not exactly 32 bytes.
    /// </summary>
    WrongKeyLength = 5,

    /// <summary>
    /// A key's canary did not authenticate under that key, or decrypted to the wrong text: the key bytes were
    /// changed or belong to another ring.
    /// </summary>
    CanaryFailed = 6,

    /// <summary>
    /// The key file lies, or may lie, inside one of <see cref="MasterKeyRingOptions.ExcludedRoots"/>.
    /// </summary>
    PathInExcludedRoot = 7,

    /// <summary>
    /// An unwrap named a key version the ring does not hold.
    /// </summary>
    UnknownKeyVersion = 8,

    /// <summary>
    /// An unwrap did not authenticate (a wrong binding or a tampered record) or its nonce had the wrong length.
    /// </summary>
    UnwrapFailed = 9,

    /// <summary>
    /// Creating a ring found one already there, or a key directory that is not empty; nothing was changed.
    /// </summary>
    AlreadyInitialized = 10,
}
