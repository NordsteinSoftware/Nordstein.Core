using Nordstein.Core.AI.Clients;
using Nordstein.Core.AI.Completions;
using Nordstein.Core.AI.Messages;

namespace Nordstein.Core.AI.Agents.Internal;

/// <summary>
/// The stateless <see cref="IAgentLoop"/>: model call, tool calls, repeat. See the interface for
/// the contract.
/// </summary>
internal sealed class AgentLoop : IAgentLoop
{
    public async Task<AgentLoopResult> RunAsync(
        IAgent agent,
        IModelClient client,
        IToolInvoker tools,
        Conversation conversation,
        AgentLoopOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(agent);
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(tools);
        ArgumentNullException.ThrowIfNull(conversation);
        ArgumentNullException.ThrowIfNull(options);
        if (string.IsNullOrWhiteSpace(options.ModelName))
        {
            throw new ArgumentException("The model name must not be empty.", nameof(options));
        }

        if (options.MaxSteps < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options), options.MaxSteps, "MaxSteps must be at least 1.");
        }

        if (conversation.SystemMessage is not null)
        {
            throw new ArgumentException(
                "The conversation must not hold a system message; the loop adds the agent's.",
                nameof(conversation));
        }

        // Snapshot the agent once: a run sends the same tools and sampling on every step, and the
        // offered tool names decide which calls reach the invoker.
        var modelOptions = new ModelOptions(
            options.ModelName,
            [.. agent.Tools],
            ToSampling(agent.ModelParameters))
        {
            ResponseFormat = options.ResponseFormat,
        };
        HashSet<string> offered = new(modelOptions.Tools.Select(tool => tool.Name), StringComparer.Ordinal);
        Conversation transcript = conversation.WithSystemMessage(agent.CreateSystemMessage(options.PromptVariables));
        TokenUsage? usage = null;

        for (var step = 1; step <= options.MaxSteps; step++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ICompletion completion = await client.CompleteAsync(
                transcript, modelOptions, options.PromptVariables, cancellationToken);
            usage += completion.Usage;
            AssistantMessage reply = completion.Response;
            transcript = transcript.With(reply);

            if (reply.ToolRequests.Count == 0)
            {
                return new AgentLoopResult(AgentLoopOutcome.Completed, reply, transcript, step, usage);
            }

            foreach (ToolRequest request in reply.ToolRequests)
            {
                cancellationToken.ThrowIfCancellationRequested();
                ToolResponse response = await InvokeAsync(tools, offered, request, cancellationToken);
                transcript = transcript.With(new ToolMessage(response));
            }
        }

        return new AgentLoopResult(AgentLoopOutcome.StepLimitReached, null, transcript, options.MaxSteps, usage);
    }

    private static async Task<ToolResponse> InvokeAsync(
        IToolInvoker tools,
        HashSet<string> offered,
        ToolRequest request,
        CancellationToken cancellationToken)
    {
        if (!offered.Contains(request.Name))
        {
            return new ToolResponse(
                request,
                new InvalidOperationException($"Unknown tool '{request.Name}'. Call only the tools you were given."));
        }

        try
        {
            IReadOnlyList<Content> results = await tools.InvokeAsync(request, cancellationToken);
            return new ToolResponse(request, [.. results]);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            return new ToolResponse(request, exception);
        }
    }

    private static ModelSamplingParameters? ToSampling(IModelParameters parameters)
    {
        // IModelParameters.N has no ModelSamplingParameters twin on purpose (see its remarks): a
        // loop reads one reply per step, so it is dropped.
        var sampling = new ModelSamplingParameters(
            parameters.Temperature,
            parameters.TopP,
            parameters.FrequencyPenalty,
            parameters.PresencePenalty,
            parameters.MaxTokens,
            parameters.Seed,
            parameters.Stop,
            parameters.ReasoningEffort);
        return sampling.IsEmpty ? null : sampling;
    }
}
