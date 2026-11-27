namespace Nordstein.Core.Workflows;

/// <summary>
/// Checks a <see cref="WorkflowGraph"/> against a node catalogue before a product stores or runs it.
/// </summary>
/// <remarks>
/// The checks: at least one and at most <c>maxNodes</c> nodes; node ids of the form
/// <c>[A-Za-z][A-Za-z0-9_-]{0,63}</c>, unique; every node type of the form <c>name@major</c>
/// (<see cref="NodeTypeReference"/>) and in the catalogue at that name and major; exactly one
/// trigger node and no edge into it; edges between existing nodes, no self-edge, no duplicate;
/// a branch on exactly the edges leaving a branching node, one its type declares, or the reserved
/// <see cref="ReservedBranches.Error"/> leaving any node but the trigger (no type declares it); a
/// <see cref="NodeDefinition.Retry"/> only on a non-trigger, non-iterating node whose type has no side effects; no cycle;
/// every node reachable from the trigger; in a graph whose trigger's type is <see cref="INodeType.ReadOnly"/>, only read-only nodes; every configuration valid against its type's input
/// schema and <see cref="INodeType.ValidateConfig"/>; every data reference well-formed, naming a
/// node that runs before the referencing node on every path from the trigger (so its output
/// exists), a path its output schema has (the error output's, <see cref="ReservedBranches.ErrorOutputSchema"/>,
/// for a node reached only through the source's error edge), and a type the input schema accepts at the reference's
/// place. Stateless and thread-safe.
/// </remarks>
public interface IWorkflowValidator
{
    /// <summary>
    /// Validates <paramref name="graph"/>.
    /// </summary>
    /// <param name="graph">The graph.</param>
    /// <param name="nodeTypes">The catalogue; each (name, major) at most once.</param>
    /// <param name="maxNodes">The most nodes a graph may have; at least 1.</param>
    /// <returns>Every problem found, in a stable order; empty for a valid graph.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="graph"/> or <paramref name="nodeTypes"/> is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="nodeTypes"/> holds a null item, a type with an invalid name or major, a read-only type that has side effects or suspends, or two types of one name and major.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="maxNodes"/> is less than 1.</exception>
    IReadOnlyList<WorkflowValidationError> Validate(WorkflowGraph graph, IReadOnlyCollection<INodeType> nodeTypes, int maxNodes);

    /// <summary>
    /// The advice a graph carries without being invalid: one warning per node whose type's major a newer major of
    /// the same name in <paramref name="nodeTypes"/> supersedes, in definition order. A node whose type the
    /// catalogue lacks gets none here; <see cref="Validate"/> reports it.
    /// </summary>
    /// <param name="graph">The graph.</param>
    /// <param name="nodeTypes">The catalogue; each (name, major) at most once.</param>
    /// <returns>The warnings; empty when every node uses its name's newest major.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="graph"/> or <paramref name="nodeTypes"/> is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="nodeTypes"/> holds a null item, a type with an invalid name or major, a read-only type that has side effects or suspends, or two types of one name and major.</exception>
    IReadOnlyList<WorkflowValidationError> Warnings(WorkflowGraph graph, IReadOnlyCollection<INodeType> nodeTypes);
}

/// <summary>
/// One problem of a graph, or (from <see cref="IWorkflowValidator.Warnings"/>) one piece of advice on it.
/// </summary>
/// <param name="NodeId">The node it concerns, or null for the graph as a whole.</param>
/// <param name="Message">What is wrong, in English, for a person or an agent fixing the graph.</param>
public sealed record WorkflowValidationError(string? NodeId, string Message);
