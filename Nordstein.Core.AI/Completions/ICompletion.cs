using Nordstein.Core.AI.Messages;
using Nordstein.Core.AI.Completions;

namespace Nordstein.Core.AI.Completions;

public interface ICompletion : IDomainObject
{
    public delegate ICompletion Create(
        AssistantMessage response,
        TokenUsage? usage,
        TimeSpan latency);
    
    AssistantMessage Response { get; }
    TokenUsage? Usage { get; }
    TimeSpan Latency { get; }
}