using System.Text.Json;

namespace Nordstein.Core.Workflows.Internal;

/// <inheritdoc />
internal sealed class WorkflowInterpreter : IWorkflowInterpreter
{
    // The longest wait one Task.Delay accepts is uint.MaxValue - 1 milliseconds; a day less keeps clear of it.
    private static readonly TimeSpan MaxTimerWait = TimeSpan.FromDays(48);

    private readonly IWorkflowValidator validator;
    private readonly ReferenceResolver resolver;

    public WorkflowInterpreter(IWorkflowValidator validator, ReferenceResolver resolver)
    {
        this.validator = validator;
        this.resolver = resolver;
    }

    public Task<WorkflowOutcome> RunAsync(WorkflowExecution execution, JsonElement triggerInput, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(execution);
        if (triggerInput.ValueKind == JsonValueKind.Undefined)
        {
            throw new ArgumentException("The trigger input is a defined JSON value.", nameof(triggerInput));
        }

        var run = new Run(Prepare(execution), triggerInput);
        return ContinueAsync(run, null, cancellationToken);
    }

    public Task<WorkflowOutcome> ResumeAsync(WorkflowExecution execution, WorkflowSuspension suspension, JsonElement answer, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(execution);
        ArgumentNullException.ThrowIfNull(suspension);
        if (answer.ValueKind == JsonValueKind.Undefined)
        {
            throw new ArgumentException("The answer is a defined JSON value.", nameof(answer));
        }

        Prepared prepared = Prepare(execution);
        if (!prepared.Index.Positions.TryGetValue(suspension.NodeId, out int suspended)
            || prepared.Types[suspended] is not ISuspendingNodeType)
        {
            throw new ArgumentException($"The suspended node '{suspension.NodeId}' is not a node of the graph that can suspend.", nameof(suspension));
        }

        var run = new Run(prepared, suspension.TriggerInput);
        return ContinueAsync(run, new Resumption(suspension, answer, suspended), cancellationToken);
    }

    private static bool Reached(Run run, int position)
    {
        if (run.Prepared.Types[position].IsTrigger)
        {
            return true;
        }

        // An edge naming no branch is the normal path: a node that completed on the error branch (ReservedBranches) takes
        // only its error edges. A loop's body edges are taken while the loop iterates (LoopBranches.Body), its done edges
        // once it completed.
        return run.Prepared.Index.Incoming[position].Any(edge =>
            (run.Loop is { } loop && loop.Position == run.Prepared.Index.Positions[edge.From] && string.Equals(edge.Branch, LoopBranches.Body, StringComparison.Ordinal))
            || (run.Outputs.ContainsKey(edge.From)
                && (edge.Branch is null
                    ? !string.Equals(run.Branches[edge.From], ReservedBranches.Error, StringComparison.Ordinal)
                    : string.Equals(edge.Branch, run.Branches[edge.From], StringComparison.Ordinal))));
    }

    private Prepared Prepare(WorkflowExecution execution)
    {
        IReadOnlyList<WorkflowValidationError> errors = validator.Validate(execution.Graph, execution.NodeTypes, Math.Max(1, execution.Graph.Nodes.Count));
        if (errors.Count > 0)
        {
            throw new ArgumentException(
                "The workflow graph is invalid: " + string.Join(" ", errors.Select(error => error.Message)),
                nameof(execution));
        }

        NodeCatalog catalog = NodeCatalog.Of(execution.NodeTypes);
        var index = new GraphIndex(execution.Graph);
        INodeType[] types = execution.Graph.Nodes.Select(node => catalog.TryGet(node.Type, out INodeType? type)
            ? type
            : throw new ArgumentException($"The workflow graph is invalid: {catalog.Problem(node)}", nameof(execution))).ToArray();
        Dictionary<int, int> bodies = [];
        for (int loop = 0; loop < types.Length; loop++)
        {
            if (types[loop] is IIteratingNodeType)
            {
                foreach (int member in index.BodyOf(loop))
                {
                    bodies[member] = loop;
                }
            }
        }

        return new Prepared(execution, index, types, bodies);
    }

