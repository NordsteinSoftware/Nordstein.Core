namespace Nordstein.Core.Workflows.Internal;

/// <summary>
/// The rules a run and a try share for one node's execution: an exception becomes <see cref="NodeResult.Failed"/>
/// (the token's own cancellation propagates), a branch the type does not declare and a suspension of a type that
/// cannot suspend fail the node.
/// </summary>
internal static class NodeInvoker
{
    /// <summary>
    /// Runs <paramref name="invoke"/>; a null result or an exception other than <paramref name="cancellationToken"/>'s
    /// cancellation becomes <see cref="NodeResult.Failed"/>.
    /// </summary>
    public static async Task<NodeResult> InvokeAsync(Func<Task<NodeResult>> invoke, CancellationToken cancellationToken)
    {
        try
        {
            return await invoke() ?? new NodeResult.Failed("The node returned no result.");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            return new NodeResult.Failed($"{exception.GetType().Name}: {exception.Message}");
        }
    }

    /// <summary>
    /// Why <paramref name="result"/> fails the node although the node did not fail itself: a branch the type does not
    /// declare, or a suspension of a type that cannot suspend. Null when the result stands.
    /// </summary>
    public static string? Problem(INodeType type, NodeResult result) => result switch
    {
        NodeResult.Completed completed => BranchProblem(type, completed.Branch),
        NodeResult.Suspended when type is not ISuspendingNodeType => $"A '{type.Type}' node cannot suspend.",
        _ => null,
    };

    private static string? BranchProblem(INodeType type, string? branch) =>
        string.Equals(branch, ReservedBranches.Error, StringComparison.Ordinal)
            ? $"A '{type.Type}' node chose the reserved branch '{ReservedBranches.Error}'; only the kernel takes it."
            : type.Branches.Count == 0
            ? branch is null ? null : $"A '{type.Type}' node does not branch, but it chose '{branch}'."
            : branch is not null && type.Branches.Contains(branch, StringComparer.Ordinal)
                ? null
                : $"A '{type.Type}' node must choose one of its branches {string.Join(", ", type.Branches)}, but it chose '{branch}'.";
}
