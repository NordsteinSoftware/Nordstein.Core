using System.Text.Json;
using System.Text.RegularExpressions;

namespace Nordstein.Core.Workflows.Internal;

/// <inheritdoc />
internal sealed class WorkflowValidator : IWorkflowValidator
{
    private readonly Regex nodeId = new("^[A-Za-z][A-Za-z0-9_-]{0,63}$", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
    private readonly ConfigChecker configs;
    private readonly ReferenceChecker references;
    private readonly SchemaReader schemas;

    public WorkflowValidator(ConfigChecker configs, ReferenceChecker references, SchemaReader schemas)
    {
        this.configs = configs;
        this.references = references;
        this.schemas = schemas;
    }

    public IReadOnlyList<WorkflowValidationError> Validate(WorkflowGraph graph, IReadOnlyCollection<INodeType> nodeTypes, int maxNodes)
    {
        ArgumentNullException.ThrowIfNull(graph);
        NodeCatalog catalog = NodeCatalog.Of(nodeTypes);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxNodes, 1);
        List<WorkflowValidationError> errors = [];
        if (graph.Nodes.Count == 0)
        {
            errors.Add(new WorkflowValidationError(null, "A workflow has at least one node."));
            return errors;
        }

        if (graph.Nodes.Count > maxNodes)
        {
            errors.Add(new WorkflowValidationError(null, $"A workflow has at most {maxNodes} nodes; this one has {graph.Nodes.Count}."));
        }

        bool sound = CheckNodes(graph, catalog, errors);
        sound &= CheckEdges(graph, catalog, errors);
        if (!sound)
        {
            return errors;
        }

        var index = new GraphIndex(graph);
        List<int> triggers = Enumerable.Range(0, graph.Nodes.Count).Where(position => catalog.TryGet(graph.Nodes[position].Type, out INodeType? type) && type.IsTrigger).ToList();
        if (triggers.Count == 0)
        {
            errors.Add(new WorkflowValidationError(null, "A workflow has exactly one trigger node; this one has none."));
        }

        foreach (int extra in triggers.Skip(1))
        {
            errors.Add(new WorkflowValidationError(graph.Nodes[extra].Id, $"A workflow has exactly one trigger node; '{graph.Nodes[extra].Id}' is a second one."));
        }

        foreach (int position in index.OnCycle())
        {
            errors.Add(new WorkflowValidationError(graph.Nodes[position].Id, $"Node '{graph.Nodes[position].Id}' is on a cycle; a workflow has no loops."));
        }

        int? trigger = triggers.Count == 1 ? triggers[0] : null;
        HashSet<int> reachable = trigger is { } root ? index.ReachableFrom(root) : [];
        if (trigger is not null)
        {
            foreach (NodeDefinition node in graph.Nodes.Where((_, position) => !reachable.Contains(position)))
            {
                errors.Add(new WorkflowValidationError(node.Id, $"Node '{node.Id}' cannot be reached from the trigger."));
            }
        }

        if (trigger is { } readOnlyRoot && catalog.TryGet(graph.Nodes[readOnlyRoot].Type, out INodeType? rootType) && rootType.ReadOnly)
        {
            foreach (NodeDefinition node in graph.Nodes.Where(node => catalog.TryGet(node.Type, out INodeType? type) && !type.ReadOnly))
            {
                errors.Add(new WorkflowValidationError(node.Id, $"Node '{node.Id}' has the type '{node.Type}', which is not read-only; a graph started by the read-only trigger '{graph.Nodes[readOnlyRoot].Type}' holds only read-only nodes."));
            }
        }

        Dictionary<int, List<ReferenceSite>> sites = CheckConfigs(graph, catalog, errors);
        Dictionary<int, int> bodies = index.IsAcyclic ? CheckBodies(index, catalog, trigger, errors) : [];
        if (trigger is { } start && index.IsAcyclic)
        {
            CheckReferences(index, catalog, start, sites, bodies, errors);
        }

        return errors;
    }

    public IReadOnlyList<WorkflowValidationError> Warnings(WorkflowGraph graph, IReadOnlyCollection<INodeType> nodeTypes)
    {
        ArgumentNullException.ThrowIfNull(graph);
        NodeCatalog catalog = NodeCatalog.Of(nodeTypes);
        List<WorkflowValidationError> warnings = [];
        foreach (NodeDefinition node in graph.Nodes)
        {
            if (catalog.TryGet(node.Type, out INodeType? type) && catalog.Successor(type) is { } successor)
            {
                warnings.Add(new WorkflowValidationError(node.Id, $"Node '{node.Id}' uses '{node.Type}'; '{successor}' supersedes it."));
            }
        }

        return warnings;
    }