    private async Task<WorkflowOutcome> ContinueAsync(Run run, Resumption? resumption, CancellationToken cancellationToken)
    {
        WorkflowExecution execution = run.Prepared.Execution;
        Dictionary<string, CompletedStep> replay = resumption is null ? [] : Replay(resumption.Suspension);
        int replayed = 0;
        bool resumed = resumption is null;
        foreach (int position in run.Prepared.Index.Order)
        {
            // A body node runs inside its loop's iterations (RunLoopAsync), never on its own.
            if (run.Prepared.Bodies.ContainsKey(position) || !Reached(run, position))
            {
                continue;
            }

            NodeDefinition node = execution.Graph.Nodes[position];
            if (!resumed)
            {
                if (replay.TryGetValue(node.Id, out CompletedStep? done))
                {
                    run.Complete(node.Id, done.Output, done.Branch);
                    run.StepsStarted++;
                    replayed++;
                    continue;
                }

                if (resumption is null || position != resumption.Position || replayed != replay.Count)
                {
                    throw new ArgumentException(
                        $"The suspension does not fit the graph: node '{node.Id}' would run before the suspended node '{resumption?.Suspension.NodeId}' but has no completed step, or a completed step would not have run.",
                        "suspension");
                }

                resumed = true;
                run.StepsStarted++;
                NodeContext resumeContext = Context(run, position);
                var suspending = (ISuspendingNodeType)run.Prepared.Types[position];
                NodeResult resumeResult = await InvokeAsync(
                    run, node.Id, () => suspending.ResumeAsync(resumeContext, resumption.Suspension.Bookmark, resumption.Answer, cancellationToken), cancellationToken);
                if (await RecordAsync(run, position, resumeResult, cancellationToken) is { } resumeEnd)
                {
                    return resumeEnd;
                }

                continue;
            }

            cancellationToken.ThrowIfCancellationRequested();
            NodeContext context = Context(run, position);
            await execution.Journal.StepStartedAsync(node.Id, node.Type, context.Input, cancellationToken);
            if (run.StepsStarted >= execution.MaxSteps)
            {
                var budget = new WorkflowFailure(WorkflowFailureKind.StepBudgetExhausted, $"The run reached its limit of {execution.MaxSteps} steps.");
                await execution.Journal.StepFailedAsync(node.Id, budget, cancellationToken);
                return new WorkflowOutcome.Failed(node.Id, budget);
            }

            run.StepsStarted++;
            INodeType type = run.Prepared.Types[position];
            if (execution.Pins.TryGetValue(node.Id, out PinnedStep? pin))
            {
                if (await PinAsync(run, position, pin, cancellationToken) is { } pinnedEnd)
                {
                    return pinnedEnd;
                }

                continue;
            }

            if (type is IIteratingNodeType loop)
            {
                if (await RunLoopAsync(run, position, loop, context, cancellationToken) is { } loopEnd)
                {
                    return loopEnd;
                }

                continue;
            }

            NodeResult result = await ExecuteWithRetryAsync(run, node, type, context, cancellationToken);
            if (await RecordAsync(run, position, result, cancellationToken) is { } end)
            {
                return end;
            }
        }

        if (!resumed)
        {
            throw new ArgumentException(
                $"The suspension does not fit the graph: the suspended node '{resumption?.Suspension.NodeId}' would not run.",
                "suspension");
        }

        return new WorkflowOutcome.Completed();
    }

    private static Dictionary<string, CompletedStep> Replay(WorkflowSuspension suspension)
    {
        Dictionary<string, CompletedStep> replay = new(StringComparer.Ordinal);
        foreach (CompletedStep step in suspension.CompletedSteps)
        {
            if (step.NodeId is null || step.Output.ValueKind == JsonValueKind.Undefined || !replay.TryAdd(step.NodeId, step))
            {
                throw new ArgumentException("The suspension's completed steps name each node once, with a defined output.", nameof(suspension));
            }
        }

        if (replay.ContainsKey(suspension.NodeId))
        {
            throw new ArgumentException($"The suspended node '{suspension.NodeId}' is also a completed step.", nameof(suspension));
        }

        return replay;
    }

