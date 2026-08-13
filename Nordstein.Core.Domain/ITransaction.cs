namespace Nordstein.Core.Domain;

/// <summary>
/// Executes operations in a logical transaction.
/// </summary>
public interface ITransaction
{
    bool IsActive { get; }

    Task<TResult> InvokeAsync<TResult>(
        Func<Task<TResult>> operation,
        CancellationToken cancellationToken = default);

    Task InvokeAsync(Func<Task> operation, CancellationToken cancellationToken = default);
}
