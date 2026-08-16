namespace Nordstein.Core.Domain;

/// <summary>
/// Executes operations in a logical transaction.
/// </summary>
public interface ITransaction
{
    /// <summary>
    /// <see langword="true"/> when a logical transaction is already in progress in the current
    /// async flow.
    /// </summary>
    /// <remarks>
    /// Repositories check this property to decide whether to share the ambient transaction context
    /// or open a new one.
    /// </remarks>
    bool IsActive { get; }

    /// <summary>
    /// Executes <paramref name="operation"/> inside a logical transaction and returns its result.
    /// </summary>
    /// <remarks>
    /// Nested calls reuse the outer transaction. On success the transaction is committed; on any
    /// exception the transaction is rolled back and post-commit notifications are discarded.
    /// The <paramref name="cancellationToken"/> is honored during the operation but not during
    /// commit or rollback.
    /// </remarks>
    /// <typeparam name="TResult">The type of value produced by the operation.</typeparam>
    /// <param name="operation">The async work to perform inside the transaction.</param>
    /// <param name="cancellationToken">Token to observe for cancellation during the operation.</param>
    /// <returns>The value returned by <paramref name="operation"/>.</returns>
    Task<TResult> InvokeAsync<TResult>(
        Func<Task<TResult>> operation,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Executes <paramref name="operation"/> inside a logical transaction without returning a value.
    /// </summary>
    /// <remarks>
    /// Void-returning overload of <see cref="InvokeAsync{TResult}"/>. See that overload for full
    /// transaction semantics.
    /// </remarks>
    /// <param name="operation">The async work to perform inside the transaction.</param>
    /// <param name="cancellationToken">Token to observe for cancellation during the operation.</param>
    Task InvokeAsync(Func<Task> operation, CancellationToken cancellationToken = default);
}