    // The loop (IIteratingNodeType): the array under ItemsProperty, capped by MaxItems; per item, the body nodes in the
    // graph's order with the item and index as reference roots, each a journaled step counted against the budget; the
    // body's outputs are forgotten after each iteration. A failed body step fails the loop's step with its reason (the loop's
    // error edge may take it); after the last item the loop completes on done with its collection.
    private async Task<WorkflowOutcome?> RunLoopAsync(Run run, int position, IIteratingNodeType loop, NodeContext context, CancellationToken cancellationToken)
    {
        WorkflowExecution execution = run.Prepared.Execution;
        NodeDefinition node = execution.Graph.Nodes[position];
        if (!context.Input.TryGetProperty(loop.ItemsProperty, out JsonElement items) || items.ValueKind != JsonValueKind.Array)
        {
            return await FailAsync(run, position, $"{loop.ItemsProperty} is not an array once resolved.", cancellationToken);
        }

        // The loop's hooks are the node type's own code, like ExecuteAsync: one that throws fails the step with the
        // same reason a throwing node gets (NodeInvoker), so the loop's step never stays open.
        int max;
        try
        {
            max = loop.MaxItems(node.Config);
        }
        catch (Exception exception)
        {
            return await FailAsync(run, position, NodeInvoker.Reason(exception), cancellationToken);
        }

        if (items.GetArrayLength() > max)
        {
            return await FailAsync(run, position, $"{loop.ItemsProperty} holds {items.GetArrayLength()} items; at most {max}.", cancellationToken);
        }

        List<int> body = [.. run.Prepared.Index.Order.Where(member => run.Prepared.Bodies.TryGetValue(member, out int owner) && owner == position)];
        List<JsonElement> results = [];
        int index = 0;
        foreach (JsonElement item in items.EnumerateArray())
        {
            run.Loop = new LoopScope(position, item, index);
            JsonElement? last = null;
            foreach (int member in body)
            {
                if (!Reached(run, member))
                {
                    continue;
                }

                WorkflowOutcome.Failed? failed;
                try
                {
                    failed = await BodyStepAsync(run, member, index, cancellationToken);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    // The body step closed itself (InvokeAsync); the loop's own step is still open and the journal
                    // contract promises it an end too, on CancellationToken.None for the same reason.
                    run.Loop = null;
                    ForgetBody(run, body);
                    await execution.Journal.StepFailedAsync(
                        node.Id, new WorkflowFailure(WorkflowFailureKind.Cancelled, "The run was cancelled while this loop ran."), CancellationToken.None);
                    throw;
                }

                if (failed is not null)
                {
                    run.Loop = null;
                    ForgetBody(run, body);
                    if (failed.Failure.Kind != WorkflowFailureKind.NodeFailed)
                    {
                        // The step budget: the loop fails with the same kind, which no error edge takes.
                        await execution.Journal.StepFailedAsync(node.Id, failed.Failure, cancellationToken);
                        return new WorkflowOutcome.Failed(node.Id, failed.Failure);
                    }

                    return await FailAsync(run, position, $"iteration {index}, step '{execution.Graph.Nodes[member].Id}': {failed.Failure.Message}", cancellationToken);
                }

                if (run.Outputs.TryGetValue(execution.Graph.Nodes[member].Id, out JsonElement output))
                {
                    last = output;
                }
            }

            results.Add(last ?? JsonSerializer.SerializeToElement<object?>(null));
            ForgetBody(run, body);
            index++;
        }

        run.Loop = null;
        JsonElement collected;
        try
        {
            collected = loop.Collect(context, results);
        }
        catch (Exception exception)
        {
            return await FailAsync(run, position, NodeInvoker.Reason(exception), cancellationToken);
        }

        await execution.Journal.StepCompletedAsync(node.Id, collected, LoopBranches.Done, cancellationToken);
        run.Complete(node.Id, collected, LoopBranches.Done);
        return null;
    }

    // One body node in one iteration: started with its iteration, the budget, a pin or the node's tries, then recorded; the
    // failed outcome when the step failed without an error edge, else null.
    private async Task<WorkflowOutcome.Failed?> BodyStepAsync(Run run, int position, int iteration, CancellationToken cancellationToken)
    {
        WorkflowExecution execution = run.Prepared.Execution;
        NodeDefinition node = execution.Graph.Nodes[position];
        INodeType type = run.Prepared.Types[position];
        cancellationToken.ThrowIfCancellationRequested();
        NodeContext context = Context(run, position);
        await execution.Journal.StepStartedAsync(node.Id, node.Type, context.Input, iteration, cancellationToken);
        if (run.StepsStarted >= execution.MaxSteps)
        {
            var budget = new WorkflowFailure(WorkflowFailureKind.StepBudgetExhausted, $"The run reached its limit of {execution.MaxSteps} steps.");
            await execution.Journal.StepFailedAsync(node.Id, budget, cancellationToken);
            return new WorkflowOutcome.Failed(node.Id, budget);
        }

        run.StepsStarted++;
        WorkflowOutcome? outcome = execution.Pins.TryGetValue(node.Id, out PinnedStep? pin)
            ? await PinAsync(run, position, pin, cancellationToken)
            : await RecordAsync(run, position, await ExecuteWithRetryAsync(run, node, type, context, cancellationToken), cancellationToken);
        return outcome switch
        {
            null => null,
            WorkflowOutcome.Failed failed => failed,
            _ => throw new InvalidOperationException($"A body node '{node.Id}' ended the run with {outcome.GetType().Name}; the validator allows no suspending node in a body."),
        };
    }