    private bool CheckNodes(WorkflowGraph graph, NodeCatalog catalog, List<WorkflowValidationError> errors)
    {
        bool sound = true;
        HashSet<string> seen = new(StringComparer.Ordinal);
        foreach (NodeDefinition node in graph.Nodes)
        {
            if (!nodeId.IsMatch(node.Id))
            {
                errors.Add(new WorkflowValidationError(node.Id, $"Node id '{node.Id}' is not a letter followed by up to 63 letters, digits, '-' or '_'."));
                sound = false;
            }
            else if (!seen.Add(node.Id))
            {
                errors.Add(new WorkflowValidationError(node.Id, $"Node id '{node.Id}' is used twice."));
                sound = false;
            }

            if (!catalog.TryGet(node.Type, out INodeType? type))
            {
                errors.Add(new WorkflowValidationError(node.Id, catalog.Problem(node) ?? $"Node '{node.Id}' has the unknown type '{node.Type}'."));
            }
            else if (type.Branches.Contains(ReservedBranches.Error, StringComparer.Ordinal))
            {
                errors.Add(new WorkflowValidationError(node.Id, $"Node '{node.Id}' has the type '{node.Type}', which declares the reserved branch '{ReservedBranches.Error}'."));
            }
            else if (type is IIteratingNodeType && !(type.Branches.Count == 2 && type.Branches.Contains(LoopBranches.Body, StringComparer.Ordinal) && type.Branches.Contains(LoopBranches.Done, StringComparer.Ordinal)))
            {
                errors.Add(new WorkflowValidationError(node.Id, $"Node '{node.Id}' has the type '{node.Type}', which iterates but does not declare exactly the branches '{LoopBranches.Body}' and '{LoopBranches.Done}'."));
            }
            else if (node.Retry is not null && type.IsTrigger)
            {
                errors.Add(new WorkflowValidationError(node.Id, $"Node '{node.Id}' retries, but a trigger starts the run and does not retry."));
            }
            else if (node.Retry is not null && type.HasSideEffects)
            {
                errors.Add(new WorkflowValidationError(node.Id, $"Node '{node.Id}' retries, but a '{node.Type}' node has side effects; only a read-only node retries."));
            }
            else if (node.Retry is not null && type is IIteratingNodeType)
            {
                errors.Add(new WorkflowValidationError(node.Id, $"Node '{node.Id}' retries, but a '{node.Type}' node is a loop the kernel runs; a body node retries instead."));
            }
        }

        return sound;
    }

    private static bool CheckEdges(WorkflowGraph graph, NodeCatalog catalog, List<WorkflowValidationError> errors)
    {
        bool sound = true;
        Dictionary<string, NodeDefinition> nodes = graph.Nodes.DistinctBy(node => node.Id, StringComparer.Ordinal).ToDictionary(node => node.Id, StringComparer.Ordinal);
        HashSet<(string, string, string?)> seen = [];
        foreach (EdgeDefinition edge in graph.Edges)
        {
            bool hasFrom = nodes.TryGetValue(edge.From, out NodeDefinition? from);
            bool hasTo = nodes.TryGetValue(edge.To, out NodeDefinition? to);
            if (!hasFrom || !hasTo)
            {
                string missing = hasFrom ? edge.To : edge.From;
                errors.Add(new WorkflowValidationError(hasFrom ? edge.From : null, $"An edge from '{edge.From}' to '{edge.To}' names the unknown node '{missing}'."));
                sound = false;
                continue;
            }

            if (string.Equals(edge.From, edge.To, StringComparison.Ordinal))
            {
                errors.Add(new WorkflowValidationError(edge.From, $"Node '{edge.From}' has an edge to itself; a workflow has no loops."));
                sound = false;
                continue;
            }

            if (!seen.Add((edge.From, edge.To, edge.Branch)))
            {
                errors.Add(new WorkflowValidationError(edge.From, $"The edge from '{edge.From}' to '{edge.To}' is listed twice."));
                continue;
            }

            if (to is not null && catalog.TryGet(to.Type, out INodeType? toType) && toType.IsTrigger)
            {
                errors.Add(new WorkflowValidationError(edge.To, $"The trigger '{edge.To}' has an edge into it; a trigger starts the run."));
            }

            if (from is not null && catalog.TryGet(from.Type, out INodeType? fromType))
            {
                CheckBranch(edge, fromType, errors);
            }
        }

        return sound;
    }

