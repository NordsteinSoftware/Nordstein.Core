namespace Nordstein.Core.Licensing;

/// <summary>
/// Why a license JWT was rejected.
/// </summary>
public enum InvalidLicenseReason
{
    /// <summary>
    /// The value is not a parseable JWT at all (or is empty).
    /// </summary>
    Malformed,

    /// <summary>
    /// The signature does not verify against any trusted public key.
    /// </summary>
    BadSignature,

    /// <summary>
    /// The <c>iss</c> claim does not match the configured issuer.
    /// </summary>
    WrongIssuer,

    /// <summary>
    /// The <c>aud</c> claim does not match the configured audience.
    /// </summary>
    WrongAudience,

    /// <summary>
    /// The token's lifetime has ended (<c>exp</c> is in the past).
    /// </summary>
    Expired,

    /// <summary>
    /// A claim the engine requires is absent.
    /// </summary>
    MissingClaim,
}

/// <summary>
/// Thrown when a configured license JWT cannot be validated.
/// </summary>
public sealed class InvalidLicenseException : Exception
{
    public InvalidLicenseException(InvalidLicenseReason reason)
        : this(reason, $"The configured license is invalid: {reason}.")
    {
    }

    public InvalidLicenseException(InvalidLicenseReason reason, string message)
        : base(message)
    {
        Reason = reason;
    }

    public InvalidLicenseException(InvalidLicenseReason reason, string message, Exception innerException)
        : base(message, innerException)
    {
        Reason = reason;
    }

    /// <summary>
    /// The specific reason validation failed.
    /// </summary>
    public InvalidLicenseReason Reason { get; }
}
