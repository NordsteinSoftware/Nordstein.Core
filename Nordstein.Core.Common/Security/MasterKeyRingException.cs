namespace Nordstein.Core.Common.Security;

/// <summary>
/// A typed, fail-closed refusal of the master key ring (<see cref="IMasterKeyRing"/>,
/// <see cref="IMasterKeyRingInitializer"/>). The message may name a path or a key version; it never holds key
/// material or a wrapped value's plaintext.
/// </summary>
public sealed class MasterKeyRingException : Exception
{
    /// <summary>
    /// A refusal for <paramref name="failure"/>.
    /// </summary>
    /// <param name="failure">Why the ring refused.</param>
    /// <param name="message">A description that holds no key material.</param>
    public MasterKeyRingException(MasterKeyRingFailure failure, string message)
        : base(message)
    {
        Failure = failure;
    }

    /// <summary>
    /// A refusal for <paramref name="failure"/> caused by <paramref name="innerException"/>.
    /// </summary>
    /// <param name="failure">Why the ring refused.</param>
    /// <param name="message">A description that holds no key material.</param>
    /// <param name="innerException">The failure that caused the refusal.</param>
    public MasterKeyRingException(MasterKeyRingFailure failure, string message, Exception innerException)
        : base(message, innerException)
    {
        Failure = failure;
    }

    /// <summary>
    /// Why the ring refused.
    /// </summary>
    public MasterKeyRingFailure Failure { get; }
}
