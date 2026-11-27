using Nordstein.Core.AI.Completions;
using Nordstein.Core.AI.Messages;

namespace Nordstein.Core.AI.Agents;

/// <summary>
/// The result of one <see cref="IAgentLoop"/> run.
/// </summary>
/// <param name="Outcome">How the run ended.</param>
/// <param name="Answer">
/// The model's final answer when <paramref name="Outcome"/> is
/// <see cref="AgentLoopOutcome.Completed"/>, otherwise <see langword="null"/>.
/// </param>
/// <param name="Conversation">
/// The whole transcript: the agent's system message, the caller's messages, then every assistant
/// message and tool result of the run in order.
/// </param>
/// <param name="Steps">The number of model calls the run made, from 1 to <see cref="AgentLoopOptions.MaxSteps"/>.</param>
/// <param name="Usage">
/// The sum of the token usage the model reported over all steps, or <see langword="null"/> when
/// no step reported any.
/// </param>
public sealed record AgentLoopResult(
    AgentLoopOutcome Outcome,
    AssistantMessage? Answer,
    Conversation Conversation,
    int Steps,
    TokenUsage? Usage);
