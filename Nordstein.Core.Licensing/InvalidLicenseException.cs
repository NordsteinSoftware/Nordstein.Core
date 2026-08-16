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
    /// <summary>
    /// Initializes a new instance with the standard message for the given
    /// <paramref name="reason"/>. The <see cref="Reason"/> property is set from
    /// <paramref name="reason"/>.
    /// </summary>
    /// <param name="reason">The specific reason the license JWT was rejected.</param>
    public InvalidLicenseException(InvalidLicenseReason reason)
        : this(reason, $"The configured license is invalid: {reason}.")
    {
    }

    /// <summary>
    /// Initializes a new instance with a custom message and the given
    /// <paramref name="reason"/>.
    /// </summary>
    /// <param name="reason">The specific reason the license JWT was rejected.</param>
    /// <param name="message">A custom error message describing the failure.</param>
    public InvalidLicenseException(InvalidLicenseReason reason, string message)
        : base(message)
    {
        Reason = reason;
    }

    /// <summary>
    /// Initializes a new instance with a custom message, an inner exception, and the given
    /// <paramref name="reason"/>. Use this overload when wrapping a JWT validation exception.
    /// </summary>
    /// <param name="reason">The specific reason the license JWT was rejected.</param>
    /// <param name="message">A custom error message describing the failure.</param>
    /// <param name="innerException">The exception that caused this validation failure, such as a JWT parsing error.</param>
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
