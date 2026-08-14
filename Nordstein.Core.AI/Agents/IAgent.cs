using Nordstein.Core.AI.Completions;
using Nordstein.Core.AI.Messages;
using Nordstein.Core.AI.Prompts;
using Nordstein.Core.AI.Tools;

namespace Nordstein.Core.AI.Agents;

/// <summary>
/// The product-agnostic contract of an AI agent: a named actor defined by a system prompt, a
/// tool-set, and model parameters. Deliberately versionless and persistence-free — tenancy,
/// version history, endpoints, and lifecycle are the consuming product's concern and extend
/// this contract there.
/// </summary>
public interface IAgent
{
    /// <summary>Short human-readable name of the agent.</summary>
    string Name { get; }

    /// <summary>The system prompt template that defines the agent's behaviour.</summary>
    IPromptTemplate SystemPrompt { get; }

    /// <summary>The tools available to the agent.</summary>
    IReadOnlyList<ToolSpecification> Tools { get; }

    /// <summary>Sampling and decoding parameters for the agent's completions.</summary>
    IModelParameters ModelParameters { get; }

    /// <summary>
    /// Renders <see cref="SystemPrompt"/> with <paramref name="variables"/> into the system
    /// message a completion request starts from.
    /// </summary>
    SystemMessage CreateSystemMessage(IReadOnlyDictionary<string, string>? variables = null);
}