    private static void CheckBranch(EdgeDefinition edge, INodeType fromType, List<WorkflowValidationError> errors)
    {
        if (string.Equals(edge.Branch, ReservedBranches.Error, StringComparison.Ordinal))
        {
            // The error branch (ReservedBranches) leaves every node but the trigger, declared or not.
            if (fromType.IsTrigger)
            {
                errors.Add(new WorkflowValidationError(edge.From, $"The edge from '{edge.From}' to '{edge.To}' names the branch '{ReservedBranches.Error}', but a trigger does not fail into a branch."));
            }

            return;
        }

        if (fromType.Branches.Count == 0 && edge.Branch is not null)
        {
            errors.Add(new WorkflowValidationError(edge.From, $"The edge from '{edge.From}' to '{edge.To}' names branch '{edge.Branch}', but a '{fromType.Type}' node does not branch."));
        }
        else if (fromType.Branches.Count > 0 && (edge.Branch is null || !fromType.Branches.Contains(edge.Branch, StringComparer.Ordinal)))
        {
            errors.Add(new WorkflowValidationError(edge.From, $"The edge from '{edge.From}' to '{edge.To}' must name one of the branches {string.Join(", ", fromType.Branches.Select(branch => $"'{branch}'"))}."));
        }
    }

    private static readonly JsonElement IndexSchema = JsonDocument.Parse("""{"type":"integer"}""").RootElement.Clone();

    // The schema of {{item}} in a loop body: the items schema of the array the loop's ItemsProperty references, resolved
    // through the referenced node's output schema; null (any) when the reference is not a whole-string one, the source's
    // configuration did not pass, or the schema names no items.
    private JsonElement? ItemSchema(GraphIndex index, NodeCatalog catalog, int loop, Dictionary<int, List<ReferenceSite>> valid)
    {
        NodeDefinition node = index.Graph.Nodes[loop];
        if (!valid.TryGetValue(loop, out List<ReferenceSite>? sites) || !catalog.TryGet(node.Type, out INodeType? loopType) || loopType is not IIteratingNodeType type)
        {
            return null;
        }

        ReferenceSite? site = sites.FirstOrDefault(candidate => !candidate.InText && string.Equals(candidate.Location, "config." + type.ItemsProperty, StringComparison.Ordinal));
        if (site is null)
        {
            return null;
        }

        JsonElement? schema;
        if (site.Reference.Root == ReferenceRoot.Trigger)
        {
            NodeDefinition trigger = index.Graph.Nodes.First(candidate => catalog.TryGet(candidate.Type, out INodeType? candidateType) && candidateType.IsTrigger);
            schema = catalog.TryGet(trigger.Type, out INodeType? triggerType) ? triggerType.GetOutputSchema(trigger.Config) : null;
        }
        else if (site.Reference.NodeId is { } sourceId && index.Positions.TryGetValue(sourceId, out int source) && valid.ContainsKey(source))
        {
            NodeDefinition sourceNode = index.Graph.Nodes[source];
            schema = catalog.TryGet(sourceNode.Type, out INodeType? sourceType) ? sourceType.GetOutputSchema(sourceNode.Config) : null;
        }
        else
        {
            return null;
        }

        foreach (string segment in site.Reference.Path)
        {
            if (schema is not { } current || !schemas.TryChild(current, segment, out schema))
            {
                return null;
            }
        }

        return schema is { } array && schemas.TryChild(array, "0", out JsonElement? item) ? item : null;
    }

    private Dictionary<int, List<ReferenceSite>> CheckConfigs(WorkflowGraph graph, NodeCatalog catalog, List<WorkflowValidationError> errors)
    {
        Dictionary<int, List<ReferenceSite>> valid = [];
        for (int position = 0; position < graph.Nodes.Count; position++)
        {
            NodeDefinition node = graph.Nodes[position];
            if (!catalog.TryGet(node.Type, out INodeType? type))
            {
                continue;
            }

            List<string> problems = [];
            List<ReferenceSite> sites = [];
            configs.Check(node.Config, type.InputSchema, problems, sites);
            if (problems.Count == 0)
            {
                problems.AddRange(type.ValidateConfig(node.Config));
            }

            errors.AddRange(problems.Select(problem => new WorkflowValidationError(node.Id, $"Node '{node.Id}': {problem}")));
            if (problems.Count == 0)
            {
                valid[position] = sites;
            }
        }

        return valid;
    }

