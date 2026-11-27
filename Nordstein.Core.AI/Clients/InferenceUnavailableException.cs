using Nordstein.Core.AI.Clients;
namespace Nordstein.Core.AI.Clients;

/// <summary>
/// The model endpoint was unreachable, timed out or answered with an error status. Transient: the
/// same request may succeed later, so a caller may back off and retry. The message names what
/// failed, never request or reply content.
/// </summary>
public sealed class InferenceUnavailableException : Exception
{
    /// <summary>Initializes the exception with what failed.</summary>
    /// <param name="message">What failed; never request or reply content.</param>
    public InferenceUnavailableException(string message)
        : base(message)
    {
    }

    /// <summary>Initializes the exception with what failed and the transport error.</summary>
    /// <param name="message">What failed; never request or reply content.</param>
    /// <param name="innerException">The transport error.</param>
    public InferenceUnavailableException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
