using AwesomeAssertions;
using Nordstein.Core.Common.Async;

namespace Nordstein.Core.Common.Tests;

[TestClass]
public sealed class TaskExtensionsTests
{
    [TestMethod]
    public async Task ToTaskResult_WrapsValueInCompletedTask()
    {
        Task<int> task = 42.ToTaskResult();

        task.IsCompletedSuccessfully.Should().BeTrue();
        (await task).Should().Be(42);
    }

    [TestMethod]
    public async Task ToTaskResult_WithReferenceType_WrapsValue()
    {
        Task<string> task = "hello".ToTaskResult();

        (await task).Should().Be("hello");
    }

    [TestMethod]
    public async Task Await_AwaitsAllTasks_PreservingOrder()
    {
        Task<int>[] tasks = [1.ToTaskResult(), 2.ToTaskResult(), 3.ToTaskResult()];

        IReadOnlyCollection<int> results = await tasks.Await();

        results.Should().Equal(1, 2, 3);
    }

    [TestMethod]
    public async Task Await_WithEmptySequence_ReturnsEmpty()
    {
        Task<int>[] tasks = [];

        IReadOnlyCollection<int> results = await tasks.Await();

        results.Should().BeEmpty();
    }

    [TestMethod]
    public void SynchronouslyAwait_Task_ReturnsResult()
    {
        int result = Task.FromResult(7).SynchronouslyAwait();

        result.Should().Be(7);
    }

    [TestMethod]
    public void SynchronouslyAwait_ValueTask_ReturnsResult()
    {
        int result = new ValueTask<int>(9).SynchronouslyAwait();

        result.Should().Be(9);
    }

    [TestMethod]
    public void SynchronouslyAwait_Task_PropagatesOriginalException()
    {
        // GetAwaiter().GetResult() unwraps the AggregateException, rethrowing the original.
        Task<int> task = Task.FromException<int>(new InvalidOperationException("boom"));

        var act = () => task.SynchronouslyAwait();

        act.Should().Throw<InvalidOperationException>().WithMessage("boom");
    }

    [TestMethod]
    public void SynchronouslyAwait_ValueTask_PropagatesOriginalException()
    {
        var task = new ValueTask<int>(Task.FromException<int>(new InvalidOperationException("boom")));

        var act = () => task.SynchronouslyAwait();

        act.Should().Throw<InvalidOperationException>().WithMessage("boom");
    }
}
