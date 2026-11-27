namespace Nordstein.Core.Workflows.Internal;

/// <summary>
/// The adjacency of a graph whose node ids are unique and whose edges join existing nodes, by node position in
/// <see cref="WorkflowGraph.Nodes"/>. Immutable once built.
/// </summary>
internal sealed class GraphIndex
{
    public GraphIndex(WorkflowGraph graph)
    {
        ArgumentNullException.ThrowIfNull(graph);
        Graph = graph;
        Positions = graph.Nodes.Select((node, position) => (node.Id, position)).ToDictionary(pair => pair.Id, pair => pair.position, StringComparer.Ordinal);
        var incoming = graph.Nodes.Select(_ => new List<EdgeDefinition>()).ToArray();
        var outgoing = graph.Nodes.Select(_ => new List<int>()).ToArray();
        var outgoingEdges = graph.Nodes.Select(_ => new List<EdgeDefinition>()).ToArray();
        foreach (EdgeDefinition edge in graph.Edges)
        {
            int from = Positions[edge.From];
            int to = Positions[edge.To];
            incoming[to].Add(edge);
            outgoing[from].Add(to);
            outgoingEdges[from].Add(edge);
        }

        Incoming = incoming;
        Outgoing = outgoing;
        OutgoingEdges = outgoingEdges;
        Order = TopologicalOrder();
    }

    public WorkflowGraph Graph { get; }

    public IReadOnlyDictionary<string, int> Positions { get; }

    public IReadOnlyList<IReadOnlyList<EdgeDefinition>> Incoming { get; }

    public IReadOnlyList<IReadOnlyList<int>> Outgoing { get; }

    public IReadOnlyList<IReadOnlyList<EdgeDefinition>> OutgoingEdges { get; }

    /// <summary>
    /// Whether <paramref name="position"/> has an edge leaving it on <see cref="ReservedBranches.Error"/>.
    /// </summary>
    public bool HasErrorEdge(int position) => OutgoingEdges[position].Any(edge => string.Equals(edge.Branch, ReservedBranches.Error, StringComparison.Ordinal));

    /// <summary>
    /// The positions reachable through <paramref name="loop"/>'s <see cref="LoopBranches.Body"/> edges: its body.
    /// </summary>
    public HashSet<int> BodyOf(int loop) =>
        ReachableFromEdges(OutgoingEdges[loop].Where(edge => string.Equals(edge.Branch, LoopBranches.Body, StringComparison.Ordinal)));

    /// <summary>
    /// Whether <paramref name="target"/> is reached from <paramref name="start"/> only through <paramref name="start"/>'s
    /// error edges: every path from the start to the target begins with an edge on <see cref="ReservedBranches.Error"/>.
    /// False when the target is not reached at all.
    /// </summary>
    public bool ReachedOnlyThroughErrorEdge(int start, int target)
    {
        HashSet<int> throughError = ReachableFromEdges(OutgoingEdges[start].Where(edge => string.Equals(edge.Branch, ReservedBranches.Error, StringComparison.Ordinal)));
        HashSet<int> otherwise = ReachableFromEdges(OutgoingEdges[start].Where(edge => !string.Equals(edge.Branch, ReservedBranches.Error, StringComparison.Ordinal)));
        return throughError.Contains(target) && !otherwise.Contains(target);
    }

    /// <summary>
    /// Kahn's order, the lowest ready position first; shorter than the graph when there is a cycle.
    /// </summary>
    public IReadOnlyList<int> Order { get; }

    public bool IsAcyclic => Order.Count == Graph.Nodes.Count;

    /// <summary>
    /// The positions on a cycle (a node that reaches itself), ascending.
    /// </summary>
    public IReadOnlyList<int> OnCycle()
    {
        HashSet<int> ordered = [.. Order];
        return Enumerable.Range(0, Graph.Nodes.Count)
            .Where(position => !ordered.Contains(position) && Reaches(position, position))
            .ToList();
    }

    /// <summary>
    /// The positions reachable from <paramref name="start"/>, itself included.
    /// </summary>
    public HashSet<int> ReachableFrom(int start)
    {
        HashSet<int> seen = [start];
        Stack<int> pending = new([start]);
        while (pending.TryPop(out int current))
        {
            foreach (int next in Outgoing[current])
            {
                if (seen.Add(next))
                {
                    pending.Push(next);
                }
            }
        }

        return seen;
    }

    /// <summary>
    /// For an acyclic graph, each reachable node's dominators from <paramref name="root"/>: the nodes on every path
    /// from the root to it, itself included. Unreachable nodes are absent.
    /// </summary>
    public IReadOnlyDictionary<int, HashSet<int>> Dominators(int root)
    {
        HashSet<int> reachable = ReachableFrom(root);
        Dictionary<int, HashSet<int>> dominators = [];
        foreach (int position in Order.Where(reachable.Contains))
        {
            HashSet<int>? common = null;
            foreach (EdgeDefinition edge in Incoming[position])
            {
                if (position == root || !dominators.TryGetValue(Positions[edge.From], out HashSet<int>? before))
                {
                    continue;
                }

                if (common is null)
                {
                    common = [.. before];
                }
                else
                {
                    common.IntersectWith(before);
                }
            }

            HashSet<int> own = common ?? [];
            own.Add(position);
            dominators[position] = own;
        }

        return dominators;
    }

    /// <summary>
    /// The positions reachable by following <paramref name="first"/> and then any edge; the edges' sources are not included.
    /// </summary>
    public HashSet<int> ReachableFromEdges(IEnumerable<EdgeDefinition> first)
    {
        HashSet<int> seen = [];
        Stack<int> pending = new(first.Select(edge => Positions[edge.To]));
        while (pending.TryPop(out int current))
        {
            if (seen.Add(current))
            {
                foreach (int next in Outgoing[current])
                {
                    pending.Push(next);
                }
            }
        }

        return seen;
    }

    private bool Reaches(int from, int target)
    {
        HashSet<int> seen = [];
        Stack<int> pending = new(Outgoing[from]);
        while (pending.TryPop(out int current))
        {
            if (current == target)
            {
                return true;
            }

            if (seen.Add(current))
            {
                foreach (int next in Outgoing[current])
                {
                    pending.Push(next);
                }
            }
        }

        return false;
    }

    private List<int> TopologicalOrder()
    {
        int[] waiting = Incoming.Select(edges => edges.Count).ToArray();
        SortedSet<int> ready = [.. Enumerable.Range(0, waiting.Length).Where(position => waiting[position] == 0)];
        List<int> order = [];
        while (ready.Count > 0)
        {
            int next = ready.Min;
            ready.Remove(next);
            order.Add(next);
            foreach (int successor in Outgoing[next])
            {
                waiting[successor]--;
                if (waiting[successor] == 0)
                {
                    ready.Add(successor);
                }
            }
        }

        return order;
    }
}
