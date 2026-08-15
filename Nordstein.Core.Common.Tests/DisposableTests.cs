using AwesomeAssertions;
using Nordstein.Core.Common.Lifecycle;

namespace Nordstein.Core.Common.Tests;

[TestClass]
public sealed class DisposableTests
{
    [TestMethod]
    public void Dispose_WithAction_InvokesActionOnce()
    {
        var count = 0;
        var disposable = new Disposable(() => count++);

        disposable.Dispose();

        count.Should().Be(1);
    }

    [TestMethod]
    public void Dispose_CalledTwice_InvokesActionOnlyOnce()
    {
        // The isDisposed guard makes a second dispose a no-op.
        var count = 0;
        var disposable = new Disposable(() => count++);

        disposable.Dispose();
        disposable.Dispose();

        count.Should().Be(1);
    }

    [TestMethod]
    public void CreateAction_ReturnsDisposable_ThatInvokesActionOnDispose()
    {
        var invoked = false;
        IDisposable disposable = Disposable.Create(() => invoked = true);

        disposable.Dispose();

        invoked.Should().BeTrue();
    }

    [TestMethod]
    public void Dispose_WithSynchronousAsyncAction_InvokesActionBody()
    {
        // Synchronous Dispose on an async-action Disposable fires the action but does not await it;
        // a synchronously-completing body still runs before Invoke() returns.
        var count = 0;
        var disposable = new Disposable(() =>
        {
            count++;
            return ValueTask.CompletedTask;
        });

        disposable.Dispose();

        count.Should().Be(1);
    }

    [TestMethod]
    public async Task CreateAsyncAction_ReturnsAsyncDisposable_ThatInvokesActionOnDisposeAsync()
    {
        var invoked = false;
        IAsyncDisposable disposable = Disposable.Create(() =>
        {
            invoked = true;
            return ValueTask.CompletedTask;
        });

        await disposable.DisposeAsync();

        invoked.Should().BeTrue();
    }

    [TestMethod]
    public async Task DisposeAsync_WithSyncAction_InvokesAction()
    {
        var count = 0;
        var disposable = new Disposable(() => count++);

        await disposable.DisposeAsync();

        count.Should().Be(1);
    }

    [TestMethod]
    public async Task DisposeAsync_CalledTwice_InvokesActionOnlyOnce()
    {
        var count = 0;
        var disposable = new Disposable(() => count++);

        await disposable.DisposeAsync();
        await disposable.DisposeAsync();

        count.Should().Be(1);
    }

    [TestMethod]
    public async Task DisposeAsync_WhenAsyncActionThrows_DoesNotPropagate()
    {
        // DisposeAsync deliberately swallows failures from the async action.
        var disposable = new Disposable(() =>
            ValueTask.FromException(new InvalidOperationException("boom")));

        var act = async () => await disposable.DisposeAsync();

        await act.Should().NotThrowAsync();
    }

    [TestMethod]
    public async Task DisposeAsync_AfterAsyncActionThrows_IsStillMarkedDisposed()
    {
        // A throwing async action must still flip isDisposed so a second call is a no-op.
        var count = 0;
        var disposable = new Disposable(() =>
        {
            count++;
            return ValueTask.FromException(new InvalidOperationException("boom"));
        });

        await disposable.DisposeAsync();
        await disposable.DisposeAsync();

        count.Should().Be(1);
    }

    [TestMethod]
    public async Task Dispose_CalledConcurrently_DoesNotThrowAndInvokesAtLeastOnce()
    {
        var count = 0;
        var disposable = new Disposable(() => Interlocked.Increment(ref count));

        var act = async () => await Task.WhenAll(
            Enumerable.Range(0, 32).Select(_ => Task.Run(disposable.Dispose)));

        await act.Should().NotThrowAsync();
        count.Should().BeGreaterThanOrEqualTo(1);
    }
}
