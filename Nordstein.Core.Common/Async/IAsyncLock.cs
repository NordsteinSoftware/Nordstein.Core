namespace Nordstein.Core.Common.Async;

/// <summary>
/// A keyed mutual-exclusion lock that supports both synchronous and asynchronous acquisition.
/// </summary>
/// <remarks>
/// Each distinct key value controls its own critical section; callers that share a key serialize
/// against one another, while callers with different keys run concurrently.
/// Implementations must be thread-safe.
/// </remarks>
public interface IAsyncLock
{
    /// <summary>
    /// Synchronously acquires the lock for the given key and returns a handle that releases it on dispose.
    /// </summary>
    /// <param name="key">
    /// The key that identifies the critical section. Two callers sharing the same key will serialize;
    /// different keys do not block each other.
    /// </param>
    /// <returns>
    /// An <see cref="IDisposable"/> whose <c>Dispose</c> releases the lock.
    /// The caller must dispose the handle when the critical section is complete.
    /// </returns>
    /// <remarks>
    /// This overload blocks the calling thread. Do not use <c>await</c> inside the critical section;
    /// use <see cref="LockAsync"/> instead when the critical section contains asynchronous work.
    /// </remarks>
    IDisposable Lock(object key);

    /// <summary>
    /// Asynchronously acquires the lock for the given key and returns a handle that releases it on dispose.
    /// </summary>
    /// <param name="key">
    /// The key that identifies the critical section. Two callers sharing the same key will serialize;
    /// different keys do not block each other.
    /// </param>
    /// <param name="cancellationToken">
    /// Token to cancel the wait for lock acquisition. Cancellation is only honored while waiting to
    /// acquire the lock; once the lock is held, the critical section runs to completion regardless of
    /// cancellation state.
    /// </param>
    /// <returns>
    /// A task that completes with an <see cref="IDisposable"/> whose <c>Dispose</c> releases the lock.
    /// The caller must dispose the handle when the critical section is complete.
    /// </returns>
    /// <remarks>
    /// Safe to hold across <c>await</c> points inside the critical section, unlike <see cref="Lock"/>.
    /// </remarks>
    Task<IDisposable> LockAsync(object key, CancellationToken cancellationToken = default);
}
