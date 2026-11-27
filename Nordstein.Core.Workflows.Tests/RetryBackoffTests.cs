using System.Text.Json;
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Nordstein.Core.Testing;

namespace Nordstein.Core.Workflows.Tests;

/// <summary>
/// A retry's backoff longer than one timer can wait (<see cref="Task.Delay(TimeSpan, CancellationToken)"/> refuses more
/// than about 49 days): it is still waited, never skipped.
/// </summary>
[TestClass]
public sealed class RetryBackoffTests : BaseTest<Module>
{
    [TestMethod]
    public async Task Run_BackoffLongerThanOneTimer_WaitsInsteadOfRetryingAtOnce()
    {
        using var cancellation = new CancellationTokenSource();
        int calls = 0;
        var failing = new TestNode("failing") { Execute = (_, _) => Task.FromResult<NodeResult>(new NodeResult.Failed($"try {++calls} failed")) };
        var journal = new CancellingJournal(cancellation);
        WorkflowGraph graph = new(
            [new NodeDefinition("t", "trigger@1", Json("{}")), new NodeDefinition("a", "failing@1", Json("{}")) { Retry = new RetryPolicy(2, TimeSpan.FromDays(60)) }],
            [new EdgeDefinition("t", "a")]);
        INodeType[] catalog = [new TestNode("trigger") { IsTrigger = true }, failing];

        Func<Task> act = () => GetServices().GetRequiredService<IWorkflowInterpreter>()
            .RunAsync(new WorkflowExecution(graph, catalog, journal, 10, false), Json("{}"), cancellation.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        calls.Should().Be(1, "the second try waits for the backoff, which the cancellation ends");
        journal.Entries.Should().Equal("started t", "completed t", "started a", "retrying a 1", "failed a Cancelled");
    }

    private static JsonElement Json(string text)
    {
        using JsonDocument document = JsonDocument.Parse(text);
        return document.RootElement.Clone();
    }

    /// <summary>
    /// Records entries and cancels the run a moment after the first retry is journaled, while the backoff waits.
    /// </summary>
    private sealed class CancellingJournal : IStepJournal
    {
        private readonly CancellationTokenSource cancellation;

        public CancellingJournal(CancellationTokenSource cancellation)
        {
            this.cancellation = cancellation;
        }

        public List<string> Entries { get; } = [];

        public Task StepStartedAsync(string nodeId, string nodeType, JsonElement input, CancellationToken cancellationToken)
        {
            Entries.Add($"started {nodeId}");
            return Task.CompletedTask;
        }

        public Task StepCompletedAsync(string nodeId, JsonElement output, string? branch, CancellationToken cancellationToken)
        {
            Entries.Add($"completed {nodeId}");
            return Task.CompletedTask;
        }

        public Task StepRetryingAsync(string nodeId, int attempt, WorkflowFailure failure, CancellationToken cancellationToken)
        {
            Entries.Add($"retrying {nodeId} {attempt}");
            cancellation.CancelAfter(TimeSpan.FromMilliseconds(50));
            return Task.CompletedTask;
        }

        public Task StepSuspendedAsync(string nodeId, JsonElement bookmark, CancellationToken cancellationToken)
        {
            Entries.Add($"suspended {nodeId}");
            return Task.CompletedTask;
        }

        public Task StepFailedAsync(string nodeId, WorkflowFailure failure, CancellationToken cancellationToken)
        {
            Entries.Add($"failed {nodeId} {failure.Kind}");
            return Task.CompletedTask;
        }
    }
}
