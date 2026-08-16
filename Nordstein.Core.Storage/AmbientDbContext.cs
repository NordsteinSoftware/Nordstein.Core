using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Nordstein.Core.Storage;

/// <summary>
/// Holds the <see cref="DbContext"/> (and its EF transaction) that is active for the current
/// logical transaction. Lets nested repository calls share one context and therefore one
/// connection, so transactions never promote to a 2-phase (prepared) transaction.
/// </summary>
/// <remarks>
/// State is stored in an <see cref="AsyncLocal{T}"/> so it is scoped to the current async flow
/// rather than the DI scope. Singletons resolved from the root scope (e.g. hosted services) share a
/// single AmbientDbContext instance; backing the state with the DI scope would let concurrent
/// background workers clobber each other's context, causing "A second operation was started on this
/// context instance" and dispose races. Per-async-flow isolation keeps each logical transaction's
/// context private to the flow that opened it.
/// </remarks>
public sealed class AmbientDbContext
{
    private readonly AsyncLocal<State?> current = new();

    /// <summary>
    /// The EF <see cref="DbContext"/> active for the current async flow; <see langword="null"/> when no
    /// logical transaction is in progress.
    /// </summary>
    /// <remarks>
    /// Backed by an <see cref="AsyncLocal{T}"/>, so the value is private to each async flow. Reading
    /// this from outside an active <c>ITransaction.InvokeAsync</c> call returns <see langword="null"/>;
    /// use <see cref="RequireContext"/> when a non-null value is required.
    /// </remarks>
    public DbContext? Context => current.Value?.Context;

    /// <summary>
    /// The EF transaction active for the current async flow; <see langword="null"/> when no logical
    /// transaction is in progress.
    /// </summary>
    /// <remarks>
    /// Backed by an <see cref="AsyncLocal{T}"/>. Populated by <c>ITransaction.InvokeAsync</c>
    /// alongside <see cref="Context"/>; both are set and cleared as a unit.
    /// </remarks>
    public IDbContextTransaction? Transaction => current.Value?.Transaction;

    /// <summary>
    /// <see langword="true"/> when a logical transaction has been opened in the current async flow;
    /// <see langword="false"/> otherwise.
    /// </summary>
    /// <remarks>
    /// Use this to distinguish a top-level call (no ambient transaction) from a nested one. Nested
    /// calls share the same context and connection rather than opening a second transaction.
    /// </remarks>
    public bool IsActive => current.Value is not null;

    /// <summary>
    /// Activates the ambient state for the current async flow by associating the supplied
    /// <paramref name="context"/> and <paramref name="transaction"/>.
    /// </summary>
    /// <param name="context">The <see cref="DbContext"/> opened for this logical transaction.</param>
    /// <param name="transaction">The EF transaction wrapping the operation.</param>
    /// <remarks>
    /// Called by <c>ITransaction.InvokeAsync</c> when opening a new outermost transaction.
    /// Application code must not call this directly; doing so would corrupt the ambient state for
    /// the current flow.
    /// </remarks>
    public void Set(DbContext context, IDbContextTransaction transaction)
        => current.Value = new State(context, transaction);

    /// <summary>
    /// Clears the ambient state for the current async flow after a transaction commits or rolls back.
    /// </summary>
    /// <remarks>
    /// Called by <c>ITransaction.InvokeAsync</c> in its finally block.
    /// Application code must not call this directly; doing so would discard the active context
    /// mid-transaction and leave subsequent operations without a shared connection.
    /// </remarks>
    public void Clear()
        => current.Value = null;

    /// <summary>
    /// Registers an action to run only after the outermost transaction has committed (e.g. firing a
    /// domain change event). If no transaction is active the action runs immediately, since there is
    /// nothing to wait for. Deferring prevents notifying consumers about writes that a later step in
    /// the same logical unit might still roll back.
    /// </summary>
    public void RegisterPostCommit(Action action)
    {
        State? state = current.Value;
        if (state is null)
        {
            action();
            return;
        }

        state.PostCommit.Add(action);
    }

    /// <summary>
    /// Removes and returns the post-commit actions queued for the active flow. Called by the
    /// outermost transaction after a successful commit so they can be fired exactly once.
    /// </summary>
    public IReadOnlyList<Action> TakePostCommit()
    {
        State? state = current.Value;
        if (state is null || state.PostCommit.Count == 0)
        {
            return [];
        }

        Action[] actions = [.. state.PostCommit];
        state.PostCommit.Clear();
        return actions;
    }

    /// <summary>
    /// Returns the active context or throws when no logical transaction is in progress.
    /// </summary>
    public DbContext RequireContext()
        => Context ?? throw new InvalidOperationException(
            "No ambient transaction context is active. Repository writes must run inside ITransaction.InvokeAsync.");

    private sealed record State(DbContext Context, IDbContextTransaction Transaction)
    {
        public List<Action> PostCommit { get; } = [];
    }
}
