namespace Nordstein.Core.Common.Security;

/// <summary>
/// The outcome of <see cref="IPasswordHasher.Verify"/>.
/// </summary>
/// <remarks>
/// The default value is <see cref="Failed"/>, so an unset result never reads as success. Treat both
/// <see cref="Succeeded"/> and <see cref="SucceededRehashNeeded"/> as a correct password; the second
/// only adds that the stored hash should be replaced.
/// </remarks>
public enum PasswordVerification
{
    /// <summary>
    /// The password does not match the hash.
    /// </summary>
    Failed = 0,

    /// <summary>
    /// The password matches, and the hash uses the hasher's current parameters.
    /// </summary>
    Succeeded = 1,

    /// <summary>
    /// The password matches, but the hash uses weaker or different parameters than the hasher's
    /// current ones. Store <see cref="IPasswordHasher.Hash"/> of the same password in its place while
    /// the plaintext is at hand; the old hash keeps verifying until then.
    /// </summary>
    SucceededRehashNeeded = 2,
}
