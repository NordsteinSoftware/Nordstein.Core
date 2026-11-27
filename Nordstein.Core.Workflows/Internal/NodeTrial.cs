using System.Text.Json;

namespace Nordstein.Core.Workflows.Internal;

/// <inheritdoc />
internal sealed class NodeTrial : INodeTrial
{
    private readonly ConfigChecker configs;
    private readonly ReferenceChecker references;
    private readonly ReferenceResolver resolver;
    private readonly OutputChecker outputs;

    public NodeTrial(ConfigChecker configs, ReferenceChecker references, ReferenceResolver resolver, OutputChecker outputs)
    {
        this.configs = configs;
        this.references = references;
        this.resolver = resolver;
        this.outputs = outputs;
    }

    public async Task<NodeTrialResult> TryAsync(
        WorkflowGraph graph,
        IReadOnlyCollection<INodeType> nodeTypes,
        string nodeId,
        JsonElement triggerOutput,
        IReadOnlyDictionary<string, JsonElement> outputs,
        bool dryRun,
        JsonElement? answer,
        IterationStub? iteration,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(graph);
        ArgumentNullException.ThrowIfNull(nodeId);
        ArgumentNullException.ThrowIfNull(outputs);
        NodeCatalog catalog = NodeCatalog.Of(nodeTypes);
        Defined(triggerOutput, nameof(triggerOutput));
        foreach ((string id, JsonElement output) in outputs)
        {
            Defined(output, nameof(outputs), $"The output of '{id}'");
        }

        if (answer is { } given)
        {
            Defined(given, nameof(answer));
        }

        NodeDefinition node = graph.Nodes.FirstOrDefault(candidate => string.Equals(candidate.Id, nodeId, StringComparison.Ordinal))
            ?? throw new ArgumentException($"The graph has no node '{nodeId}'.", nameof(nodeId));
        if (!catalog.TryGet(node.Type, out INodeType? type))
        {
            return Stopped([new WorkflowValidationError(node.Id, catalog.Problem(node) ?? $"Node '{node.Id}' has the unknown type '{node.Type}'.")]);
        }

        List<WorkflowValidationError> problems = CheckConfig(node, type, out List<ReferenceSite> sites);
        if (problems.Count == 0)
        {
            problems.AddRange(CheckReferences(graph, catalog, node, sites, outputs, iteration));
        }

        if (problems.Count > 0)
        {
            return Stopped(problems);
        }

        cancellationToken.ThrowIfCancellationRequested();
        JsonElement indexValue = iteration is { } stub ? JsonSerializer.SerializeToElement(stub.Index) : default;
        JsonElement input = resolver.Resolve(node.Config, reference => reference.Root switch
        {
            ReferenceRoot.Trigger => triggerOutput,
            ReferenceRoot.Item => iteration?.Item,
            ReferenceRoot.Index => iteration is null ? null : indexValue,
            _ => reference.NodeId is { } id && outputs.TryGetValue(id, out JsonElement output) ? output : null,
        });
        var context = new NodeContext(node.Id, node.Type, input, triggerOutput, dryRun);
        // A loop node is run by the kernel, never executed (IIteratingNodeType): a try reports an empty collection.
        NodeResult result = type is IIteratingNodeType loop
            ? new NodeResult.Completed(loop.Collect(context, []), LoopBranches.Done)
            : await NodeInvoker.InvokeAsync(() => type.ExecuteAsync(context, cancellationToken), cancellationToken);
        if (result is NodeResult.Suspended suspended && answer is { } resume && type is ISuspendingNodeType suspending)
        {
            result = await NodeInvoker.InvokeAsync(() => suspending.ResumeAsync(context, suspended.Bookmark, resume, cancellationToken), cancellationToken);
        }

        if (NodeInvoker.Problem(type, result) is { } problem)
        {
            result = new NodeResult.Failed(problem);
        }

        IReadOnlyList<OutputProblem> mismatches = result is NodeResult.Completed completed
            ? this.outputs.Check(completed.Output, type.GetOutputSchema(node.Config))
            : [];
        return new NodeTrialResult(input, result, [], mismatches);
    }

