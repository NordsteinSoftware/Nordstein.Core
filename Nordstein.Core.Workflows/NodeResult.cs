using System.Text.Json;

namespace Nordstein.Core.Workflows;

/// <summary>
/// How a node's execution ended: <see cref="Completed"/>, <see cref="Suspended"/> or
/// <see cref="Failed"/>. The three nested records are the only kinds.
/// </summary>
public abstract record NodeResult
{
    private NodeResult()
    {
    }

    /// <summary>
    /// The node finished with an output that downstream data references read.
    /// </summary>
    public sealed record Completed : NodeResult
    {
        /// <summary>
        /// Creates a completed result.
        /// </summary>
        /// <param name="output">The node's output; it should match the node type's output schema.</param>
        /// <param name="branch">The branch taken, one of <see cref="INodeType.Branches"/>; null for a node that does not branch.</param>
        /// <exception cref="ArgumentException"><paramref name="output"/> is an undefined (default) element.</exception>
        public Completed(JsonElement output, string? branch = null)
        {
            Output = Defined(output, nameof(output));
            Branch = branch;
        }

        /// <summary>
        /// The node's output.
        /// </summary>
        public JsonElement Output { get; }

        /// <summary>
        /// The branch taken, or null.
        /// </summary>
        public string? Branch { get; }
    }

    /// <summary>
    /// The node waits for an answer from outside; the run stops until
    /// <see cref="IWorkflowInterpreter.ResumeAsync"/>.
    /// </summary>
    public sealed record Suspended : NodeResult
    {
        /// <summary>
        /// Creates a suspended result.
        /// </summary>
        /// <param name="bookmark">What the node needs back on resume, such as the question it asked.</param>
        /// <exception cref="ArgumentException"><paramref name="bookmark"/> is an undefined (default) element.</exception>
        public Suspended(JsonElement bookmark)
        {
            Bookmark = Defined(bookmark, nameof(bookmark));
        }

        /// <summary>
        /// What the node needs back on resume.
        /// </summary>
        public JsonElement Bookmark { get; }
    }

    /// <summary>
    /// The node failed; the run fails with it.
    /// </summary>
    public sealed record Failed : NodeResult
    {
        /// <summary>
        /// Creates a failed result.
        /// </summary>
        /// <param name="reason">Why, for the step's trace.</param>
        /// <exception cref="ArgumentException"><paramref name="reason"/> is null or white space.</exception>
        public Failed(string reason)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(reason);
            Reason = reason;
        }

        /// <summary>
        /// Why the node failed.
        /// </summary>
        public string Reason { get; }
    }

    private static JsonElement Defined(JsonElement value, string name) =>
        value.ValueKind == JsonValueKind.Undefined
            ? throw new ArgumentException("The value is a defined JSON value.", name)
            : value;
}
