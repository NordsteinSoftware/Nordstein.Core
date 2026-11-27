using System.Text.Json;

namespace Nordstein.Core.Workflows;

/// <summary>
/// A node type whose node runs the nodes behind its <see cref="LoopBranches.Body"/> edges once per item of an array,
/// then completes on <see cref="LoopBranches.Done"/> with what it collected. The kernel runs the loop: it never calls
/// <see cref="INodeType.ExecuteAsync"/> on such a node, and a node trial reports an empty collection.
/// </summary>
/// <remarks>
/// <para>
/// The body: the nodes reachable through the node's <c>body</c> edges. The validator refuses an edge that leaves the body,
/// another iterating node or an <see cref="ISuspendingNodeType"/> inside it, and a body node that reads the loop's own
/// output; inside the body <c>{{item.&lt;path&gt;}}</c> (the current item, typed by the array's <c>items</c> schema) and
/// <c>{{index}}</c> (its position from 0) are valid references, outside they are errors. <see cref="INodeType.Branches"/>
/// declares exactly <c>body</c> and <c>done</c>.
/// </para>
/// <para>
/// A run: the node starts with its resolved input, the array under <see cref="ItemsProperty"/> is read (not an array, or
/// more than <see cref="MaxItems"/> items: the step fails), the body runs per item in order, sequentially, every body step
/// journaled with its iteration and counted against the step budget, and the node completes on <c>done</c> with
/// <see cref="Collect"/>. A body step that fails without an error edge fails the loop's step with its reason (the loop's own
/// error edge then applies). A <see cref="MaxItems"/> or <see cref="Collect"/> that throws fails the loop's step the way a
/// throwing <see cref="INodeType.ExecuteAsync"/> fails its own, with the exception's type name and message as the reason. A
/// pinned loop node completes with its pin instead.
/// </para>
/// </remarks>
public interface IIteratingNodeType : INodeType
{
    /// <summary>
    /// The configuration property holding the array to iterate, as a rule a whole-string data reference such as
    /// <c>{{steps.read.output.lineItems}}</c>.
    /// </summary>
    string ItemsProperty { get; }

    /// <summary>
    /// The most items a node with <paramref name="config"/> iterates; an array with more fails the step.
    /// </summary>
    /// <param name="config">The node's configuration, references unresolved.</param>
    int MaxItems(JsonElement config);

    /// <summary>
    /// The node's output after the loop: what the body's last executed node produced per item, in order.
    /// </summary>
    /// <param name="context">The node, its resolved input and the run's trigger input.</param>
    /// <param name="results">One output per item; <c>null</c> JSON for an iteration whose body reached no node.</param>
    JsonElement Collect(NodeContext context, IReadOnlyList<JsonElement> results);
}

/// <summary>
/// The branch names an <see cref="IIteratingNodeType"/> declares.
/// </summary>
public static class LoopBranches
{
    /// <summary>
    /// The edges into the body, <c>body</c>: taken once per item.
    /// </summary>
    public const string Body = "body";

    /// <summary>
    /// The edges out of the loop, <c>done</c>: taken once the loop completed.
    /// </summary>
    public const string Done = "done";
}

/// <summary>
/// The loop variables a node trial (<see cref="INodeTrial"/>) resolves <c>{{item.&lt;path&gt;}}</c> and <c>{{index}}</c> with when
/// it tries a body node on its own.
/// </summary>
/// <param name="Item">The item the try stands in.</param>
/// <param name="Index">Its position, from 0.</param>
public sealed record IterationStub(JsonElement Item, int Index);
