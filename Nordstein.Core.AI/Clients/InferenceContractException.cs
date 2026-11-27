using Nordstein.Core.AI.Clients;
namespace Nordstein.Core.AI.Clients;

/// <summary>
/// The model endpoint answered, but not with what the contract requires: a vector count or
/// dimension that does not match, a reply without choices, an answer that is not the expected
/// response. Permanent until the configuration changes: retrying the same request yields the same
/// shape. Not sealed: a product derives its own reasons from it. The message names what did not
/// match, never request or reply content.
/// </summary>
public class InferenceContractException : Exception
{
    /// <summary>Initializes the exception with what did not match.</summary>
    /// <param name="message">What did not match; never request or reply content.</param>
    public InferenceContractException(string message)
        : base(message)
    {
    }

    /// <summary>Initializes the exception with what did not match and the error that showed it.</summary>
    /// <param name="message">What did not match; never request or reply content.</param>
    /// <param name="innerException">The error that showed it, such as a parse failure.</param>
    public InferenceContractException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
