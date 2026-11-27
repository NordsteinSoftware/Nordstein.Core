using System.Text.Json;

namespace Nordstein.Core.Workflows;

/// <summary>
/// A node type of a product's closed catalogue: what a node of this type takes, what it returns and
/// what it does. The kernel knows no node type; the product hands its catalogue to
/// <see cref="IWorkflowValidator"/> and <see cref="IWorkflowInterpreter"/>.
/// </summary>
/// <remarks>
/// Schemas use the JSON Schema keywords <c>type</c> (a name or an array of names),
/// <c>properties</c>, <c>required</c>, <c>additionalProperties</c> (boolean), <c>items</c> and
/// <c>enum</c>; others are annotations the kernel ignores. Implementations are called concurrently
/// for different runs and must be thread-safe.
/// </remarks>
public interface INodeType
{
    /// <summary>
    /// The type's name: a letter followed by up to 63 letters, digits, <c>.</c>, <c>-</c> or <c>_</c>, without a
    /// major. A graph names the type as <c>name@major</c> (<see cref="NodeTypeReference"/>); a catalogue may hold
    /// several majors of one name, each (name, major) once.
    /// </summary>
    string Type { get; }

    /// <summary>
    /// The type's major version, 1 or more. A breaking change to the configuration, the output or the behaviour
    /// is a new major under the same name; an additive change stays in the major. A saved graph pins the major it
    /// names, and a superseded major keeps running as long as the catalogue holds it.
    /// </summary>
    int Major { get; }

    /// <summary>
    /// Whether this type starts a run. A valid graph has exactly one trigger node; it is the first
    /// node a run visits and its <see cref="NodeContext.TriggerInput"/> is the run's input.
    /// </summary>
    bool IsTrigger { get; }

    /// <summary>
    /// Whether a node of this type changes state outside the run (writes, notifies). The kernel
    /// passes <see cref="NodeContext.DryRun"/>; such a node must then change nothing and describe
    /// what it would do in its output.
    /// </summary>
    bool HasSideEffects { get; }

    /// <summary>
    /// Whether a node of this type only reads: no side effects, never suspends, and safe to run for any caller on
    /// demand. A graph whose trigger is read-only is a read-only graph, and the validator refuses every node in it
    /// whose type is not. Defaults to <see langword="false"/>, so a type that does not opt in is never allowed into
    /// a read-only graph; a catalogue holding a read-only type that has side effects or suspends is refused.
    /// </summary>
    bool ReadOnly => false;

    /// <summary>
    /// The branches a node of this type chooses between, such as <c>true</c> and <c>false</c>; empty
    /// for a node that does not branch. A branching node completes with one of them, and every
    /// edge leaving it names one.
    /// </summary>
    IReadOnlyList<string> Branches { get; }

    /// <summary>
    /// The schema of a node's configuration. A data reference in the configuration stands for a
    /// value of the type the schema expects at its place.
    /// </summary>
    JsonElement InputSchema { get; }

    /// <summary>
    /// The schema of the output of a node with <paramref name="config"/>: what data references to
    /// the node may name. The validator calls it only for a configuration that passed
    /// <see cref="InputSchema"/> and <see cref="ValidateConfig"/>.
    /// </summary>
    /// <param name="config">The node's configuration, references unresolved.</param>
    JsonElement GetOutputSchema(JsonElement config);

    /// <summary>
    /// Checks what <see cref="InputSchema"/> cannot express (a cron expression that parses, a
    /// regular expression that compiles), after the schema check passed.
    /// </summary>
    /// <param name="config">The node's configuration, references unresolved.</param>
    /// <returns>One message per problem; empty when the configuration is valid.</returns>
    IReadOnlyList<string> ValidateConfig(JsonElement config);

    /// <summary>
    /// Executes one node. Returns <see cref="NodeResult.Completed"/>,
    /// <see cref="NodeResult.Suspended"/> (only an <see cref="ISuspendingNodeType"/>) or
    /// <see cref="NodeResult.Failed"/>. An exception other than an
    /// <see cref="OperationCanceledException"/> for the run's token fails the node with its message.
    /// </summary>
    /// <param name="context">The node, its resolved input and the run's trigger input.</param>
    /// <param name="cancellationToken">The run's token.</param>
    Task<NodeResult> ExecuteAsync(NodeContext context, CancellationToken cancellationToken = default);
}

/// <summary>
/// A node type whose node can stop the run to wait for an answer from outside (a person, an
/// approval). The product stores the suspension and calls
/// <see cref="IWorkflowInterpreter.ResumeAsync"/> with the answer.
/// </summary>
public interface ISuspendingNodeType : INodeType
{
    /// <summary>
    /// Continues a node that suspended with <paramref name="bookmark"/>, now that
    /// <paramref name="answer"/> arrived. Returns a result like <see cref="INodeType.ExecuteAsync"/>;
    /// it may suspend again.
    /// </summary>
    /// <param name="context">The node, its resolved input and the run's trigger input, as at its first execution.</param>
    /// <param name="bookmark">What the node returned in its <see cref="NodeResult.Suspended"/>.</param>
    /// <param name="answer">The answer the product received.</param>
    /// <param name="cancellationToken">The run's token.</param>
    Task<NodeResult> ResumeAsync(NodeContext context, JsonElement bookmark, JsonElement answer, CancellationToken cancellationToken = default);
}

/// <summary>
/// What a node executes with.
/// </summary>
/// <param name="NodeId">The node's id in the graph.</param>
/// <param name="NodeType">The node's type as the graph names it, <c>name@major</c> (<see cref="NodeDefinition.Type"/>).</param>
/// <param name="Input">The node's configuration with every data reference resolved.</param>
/// <param name="TriggerInput">The run's trigger input, as the product started the run with it.</param>
/// <param name="DryRun">Whether the run is a dry run: a node with side effects changes nothing and describes what it would do.</param>
public sealed record NodeContext(string NodeId, string NodeType, JsonElement Input, JsonElement TriggerInput, bool DryRun);
