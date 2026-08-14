using Nordstein.Core.AI.Messages;
using Nordstein.Core.AI.Completions;

namespace Nordstein.Core.AI.Completions;

/// <summary>
/// The result of one model completion: the assistant's response with the token usage and
/// latency of the call that produced it.
/// </summary>
public interface ICompletion : IDomainObject
{
    /// <summary>Creates a completion from a response, its usage (if reported), and its latency.</summary>
    public delegate ICompletion Create(
        AssistantMessage response,
        TokenUsage? usage,
        TimeSpan latency);

    /// <summary>The assistant's response message.</summary>
    AssistantMessage Response { get; }

    /// <summary>The token usage the provider reported, or <see langword="null"/> when unreported.</summary>
    TokenUsage? Usage { get; }

    /// <summary>The wall-clock duration of the model call.</summary>
    TimeSpan Latency { get; }
}