    private static void ForgetBody(Run run, List<int> body)
    {
        foreach (int member in body)
        {
            string id = run.Prepared.Execution.Graph.Nodes[member].Id;
            run.Outputs.Remove(id);
            run.Branches.Remove(id);
        }
    }

    // The node's tries (NodeDefinition.Retry): a Failed result (an exception became one) short of the last try is journaled
    // as a retry, the backoff waited on the run's token, and the node run again; the last try's result stands. A
    // cancellation during a try or a backoff ends the step Cancelled and propagates (InvokeAsync).
    private static async Task<NodeResult> ExecuteWithRetryAsync(Run run, NodeDefinition node, INodeType type, NodeContext context, CancellationToken cancellationToken)
    {
        int attempts = node.Retry?.Attempts ?? 1;
        for (int attempt = 1; ; attempt++)
        {
            NodeResult result = await InvokeAsync(run, node.Id, () => type.ExecuteAsync(context, cancellationToken), cancellationToken);
            if (result is not NodeResult.Failed failed || attempt >= attempts || node.Retry is not { } retry)
            {
                return result;
            }

            await run.Prepared.Execution.Journal.StepRetryingAsync(node.Id, attempt, new WorkflowFailure(WorkflowFailureKind.NodeFailed, failed.Reason), cancellationToken);
            await InvokeAsync(run, node.Id, async () =>
            {
                await WaitAsync(retry.Backoff, cancellationToken);
                return new NodeResult.Completed(context.Input);
            }, cancellationToken);
        }
    }

    // Task.Delay refuses a wait longer than one timer can hold (about 49 days) with an ArgumentOutOfRangeException, which
    // InvokeAsync would turn into a skipped backoff; a RetryPolicy allows any non-negative backoff, so it is waited in parts.
    private static async Task WaitAsync(TimeSpan backoff, CancellationToken cancellationToken)
    {
        TimeSpan remaining = backoff;
        while (remaining > MaxTimerWait)
        {
            await Task.Delay(MaxTimerWait, cancellationToken);
            remaining -= MaxTimerWait;
        }

        await Task.Delay(remaining, cancellationToken);
    }