    private static NodeTrialResult Stopped(IReadOnlyList<WorkflowValidationError> problems) => new(null, null, problems, []);

    private static void Defined(JsonElement value, string name, string? what = null)
    {
        if (value.ValueKind == JsonValueKind.Undefined)
        {
            throw new ArgumentException($"{what ?? "The value"} is a defined JSON value.", name);
        }
    }

    private List<WorkflowValidationError> CheckConfig(NodeDefinition node, INodeType type, out List<ReferenceSite> sites)
    {
        List<string> problems = [];
        sites = [];
        configs.Check(node.Config, type.InputSchema, problems, sites);
        if (problems.Count == 0)
        {
            problems.AddRange(type.ValidateConfig(node.Config));
        }

        return problems.Select(problem => new WorkflowValidationError(node.Id, $"Node '{node.Id}': {problem}")).ToList();
    }

    private List<WorkflowValidationError> CheckReferences(
        WorkflowGraph graph,
        NodeCatalog catalog,
        NodeDefinition node,
        List<ReferenceSite> sites,
        IReadOnlyDictionary<string, JsonElement> outputs,
        IterationStub? iteration)
    {
        List<WorkflowValidationError> problems = [];
        Dictionary<string, JsonElement?> schemas = new(StringComparer.Ordinal);
        foreach (ReferenceSite site in sites)
        {
            if (site.Reference.Root is ReferenceRoot.Item or ReferenceRoot.Index)
            {
                if (iteration is null)
                {
                    problems.Add(new WorkflowValidationError(node.Id, $"Node '{node.Id}': {site.Location} references {site.Reference.Text}, which has no item to try with."));
                }

                continue;
            }

            if (Problem(graph, catalog, node, site, outputs, schemas) is { } problem)
            {
                problems.Add(new WorkflowValidationError(node.Id, $"Node '{node.Id}': {site.Location} {problem}"));
            }
        }

        return problems;
    }

    private string? Problem(
        WorkflowGraph graph,
        NodeCatalog catalog,
        NodeDefinition node,
        ReferenceSite site,
        IReadOnlyDictionary<string, JsonElement> outputs,
        Dictionary<string, JsonElement?> schemas)
    {
        DataReference reference = site.Reference;
        NodeDefinition? source;
        if (reference.Root == ReferenceRoot.Trigger)
        {
            source = graph.Nodes.FirstOrDefault(candidate => catalog.TryGet(candidate.Type, out INodeType? candidateType) && candidateType.IsTrigger);
        }
        else
        {
            source = graph.Nodes.FirstOrDefault(candidate => string.Equals(candidate.Id, reference.NodeId, StringComparison.Ordinal));
            if (source is null)
            {
                return $"references the unknown node '{reference.NodeId}' in {reference.Text}.";
            }

            if (string.Equals(source.Id, node.Id, StringComparison.Ordinal))
            {
                return $"references the node's own output in {reference.Text}.";
            }

            if (!outputs.ContainsKey(source.Id))
            {
                return $"references '{source.Id}' in {reference.Text}, which has no output to try with.";
            }
        }

        if (source is null)
        {
            // A graph without a trigger: the supplied trigger output stands, with any path.
            return references.Problem(site, null, "trigger");
        }

        if (!schemas.TryGetValue(source.Id, out JsonElement? schema))
        {
            schema = OutputSchema(source, catalog);
            schemas[source.Id] = schema;
        }

        return references.Problem(site, schema, source.Id);
    }

    private JsonElement? OutputSchema(NodeDefinition source, NodeCatalog catalog)
    {
        if (!catalog.TryGet(source.Type, out INodeType? type))
        {
            return null;
        }

        List<string> problems = [];
        configs.Check(source.Config, type.InputSchema, problems, []);
        return problems.Count == 0 && type.ValidateConfig(source.Config).Count == 0 ? type.GetOutputSchema(source.Config) : null;
    }
}
