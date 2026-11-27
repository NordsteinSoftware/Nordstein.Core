using System.Text.Json;

namespace Nordstein.Core.Workflows;

/// <summary>
/// The branch names the kernel reserves: no node type declares them, every graph may use them where the kernel says.
/// </summary>
/// <remarks>
/// <see cref="Error"/> leaves every non-trigger node: a node whose step fails with
/// <see cref="WorkflowFailureKind.NodeFailed"/> and that has an edge naming it completes on that branch with the output
/// <see cref="ErrorOutputSchema"/> describes, <c>{ message, nodeType }</c>; without such an edge the run fails as it
/// always did. A budget, timeout or cancellation failure never takes it. A reference to the node from a node reached
/// only through its error edge is typed by <see cref="ErrorOutputSchema"/>.
/// </remarks>
public static class ReservedBranches
{
    /// <summary>
    /// The error branch, <c>error</c>.
    /// </summary>
    public const string Error = "error";

    /// <summary>
    /// The output of a node that completed on <see cref="Error"/>: <c>message</c> (the failure's text) and
    /// <c>nodeType</c> (the failed node's type).
    /// </summary>
    public static JsonElement ErrorOutputSchema { get; } = Parse(
        """{"type":"object","required":["message","nodeType"],"properties":{"message":{"type":"string"},"nodeType":{"type":"string"}}}""");

    private static JsonElement Parse(string json)
    {
        using JsonDocument document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }
}
