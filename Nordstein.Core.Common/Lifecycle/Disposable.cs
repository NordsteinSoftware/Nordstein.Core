namespace Nordstein.Core.Common.Lifecycle;

/// <summary>
/// Adapts an <see cref="Action"/> or <see cref="Func{ValueTask}"/> to
/// <see cref="IDisposable"/> or <see cref="IAsyncDisposable"/> respectively.
/// </summary>
/// <remarks>
/// Prefer the static <see cref="Create(Action)"/> and <see cref="Create(Func{ValueTask})"/> factory
/// methods over the constructors to avoid naming ambiguity with <see cref="System.IDisposable"/>
/// at the call site. Both <see cref="Dispose"/> and <see cref="DisposeAsync"/> are idempotent.
/// </remarks>
public class Disposable : IDisposable, IAsyncDisposable
{
    private readonly Func<ValueTask>? asyncAction;
    private readonly Action? action;
    private bool isDisposed;

    /// <summary>
    /// Creates a synchronous disposable that invokes <paramref name="action"/> on <see cref="Dispose"/>.
    /// </summary>
    /// <param name="action">The cleanup delegate to invoke once on disposal. Must not be <c>null</c>.</param>
    public Disposable(Action action)
    {
        this.action = action;
    }

    /// <summary>
    /// Creates an asynchronous disposable that invokes <paramref name="asyncAction"/> on
    /// <see cref="DisposeAsync"/>.
    /// </summary>
    /// <param name="asyncAction">
    /// The async cleanup delegate to invoke once on disposal. Must not be <c>null</c>.
    /// </param>
    /// <remarks>
    /// When the synchronous <see cref="Dispose"/> is called on an instance created with this
    /// constructor, the <see cref="ValueTask"/> returned by <paramref name="asyncAction"/> is started
    /// but not awaited. Prefer <see cref="DisposeAsync"/> to ensure the cleanup completes.
    /// </remarks>
    public Disposable(Func<ValueTask> asyncAction)
    {
        this.asyncAction = asyncAction;
    }

    /// <summary>
    /// Preferred factory for async cleanup: creates an <see cref="IAsyncDisposable"/> that invokes
    /// <paramref name="asyncAction"/> on <see cref="DisposeAsync"/>.
    /// </summary>
    /// <param name="asyncAction">The async cleanup delegate. Must not be <c>null</c>.</param>
    /// <returns>An <see cref="IAsyncDisposable"/> that invokes the delegate exactly once.</returns>
    public static IAsyncDisposable Create(Func<ValueTask> asyncAction)
        => new Disposable(asyncAction);

    /// <summary>
    /// Preferred factory for synchronous cleanup: creates an <see cref="IDisposable"/> that invokes
    /// <paramref name="action"/> on <see cref="Dispose"/>.
    /// </summary>
    /// <param name="action">The cleanup delegate. Must not be <c>null</c>.</param>
    /// <returns>An <see cref="IDisposable"/> that invokes the delegate exactly once.</returns>
    public static IDisposable Create(Action action)
        => new Disposable(action);

    /// <summary>
    /// Invokes the cleanup action and marks this instance as disposed.
    /// </summary>
    /// <remarks>
    /// Idempotent: subsequent calls after the first are no-ops.
    /// If this instance was constructed with an async cleanup delegate, the returned
    /// <see cref="ValueTask"/> is started but not awaited — prefer <see cref="DisposeAsync"/>
    /// in that case to guarantee the cleanup completes.
    /// </remarks>
    public void Dispose()
    {
        if(isDisposed)
        {
            return;
        }

        action?.Invoke();
        _ = asyncAction?.Invoke();
        isDisposed = true;

    }

    /// <summary>
    /// Asynchronously invokes the cleanup delegate and marks this instance as disposed.
    /// </summary>
    /// <returns>A <see cref="ValueTask"/> that completes when cleanup has finished.</returns>
    /// <remarks>
    /// Idempotent: subsequent calls after the first are no-ops.
    /// Any exception thrown by the async cleanup delegate is swallowed; exceptions from a
    /// synchronous delegate (if present) are propagated normally.
    /// </remarks>
    public async ValueTask DisposeAsync()
    {
        if(isDisposed)
        {
            return;
        }

        try
        {
            if (asyncAction != null)
            {
                await asyncAction.Invoke();
            }
        }
        catch
        {
            // ignored
        }

        action?.Invoke();
        isDisposed = true;
    }
}