    private static async Task<NodeResult> InvokeAsync(Run run, string nodeId, Func<Task<NodeResult>> invoke, CancellationToken cancellationToken)
    {
        try
        {
            return await NodeInvoker.InvokeAsync(invoke, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // WHY: the run's token is already cancelled; passing it would abort the very entry that closes the
            // started step, and the journal contract promises every started step an end (IStepJournal remarks).
            await run.Prepared.Execution.Journal.StepFailedAsync(
                nodeId, new WorkflowFailure(WorkflowFailureKind.Cancelled, "The run was cancelled while this step ran."), CancellationToken.None);
            throw;
        }
    }

    /// <summary>
    /// Journals the started node as pinned with <paramref name="pin"/>; the failed outcome when the pin names a branch
    /// the type does not declare, otherwise null.
    /// </summary>
    private static async Task<WorkflowOutcome?> PinAsync(Run run, int position, PinnedStep pin, CancellationToken cancellationToken)
    {
        IStepJournal journal = run.Prepared.Execution.Journal;
        INodeType type = run.Prepared.Types[position];
        string nodeId = run.Prepared.Execution.Graph.Nodes[position].Id;
        // A pin on the error branch stubs the failure itself (the kernel's branch, which no type chooses): it stands when
        // the node has an error edge to take it.
        bool pinnedError = string.Equals(pin.Branch, ReservedBranches.Error, StringComparison.Ordinal) && run.Prepared.Index.HasErrorEdge(position);
        if (!pin.Unchecked && !pinnedError && NodeInvoker.Problem(type, new NodeResult.Completed(pin.Output, pin.Branch)) is { } problem)
        {
            return await FailAsync(run, position, $"The pinned step: {problem}", cancellationToken);
        }

        await journal.StepPinnedAsync(nodeId, pin.Output, pin.Branch, cancellationToken);
        run.Complete(nodeId, pin.Output, pin.Branch);
        return null;
    }

    /// <summary>
    /// Journals <paramref name="result"/>; the outcome when the run ends here, otherwise null.
    /// </summary>
    private static async Task<WorkflowOutcome?> RecordAsync(Run run, int position, NodeResult result, CancellationToken cancellationToken)
    {
        IStepJournal journal = run.Prepared.Execution.Journal;
        INodeType type = run.Prepared.Types[position];
        string nodeId = run.Prepared.Execution.Graph.Nodes[position].Id;
        if (NodeInvoker.Problem(type, result) is { } problem)
        {
            return await FailAsync(run, position, problem, cancellationToken);
        }

        switch (result)
        {
            case NodeResult.Completed completed:
                await journal.StepCompletedAsync(nodeId, completed.Output, completed.Branch, cancellationToken);
                run.Complete(nodeId, completed.Output, completed.Branch);
                return null;
            case NodeResult.Suspended suspended:
                await journal.StepSuspendedAsync(nodeId, suspended.Bookmark, cancellationToken);
                return new WorkflowOutcome.Suspended(nodeId, suspended.Bookmark);
            case NodeResult.Failed failed:
                return await FailAsync(run, position, failed.Reason, cancellationToken);
            default:
                throw new InvalidOperationException($"Unknown node result {result.GetType().Name}.");
        }
    }

    /// <summary>
    /// The node failed (<see cref="WorkflowFailureKind.NodeFailed"/>): with an edge on <see cref="ReservedBranches.Error"/>
    /// it completes on that branch with <c>{ message, nodeType }</c> and the run goes on (null); without one the run fails.
    /// </summary>
    private static async Task<WorkflowOutcome?> FailAsync(Run run, int position, string reason, CancellationToken cancellationToken)
    {
        IStepJournal journal = run.Prepared.Execution.Journal;
        NodeDefinition node = run.Prepared.Execution.Graph.Nodes[position];
        var failure = new WorkflowFailure(WorkflowFailureKind.NodeFailed, reason);
        if (!run.Prepared.Index.HasErrorEdge(position))
        {
            await journal.StepFailedAsync(node.Id, failure, cancellationToken);
            return new WorkflowOutcome.Failed(node.Id, failure);
        }

        JsonElement output = JsonSerializer.SerializeToElement(new ErrorOutput(reason, node.Type));
        await journal.StepRecoveredAsync(node.Id, failure, output, cancellationToken);
        run.Complete(node.Id, output, ReservedBranches.Error);
        return null;
    }

    private NodeContext Context(Run run, int position)
    {
        NodeDefinition node = run.Prepared.Execution.Graph.Nodes[position];
        string triggerId = run.Prepared.Execution.Graph.Nodes[run.Prepared.Trigger].Id;
        JsonElement input = resolver.Resolve(
            node.Config,
            reference => reference.Root switch
            {
                ReferenceRoot.Item => run.Loop?.Item,
                ReferenceRoot.Index => run.Loop?.IndexValue,
                _ => run.Outputs.TryGetValue(reference.Root == ReferenceRoot.Trigger ? triggerId : (reference.NodeId ?? triggerId), out JsonElement output) ? output : null,
            });
        return new NodeContext(node.Id, node.Type, input, run.TriggerInput, run.Prepared.Execution.DryRun);
    }

    private sealed record Prepared(WorkflowExecution Execution, GraphIndex Index, IReadOnlyList<INodeType> Types, IReadOnlyDictionary<int, int> Bodies)
    {
        public int Trigger { get; } = Types.Select((type, position) => (type, position)).Single(pair => pair.type.IsTrigger).position;
    }

    private sealed record Resumption(WorkflowSuspension Suspension, JsonElement Answer, int Position);

    /// <summary>One iteration of a loop: the loop's position, the item and its index, the roots of <c>{{item}}</c> and <c>{{index}}</c>.</summary>
    private sealed record LoopScope(int Position, JsonElement Item, int Index)
    {
        public JsonElement IndexValue { get; } = JsonSerializer.SerializeToElement(Index);
    }

    /// <summary>The output of a node that completed on the error branch (<see cref="ReservedBranches.ErrorOutputSchema"/>).</summary>
    private sealed record ErrorOutput(
        [property: System.Text.Json.Serialization.JsonPropertyName("message")] string Message,
        [property: System.Text.Json.Serialization.JsonPropertyName("nodeType")] string NodeType);

    /// <summary>
    /// One pass's mutable state; never shared between passes.
    /// </summary>
    private sealed class Run
    {
        public Run(Prepared prepared, JsonElement triggerInput)
        {
            Prepared = prepared;
            TriggerInput = triggerInput;
        }

        public Prepared Prepared { get; }

        public JsonElement TriggerInput { get; }

        public Dictionary<string, JsonElement> Outputs { get; } = new(StringComparer.Ordinal);

        public Dictionary<string, string?> Branches { get; } = new(StringComparer.Ordinal);

        public int StepsStarted { get; set; }

        /// <summary>The loop iterating right now, or null.</summary>
        public LoopScope? Loop { get; set; }

        public void Complete(string nodeId, JsonElement output, string? branch)
        {
            Outputs[nodeId] = output;
            Branches[nodeId] = branch;
        }
    }
}
