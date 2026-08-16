namespace Nordstein.Core.Common.Async;

/// <summary>
/// Extension methods for <see cref="Task"/> and <see cref="ValueTask"/> that fill gaps
/// in the BCL's async programming surface.
/// </summary>
public static class TaskExtensions
{
    /// <summary>
    /// Wraps a value in an already-completed <see cref="Task{T}"/>.
    /// </summary>
    /// <typeparam name="T">The type of the value.</typeparam>
    /// <param name="value">The value to wrap.</param>
    /// <returns>A successfully completed task whose result is <paramref name="value"/>.</returns>
    public static Task<T> ToTaskResult<T>(this T value)
        => Task.FromResult(value);

    /// <summary>
    /// Awaits all tasks in the sequence concurrently and returns their results as a read-only collection.
    /// </summary>
    /// <typeparam name="T">The result type of each task.</typeparam>
    /// <param name="tasks">The tasks to run concurrently. The sequence is enumerated eagerly.</param>
    /// <returns>
    /// A task that completes when all input tasks have completed, with a collection of results in
    /// the same order as the input sequence. If any task faults, the returned task also faults with
    /// an <see cref="AggregateException"/> containing all faulted tasks' exceptions.
    /// </returns>
    public static async Task<IReadOnlyCollection<T>> Await<T>(this IEnumerable<Task<T>> tasks)
        => await Task.WhenAll(tasks);

    /// <summary>
    /// Synchronously blocks the calling thread until the task completes and returns its result.
    /// </summary>
    /// <typeparam name="TResult">The result type of the task.</typeparam>
    /// <param name="task">The task to wait on.</param>
    /// <returns>The result of the completed task.</returns>
    /// <remarks>
    /// <b>Deadlock risk:</b> calling this on a thread that owns a synchronization context (e.g.
    /// ASP.NET classic, UI threads) can deadlock if the task's continuations are scheduled back on
    /// that context. Use only where no async call path exists — test infrastructure, top-level entry
    /// points, or extension bridges declared <c>synchronous</c> by design.
    /// </remarks>
    public static TResult SynchronouslyAwait<TResult>(this Task<TResult> task)
        => task.ConfigureAwait(false).GetAwaiter().GetResult();

    /// <summary>
    /// Synchronously blocks the calling thread until the <see cref="ValueTask{TResult}"/> completes
    /// and returns its result.
    /// </summary>
    /// <typeparam name="TResult">The result type of the value task.</typeparam>
    /// <param name="task">The value task to wait on.</param>
    /// <returns>The result of the completed value task.</returns>
    /// <remarks>
    /// <b>Deadlock risk:</b> same caveat as <see cref="SynchronouslyAwait{TResult}(Task{TResult})"/>.
    /// Additionally, a <see cref="ValueTask{TResult}"/> may only be awaited once; do not retain
    /// the value task after calling this method.
    /// </remarks>
    public static TResult SynchronouslyAwait<TResult>(this ValueTask<TResult> task)
        => task.ConfigureAwait(false).GetAwaiter().GetResult();
}