    // The loop bodies (IIteratingNodeType): each body node is reached only through its loop (the loop dominates it and no
    // other edge of the loop reaches it; an edge out of the body breaks that for its target), and the body holds no loop
    // and no suspending node. Returns each body
    // node's loop by position; a node in two bodies is reported once, under the first loop.
    private static Dictionary<int, int> CheckBodies(GraphIndex index, NodeCatalog catalog, int? trigger, List<WorkflowValidationError> errors)
    {
        Dictionary<int, int> bodies = [];
        IReadOnlyDictionary<int, HashSet<int>> dominators = trigger is { } root ? index.Dominators(root) : new Dictionary<int, HashSet<int>>();
        for (int loop = 0; loop < index.Graph.Nodes.Count; loop++)
        {
            NodeDefinition node = index.Graph.Nodes[loop];
            if (!catalog.TryGet(node.Type, out INodeType? type) || type is not IIteratingNodeType)
            {
                continue;
            }

            HashSet<int> body = index.BodyOf(loop);
            HashSet<int> afterwards = index.ReachableFromEdges(index.OutgoingEdges[loop].Where(edge => !string.Equals(edge.Branch, LoopBranches.Body, StringComparison.Ordinal)));
            foreach (int member in body.Order())
            {
                NodeDefinition inside = index.Graph.Nodes[member];
                if (!bodies.TryAdd(member, loop))
                {
                    continue;
                }

                if (catalog.TryGet(inside.Type, out INodeType? memberType) && memberType is IIteratingNodeType)
                {
                    errors.Add(new WorkflowValidationError(inside.Id, $"Node '{inside.Id}' is a loop inside the body of '{node.Id}'; loops do not nest."));
                }
                else if (memberType is ISuspendingNodeType)
                {
                    errors.Add(new WorkflowValidationError(inside.Id, $"Node '{inside.Id}' waits for an answer inside the body of '{node.Id}'; a loop body does not suspend."));
                }

                // The body is closed under reachability, so an edge out of it makes its target a body node reached from outside too.
                if (afterwards.Contains(member) || (dominators.TryGetValue(member, out HashSet<int>? before) && !before.Contains(loop)))
                {
                    errors.Add(new WorkflowValidationError(inside.Id, $"Node '{inside.Id}' is reached from the body of '{node.Id}' and from outside it; a body node runs only inside its loop, and the loop's done branch continues after the body."));
                }
            }
        }

        return bodies;
    }

    private void CheckReferences(
        GraphIndex index,
        NodeCatalog catalog,
        int trigger,
        Dictionary<int, List<ReferenceSite>> sites,
        Dictionary<int, int> bodies,
        List<WorkflowValidationError> errors)
    {
        IReadOnlyDictionary<int, HashSet<int>> dominators = index.Dominators(trigger);
        foreach ((int position, List<ReferenceSite> nodeSites) in sites.OrderBy(pair => pair.Key))
        {
            NodeDefinition node = index.Graph.Nodes[position];
            if (!dominators.TryGetValue(position, out HashSet<int>? before))
            {
                continue;
            }

            foreach (ReferenceSite site in nodeSites)
            {
                if (CheckReference(index, catalog, trigger, position, before, site, sites, bodies) is { } problem)
                {
                    errors.Add(new WorkflowValidationError(node.Id, $"Node '{node.Id}': {site.Location} {problem}"));
                }
            }
        }
    }

    private string? CheckReference(
        GraphIndex index,
        NodeCatalog catalog,
        int trigger,
        int position,
        HashSet<int> before,
        ReferenceSite site,
        Dictionary<int, List<ReferenceSite>> valid,
        Dictionary<int, int> bodies)
    {
        DataReference reference = site.Reference;
        if (reference.Root is ReferenceRoot.Item or ReferenceRoot.Index)
        {
            if (!bodies.TryGetValue(position, out int loop))
            {
                return $"references {reference.Text} outside a loop body.";
            }

            return references.Problem(site, reference.Root == ReferenceRoot.Index ? IndexSchema : ItemSchema(index, catalog, loop, valid), reference.Root == ReferenceRoot.Index ? "index" : "item");
        }

        int source;
        if (reference.Root == ReferenceRoot.Trigger)
        {
            source = trigger;
        }
        else if (reference.NodeId is null || !index.Positions.TryGetValue(reference.NodeId, out source))
        {
            return $"references the unknown node '{reference.NodeId}' in {reference.Text}.";
        }

        if (source == position)
        {
            return $"references the node's own output in {reference.Text}.";
        }

        if (bodies.TryGetValue(position, out int enclosing) && enclosing == source)
        {
            return $"references the output of its loop '{index.Graph.Nodes[source].Id}' in {reference.Text}, which exists only after the loop.";
        }

        if (!before.Contains(source))
        {
            return index.ReachableFrom(source).Contains(position)
                ? $"references '{index.Graph.Nodes[source].Id}' in {reference.Text}, which does not run on every path to this node."
                : $"references '{index.Graph.Nodes[source].Id}' in {reference.Text}, which does not run before this node.";
        }

        if (!valid.ContainsKey(source))
        {
            return null;
        }

        NodeDefinition sourceNode = index.Graph.Nodes[source];
        if (!catalog.TryGet(sourceNode.Type, out INodeType? sourceType))
        {
            return null;
        }

        JsonElement schema = index.ReachedOnlyThroughErrorEdge(source, position)
            ? ReservedBranches.ErrorOutputSchema
            : sourceType.GetOutputSchema(sourceNode.Config);
        return references.Problem(site, schema, sourceNode.Id);
    }
}
