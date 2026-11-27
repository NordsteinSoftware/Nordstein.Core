using System.Text.Json;

namespace Nordstein.Core.Workflows;

/// <summary>
/// Reads a <see cref="WorkflowGraph"/> from the kernel's graph format, the JSON a product stores or receives for a
/// workflow. The reader checks the shape only; <see cref="IWorkflowValidator"/> checks what the graph means.
/// </summary>
/// <remarks>
/// <para>
/// The format: <c>{ "nodes": [ { "id", "type", "config"?, "retry"?: { "attempts", "backoff"? } } ], "edges"?: [ { "from",
/// "to", "branch"? } ] }</c>. <c>id</c>, <c>type</c>, <c>from</c>, <c>to</c> and <c>branch</c> are strings; <c>config</c> is any
/// JSON value, <c>{}</c> when absent; <c>attempts</c> is a whole number from 2 to <see cref="int.MaxValue"/> written without
/// a fraction or exponent; <c>backoff</c> is an ISO 8601 duration of days, hours, minutes and seconds
/// (<c>PnDTnHnMnS</c>, such as <c>PT5S</c> or <c>P1DT12H</c>; seconds with up to seven decimals), zero when absent.
/// An absent <c>edges</c> is no edges. A key is matched ordinally, so it is case-sensitive.
/// </para>
/// <para>
/// A key the format does not define is ignored at every level (the graph, a node, an edge, a retry): it is the product's
/// to read, such as a canvas layout or a node's test pin. A key the format defines but whose value is <c>null</c> is not
/// absent: it is the wrong type. The same property name twice in the graph object, a node, an edge, a retry, or any
/// object inside a <c>config</c> is an error; inside a key the reader ignores, it is the product's to judge. A string the
/// reader reads, and every name and string inside a <c>config</c>, must decode to valid text (an escaped unpaired
/// surrogate does not). The graph nests at most 64 levels deep, the graph object counting as the first.
/// </para>
/// <para>
/// The reader never consults a catalogue: a <c>type</c> of any content is read, and the validator refuses one the
/// catalogue lacks or that is not of the form <c>name@major</c>. An empty <c>nodes</c> array is read; the validator refuses
/// the graph. The reader sets no limit on the text's length or on the number of nodes and edges: bound the input before
/// reading it. Stateless and thread-safe.
/// </para>
/// </remarks>
public interface IWorkflowGraphReader
{
    /// <summary>
    /// Reads the graph from JSON text: exactly one JSON document (RFC 8259: no comments, no trailing commas, no content
    /// after the document) nesting at most 64 levels deep.
    /// </summary>
    /// <param name="json">The text.</param>
    /// <returns>The graph; its configurations own their memory.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="json"/> is null.</exception>
    /// <exception cref="WorkflowGraphFormatException">
    /// The text is not one JSON document (one error, the parser's position and message, the parser's exception as the
    /// inner exception) or not of the format (every error found, in document order).
    /// </exception>
    WorkflowGraph Read(string json);

    /// <summary>
    /// Reads the graph from a JSON element the caller parsed, for a product that reads its own keys from the same
    /// document. The element is not kept: the configurations are copied.
    /// </summary>
    /// <param name="graph">The graph object.</param>
    /// <returns>The graph; its configurations own their memory.</returns>
    /// <exception cref="ArgumentException"><paramref name="graph"/> is an undefined (default) element.</exception>
    /// <exception cref="WorkflowGraphFormatException">The element is not of the format; every error found, in document order.</exception>
    WorkflowGraph Read(JsonElement graph);
}

/// <summary>
/// Thrown when a text or element is not of the graph format <see cref="IWorkflowGraphReader"/> reads. Carries every error
/// found, each with the JSON path of the offending element.
/// </summary>
public sealed class WorkflowGraphFormatException : FormatException
{
    /// <summary>
    /// Initializes the exception; its message is <c>The text is not a workflow graph: </c> followed by every error's message.
    /// </summary>
    /// <param name="errors">The errors, at least one; copied.</param>
    /// <param name="innerException">The JSON parser's exception when the text is not JSON; otherwise null.</param>
    /// <exception cref="ArgumentNullException"><paramref name="errors"/> is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="errors"/> is empty or holds a null item.</exception>
    public WorkflowGraphFormatException(IReadOnlyList<WorkflowGraphFormatError> errors, Exception? innerException = null)
        : base(Describe(errors), innerException)
    {
        Errors = [.. errors];
    }

    /// <summary>
    /// The errors, in document order.
    /// </summary>
    public IReadOnlyList<WorkflowGraphFormatError> Errors { get; }

    private static string Describe(IReadOnlyList<WorkflowGraphFormatError> errors)
    {
        ArgumentNullException.ThrowIfNull(errors);
        if (errors.Count == 0)
        {
            throw new ArgumentException("A format exception carries at least one error.", nameof(errors));
        }

        if (errors.Any(error => error is null))
        {
            throw new ArgumentException("A format exception carries no null error.", nameof(errors));
        }

        return "The text is not a workflow graph: " + string.Join(" ", errors.Select(error => error.Message));
    }
}

/// <summary>
/// One error of a graph's format.
/// </summary>
public sealed record WorkflowGraphFormatError
{
    /// <summary>
    /// Creates an error.
    /// </summary>
    /// <param name="path">The JSON path of the offending element, from the root <c>$</c>: <c>$.nodes[2].type</c>, <c>$.nodes[0].config['a b']</c>.</param>
    /// <param name="message">What is wrong, in English, naming the place, for a person or an agent fixing the graph.</param>
    /// <exception cref="ArgumentNullException"><paramref name="path"/> or <paramref name="message"/> is null.</exception>
    public WorkflowGraphFormatError(string path, string message)
    {
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(message);
        Path = path;
        Message = message;
    }

    /// <summary>
    /// The JSON path of the offending element, from the root <c>$</c>.
    /// </summary>
    public string Path { get; }

    /// <summary>
    /// What is wrong.
    /// </summary>
    public string Message { get; }
}
