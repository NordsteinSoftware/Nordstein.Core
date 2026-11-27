using System.Text.Json;
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Nordstein.Core.Testing;

namespace Nordstein.Core.Workflows.Tests;

[TestClass]
public sealed class WorkflowInterpreterTests : BaseTest<Module>
{
    [TestMethod]
    public async Task Interpreter_Linear_VisitsEveryNodeInOrder()
    {
        var journal = new RecordingJournal();
        WorkflowGraph graph = Graph([Node("t", "trigger@1"), Node("a", "step@1"), Node("b", "step@1")], Edge("t", "a"), Edge("a", "b"));

        WorkflowOutcome outcome = await Run(graph, journal, Catalog());

        outcome.Should().Be(new WorkflowOutcome.Completed());
        journal.Entries.Should().Equal("started t", "completed t", "started a", "completed a", "started b", "completed b");
    }

    [TestMethod]
    public async Task Interpreter_TriggerNode_GetsTriggerInputAndDryRun()
    {
        var journal = new RecordingJournal();
        var trigger = new TestNode("trigger") { IsTrigger = true, Execute = (context, _) => Task.FromResult<NodeResult>(new NodeResult.Completed(context.TriggerInput)) };
        WorkflowGraph graph = Graph([Node("t", "trigger@1")]);

        await Run(graph, journal, [trigger], triggerInput: """{"documentId":"d1"}""", dryRun: true);

        trigger.Calls.Should().ContainSingle().Which.DryRun.Should().BeTrue();
        journal.Outputs["t"].GetProperty("documentId").GetString().Should().Be("d1");
    }

    [TestMethod]
    public async Task Interpreter_NodeFailedWithErrorEdge_CompletesOnErrorBranch()
    {
        var journal = new RecordingJournal();
        var failing = new TestNode("failing") { Execute = (_, _) => Task.FromResult<NodeResult>(new NodeResult.Failed("no stock")) };
        WorkflowGraph graph = Graph(
            [Node("t", "trigger@1"), Node("a", "failing@1"), Node("onError", "step@1", """{"why":"{{steps.a.output.message}}","type":"{{steps.a.output.nodeType}}"}"""), Node("next", "step@1")],
            Edge("t", "a"), Edge("a", "onError", ReservedBranches.Error), Edge("a", "next"));

        WorkflowOutcome outcome = await Run(graph, journal, Catalog(failing));

        outcome.Should().Be(new WorkflowOutcome.Completed());
        journal.Entries.Should().Equal("started t", "completed t", "started a", "recovered a", "started onError", "completed onError");
        journal.Failures["a"].Should().Be(new WorkflowFailure(WorkflowFailureKind.NodeFailed, "no stock"));
        journal.Inputs["onError"].GetProperty("why").GetString().Should().Be("no stock");
        journal.Inputs["onError"].GetProperty("type").GetString().Should().Be("failing@1");
    }

    [TestMethod]
    public async Task Interpreter_ExceptionWithErrorEdge_CompletesOnErrorBranchWithTheExceptionText()
    {
        var journal = new RecordingJournal();
        var throwing = new TestNode("throwing") { Execute = (_, _) => throw new InvalidOperationException("boom") };
        WorkflowGraph graph = Graph([Node("t", "trigger@1"), Node("a", "throwing@1"), Node("onError", "step@1")], Edge("t", "a"), Edge("a", "onError", ReservedBranches.Error));

        WorkflowOutcome outcome = await Run(graph, journal, Catalog(throwing));

        outcome.Should().Be(new WorkflowOutcome.Completed());
        journal.Outputs["a"].GetProperty("message").GetString().Should().Be("InvalidOperationException: boom");
    }

    [TestMethod]
    public async Task Interpreter_NodeFailedWithoutErrorEdge_FailsRun()
    {
        var journal = new RecordingJournal();
        var failing = new TestNode("failing") { Execute = (_, _) => Task.FromResult<NodeResult>(new NodeResult.Failed("no stock")) };
        WorkflowGraph graph = Graph([Node("t", "trigger@1"), Node("a", "failing@1"), Node("next", "step@1")], Edge("t", "a"), Edge("a", "next"));

        WorkflowOutcome outcome = await Run(graph, journal, Catalog(failing));

        outcome.Should().BeOfType<WorkflowOutcome.Failed>().Which.NodeId.Should().Be("a");
        journal.Entries.Should().Equal("started t", "completed t", "started a", "failed a NodeFailed");
    }

    [TestMethod]
    public async Task Interpreter_BudgetExhaustedWithErrorEdge_FailsRun()
    {
        var journal = new RecordingJournal();
        WorkflowGraph graph = Graph([Node("t", "trigger@1"), Node("a", "step@1"), Node("onError", "step@1")], Edge("t", "a"), Edge("a", "onError", ReservedBranches.Error));

        WorkflowOutcome outcome = await Run(graph, journal, Catalog(), maxSteps: 1);

        outcome.Should().BeOfType<WorkflowOutcome.Failed>().Which.Failure.Kind.Should().Be(WorkflowFailureKind.StepBudgetExhausted);
        journal.Entries.Should().Equal("started t", "completed t", "started a", "failed a StepBudgetExhausted");
    }

    [TestMethod]
    public async Task Interpreter_CancelledWithErrorEdge_Propagates()
    {
        var journal = new RecordingJournal();
        using var cancellation = new CancellationTokenSource();
        var cancelling = new TestNode("cancelling") { Execute = (_, token) => { cancellation.Cancel(); token.ThrowIfCancellationRequested(); return Task.FromResult<NodeResult>(new NodeResult.Completed(Json("{}"))); } };
        WorkflowGraph graph = Graph([Node("t", "trigger@1"), Node("a", "cancelling@1"), Node("onError", "step@1")], Edge("t", "a"), Edge("a", "onError", ReservedBranches.Error));

        Func<Task> act = () => Interpreter().RunAsync(Execution(graph, journal, Catalog(cancelling)), Json("{}"), cancellation.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        journal.Entries.Should().Equal("started t", "completed t", "started a", "failed a Cancelled");
    }

    [TestMethod]
    public async Task Interpreter_JournalWithoutStepRecovered_GetsStepCompletedOnTheErrorBranch()
    {
        var journal = new LegacyJournal();
        var failing = new TestNode("failing") { Execute = (_, _) => Task.FromResult<NodeResult>(new NodeResult.Failed("no stock")) };
        WorkflowGraph graph = Graph([Node("t", "trigger@1"), Node("a", "failing@1"), Node("onError", "step@1")], Edge("t", "a"), Edge("a", "onError", ReservedBranches.Error));

        await Interpreter().RunAsync(new WorkflowExecution(graph, Catalog(failing), journal, 50, false), Json("{}"), CancellationToken);

        journal.Entries.Should().Equal("started t", "completed t", "started a", "completed a error", "started onError", "completed onError");
    }

    [TestMethod]
    public async Task Run_PinnedErrorBranch_CompletesOnErrorBranch()
    {
        var journal = new RecordingJournal();
        WorkflowGraph graph = Graph([Node("t", "trigger@1"), Node("a", "step@1"), Node("onError", "step@1")], Edge("t", "a"), Edge("a", "onError", ReservedBranches.Error));
        var pins = new Dictionary<string, PinnedStep>(StringComparer.Ordinal) { ["a"] = new(Json("""{"message":"stubbed","nodeType":"step"}"""), ReservedBranches.Error) };

        WorkflowOutcome outcome = await Run(graph, journal, Catalog(), pins: pins);

        outcome.Should().Be(new WorkflowOutcome.Completed());
        journal.Entries.Should().Equal("started t", "completed t", "started a", "pinned a error", "started onError", "completed onError");
    }

    [TestMethod]
    public async Task Interpreter_RetrySucceedsOnThirdTry_JournalsThreeTries()
    {
        var journal = new RecordingJournal();
        int calls = 0;
        var flaky = new TestNode("flaky") { Execute = (context, _) => Task.FromResult<NodeResult>(++calls < 3 ? new NodeResult.Failed($"try {calls} failed") : new NodeResult.Completed(context.Input)) };
        WorkflowGraph graph = Graph([Node("t", "trigger@1"), Node("a", "flaky@1") with { Retry = new RetryPolicy(3, TimeSpan.Zero) }, Node("b", "step@1")], Edge("t", "a"), Edge("a", "b"));

        WorkflowOutcome outcome = await Run(graph, journal, Catalog(flaky));

        outcome.Should().Be(new WorkflowOutcome.Completed());
        flaky.Calls.Should().HaveCount(3);
        journal.Entries.Should().Equal("started t", "completed t", "started a", "retrying a 1 try 1 failed", "retrying a 2 try 2 failed", "completed a", "started b", "completed b");
    }

    [TestMethod]
    public async Task Interpreter_RetryExhausted_FailsAfterTheLastTry()
    {
        var journal = new RecordingJournal();
        var failing = new TestNode("failing") { Execute = (_, _) => Task.FromResult<NodeResult>(new NodeResult.Failed("down")) };
        WorkflowGraph graph = Graph([Node("t", "trigger@1"), Node("a", "failing@1") with { Retry = new RetryPolicy(2, TimeSpan.Zero) }], Edge("t", "a"));

        WorkflowOutcome outcome = await Run(graph, journal, Catalog(failing));

        outcome.Should().BeOfType<WorkflowOutcome.Failed>().Which.Failure.Message.Should().Be("down");
        failing.Calls.Should().HaveCount(2);
        journal.Entries.Should().Equal("started t", "completed t", "started a", "retrying a 1 down", "failed a NodeFailed");
    }

    [TestMethod]
    public async Task Interpreter_RetryExhaustedWithErrorEdge_RecoversAfterTheLastTry()
    {
        var journal = new RecordingJournal();
        var failing = new TestNode("failing") { Execute = (_, _) => Task.FromResult<NodeResult>(new NodeResult.Failed("down")) };
        WorkflowGraph graph = Graph(
            [Node("t", "trigger@1"), Node("a", "failing@1") with { Retry = new RetryPolicy(2, TimeSpan.Zero) }, Node("onError", "step@1")],
            Edge("t", "a"), Edge("a", "onError", ReservedBranches.Error));

        WorkflowOutcome outcome = await Run(graph, journal, Catalog(failing));

        outcome.Should().Be(new WorkflowOutcome.Completed());
        journal.Entries.Should().Equal("started t", "completed t", "started a", "retrying a 1 down", "recovered a", "started onError", "completed onError");
    }

    [TestMethod]
    public async Task Interpreter_RetryCancelled_DoesNotRetry()
    {
        var journal = new RecordingJournal();
        using var cancellation = new CancellationTokenSource();
        var cancelling = new TestNode("cancelling") { Execute = (_, token) => { cancellation.Cancel(); token.ThrowIfCancellationRequested(); return Task.FromResult<NodeResult>(new NodeResult.Completed(Json("{}"))); } };
        WorkflowGraph graph = Graph([Node("t", "trigger@1"), Node("a", "cancelling@1") with { Retry = new RetryPolicy(5, TimeSpan.Zero) }], Edge("t", "a"));

        Func<Task> act = () => Interpreter().RunAsync(Execution(graph, journal, Catalog(cancelling)), Json("{}"), cancellation.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        cancelling.Calls.Should().ContainSingle();
        journal.Entries.Should().Equal("started t", "completed t", "started a", "failed a Cancelled");
    }

    [TestMethod]
    public async Task Interpreter_RetryBackoffCancelled_JournalsCancelledAndPropagates()
    {
        var journal = new RecordingJournal();
        using var cancellation = new CancellationTokenSource();
        var failing = new TestNode("failing") { Execute = (_, _) => { cancellation.Cancel(); return Task.FromResult<NodeResult>(new NodeResult.Failed("down")); } };
        WorkflowGraph graph = Graph([Node("t", "trigger@1"), Node("a", "failing@1") with { Retry = new RetryPolicy(2, TimeSpan.FromMinutes(5)) }], Edge("t", "a"));

        Func<Task> act = () => Interpreter().RunAsync(Execution(graph, journal, Catalog(failing)), Json("{}"), cancellation.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        journal.Entries.Should().Equal("started t", "completed t", "started a", "retrying a 1 down", "failed a Cancelled");
    }

    [TestMethod]
    public async Task Interpreter_RetryOnPinnedNode_NeverExecutes()
    {
        var journal = new RecordingJournal();
        var failing = new TestNode("failing") { Execute = (_, _) => Task.FromResult<NodeResult>(new NodeResult.Failed("down")) };
        WorkflowGraph graph = Graph([Node("t", "trigger@1"), Node("a", "failing@1") with { Retry = new RetryPolicy(3, TimeSpan.Zero) }], Edge("t", "a"));
        var pins = new Dictionary<string, PinnedStep>(StringComparer.Ordinal) { ["a"] = new(Json("{}"), null) };

        WorkflowOutcome outcome = await Run(graph, journal, Catalog(failing), pins: pins);

        outcome.Should().Be(new WorkflowOutcome.Completed());
        failing.Calls.Should().BeEmpty();
    }

    [TestMethod]
    public async Task Interpreter_ForEach_RunsBodyPerItemInOrder()
    {
        var journal = new RecordingJournal();
        WorkflowGraph graph = LoopGraph(
            Node("b", "step@1", """{"name":"{{item.name}}","i":"{{index}}"}"""),
            Node("c", "step@1", """{"double":"{{steps.b.output.i}}"}"""));

        WorkflowOutcome outcome = await Run(graph, journal, Catalog(new TestLoopNode()), triggerInput: """{"tags":[{"name":"a"},{"name":"b"},{"name":"c"}]}""");

        outcome.Should().Be(new WorkflowOutcome.Completed());
        journal.Entries.Should().Equal(
            "started t", "completed t", "started each",
            "started b #0", "completed b", "started c #0", "completed c",
            "started b #1", "completed b", "started c #1", "completed c",
            "started b #2", "completed b", "started c #2", "completed c",
            "completed each done", "started after", "completed after");
        journal.InputsOf("b").Select(input => (input.GetProperty("name").GetString(), input.GetProperty("i").GetInt32())).Should().Equal(("a", 0), ("b", 1), ("c", 2));
        journal.Outputs["each"].GetProperty("count").GetInt32().Should().Be(3);
        journal.Outputs["each"].GetProperty("results").EnumerateArray().Select(result => result.GetProperty("double").GetInt32()).Should().Equal(0, 1, 2);
        journal.Inputs["after"].GetProperty("r").GetArrayLength().Should().Be(3);
    }

    [TestMethod]
    public async Task Interpreter_ForEach_EmptyArray_CompletesWithNoResults()
    {
        var journal = new RecordingJournal();
        WorkflowGraph graph = LoopGraph(Node("b", "step@1"));

        WorkflowOutcome outcome = await Run(graph, journal, Catalog(new TestLoopNode()), triggerInput: """{"tags":[]}""");

        outcome.Should().Be(new WorkflowOutcome.Completed());
        journal.Entries.Should().Equal("started t", "completed t", "started each", "completed each done", "started after", "completed after");
        journal.Outputs["each"].GetProperty("count").GetInt32().Should().Be(0);
    }

    [TestMethod]
    public async Task Interpreter_ForEach_OverMaxItems_FailsStep()
    {
        var journal = new RecordingJournal();
        WorkflowGraph graph = LoopGraph(Node("b", "step@1"), max: 2);

        WorkflowOutcome outcome = await Run(graph, journal, Catalog(new TestLoopNode()), triggerInput: """{"tags":[1,2,3]}""");

        outcome.Should().BeOfType<WorkflowOutcome.Failed>().Which.Should().Be(new WorkflowOutcome.Failed("each", new WorkflowFailure(WorkflowFailureKind.NodeFailed, "items holds 3 items; at most 2.")));
        journal.Entries.Should().Equal("started t", "completed t", "started each", "failed each NodeFailed");
    }

    [TestMethod]
    public async Task Interpreter_ForEach_ItemsNotAnArray_FailsStep()
    {
        var journal = new RecordingJournal();
        WorkflowGraph graph = LoopGraph(Node("b", "step@1"));

        WorkflowOutcome outcome = await Run(graph, journal, Catalog(new TestLoopNode()), triggerInput: """{"tags":"none"}""");

        outcome.Should().BeOfType<WorkflowOutcome.Failed>().Which.Failure.Message.Should().Be("items is not an array once resolved.");
    }

    [TestMethod]
    public async Task Interpreter_ForEach_MaxItemsThrows_FailsTheLoopStepLikeAThrowingNode()
    {
        var journal = new RecordingJournal();
        var loop = new TestLoopNode { MaxItemsFailure = new InvalidOperationException("no limit") };
        WorkflowGraph graph = LoopGraph(Node("b", "step@1"));

        WorkflowOutcome outcome = await Run(graph, journal, Catalog(loop), triggerInput: """{"tags":[1,2]}""");

        outcome.Should().Be(new WorkflowOutcome.Failed("each", new WorkflowFailure(WorkflowFailureKind.NodeFailed, "InvalidOperationException: no limit")));
        journal.Entries.Should().Equal("started t", "completed t", "started each", "failed each NodeFailed");
    }

    [TestMethod]
    public async Task Interpreter_ForEach_CollectThrows_FailsTheLoopStepAfterTheBodyRan()
    {
        var journal = new RecordingJournal();
        var loop = new TestLoopNode { CollectFailure = new InvalidOperationException("no sum") };
        WorkflowGraph graph = LoopGraph(Node("b", "step@1"));

        WorkflowOutcome outcome = await Run(graph, journal, Catalog(loop), triggerInput: """{"tags":[1]}""");

        outcome.Should().Be(new WorkflowOutcome.Failed("each", new WorkflowFailure(WorkflowFailureKind.NodeFailed, "InvalidOperationException: no sum")));
        journal.Entries.Should().Equal("started t", "completed t", "started each", "started b #0", "completed b", "failed each NodeFailed");
    }

    [TestMethod]
    public async Task Interpreter_ForEach_CollectThrowsWithLoopErrorEdge_RecoversOnTheLoop()
    {
        var journal = new RecordingJournal();
        var loop = new TestLoopNode { CollectFailure = new InvalidOperationException("no sum") };
        WorkflowGraph graph = Graph(
            [Node("t", "trigger@1"), Node("each", "each@1", """{"items":"{{trigger.tags}}"}"""), Node("b", "step@1"), Node("onError", "step@1")],
            Edge("t", "each"), Edge("each", "b", LoopBranches.Body), Edge("each", "onError", ReservedBranches.Error));

        WorkflowOutcome outcome = await Run(graph, journal, Catalog(loop), triggerInput: """{"tags":[1]}""");

        outcome.Should().Be(new WorkflowOutcome.Completed());
        journal.Entries.Should().Equal("started t", "completed t", "started each", "started b #0", "completed b", "recovered each", "started onError", "completed onError");
    }

    [TestMethod]
    public async Task Interpreter_ForEach_BodyStepFails_FailsTheLoopNamingIterationAndStep()
    {
        var journal = new RecordingJournal();
        int calls = 0;
        var flaky = new TestNode("flaky") { Execute = (context, _) => Task.FromResult<NodeResult>(++calls == 2 ? new NodeResult.Failed("no price") : new NodeResult.Completed(context.Input)) };
        WorkflowGraph graph = LoopGraph(Node("b", "flaky@1"));

        WorkflowOutcome outcome = await Run(graph, journal, Catalog(new TestLoopNode(), flaky), triggerInput: """{"tags":[1,2,3]}""");

        outcome.Should().BeOfType<WorkflowOutcome.Failed>().Which.Should().Be(new WorkflowOutcome.Failed("each", new WorkflowFailure(WorkflowFailureKind.NodeFailed, "iteration 1, step 'b': no price")));
        journal.Entries.Should().Equal("started t", "completed t", "started each", "started b #0", "completed b", "started b #1", "failed b NodeFailed", "failed each NodeFailed");
    }

    [TestMethod]
    public async Task Interpreter_ForEach_BodyStepFailsWithLoopErrorEdge_RecoversOnTheLoop()
    {
        var journal = new RecordingJournal();
        var failing = new TestNode("failing") { Execute = (_, _) => Task.FromResult<NodeResult>(new NodeResult.Failed("no price")) };
        WorkflowGraph graph = Graph(
            [Node("t", "trigger@1"), Node("each", "each@1", """{"items":"{{trigger.tags}}"}"""), Node("b", "failing@1"), Node("after", "step@1"), Node("onError", "step@1", """{"why":"{{steps.each.output.message}}"}""")],
            Edge("t", "each"), Edge("each", "b", LoopBranches.Body), Edge("each", "after", LoopBranches.Done), Edge("each", "onError", ReservedBranches.Error));

        WorkflowOutcome outcome = await Run(graph, journal, Catalog(new TestLoopNode(), failing), triggerInput: """{"tags":[1]}""");

        outcome.Should().Be(new WorkflowOutcome.Completed());
        journal.Entries.Should().Equal("started t", "completed t", "started each", "started b #0", "failed b NodeFailed", "recovered each", "started onError", "completed onError");
        journal.Inputs["onError"].GetProperty("why").GetString().Should().Be("iteration 0, step 'b': no price");
    }

    [TestMethod]
    public async Task Interpreter_ForEach_BodyStepWithErrorEdgeInsideTheBody_LoopGoesOn()
    {
        var journal = new RecordingJournal();
        var failing = new TestNode("failing") { Execute = (_, _) => Task.FromResult<NodeResult>(new NodeResult.Failed("no price")) };
        WorkflowGraph graph = Graph(
            [Node("t", "trigger@1"), Node("each", "each@1", """{"items":"{{trigger.tags}}"}"""), Node("b", "failing@1"), Node("fallback", "step@1", """{"m":"{{steps.b.output.message}}"}"""), Node("after", "step@1")],
            Edge("t", "each"), Edge("each", "b", LoopBranches.Body), Edge("b", "fallback", ReservedBranches.Error), Edge("each", "after", LoopBranches.Done));

        WorkflowOutcome outcome = await Run(graph, journal, Catalog(new TestLoopNode(), failing), triggerInput: """{"tags":[1,2]}""");

        outcome.Should().Be(new WorkflowOutcome.Completed());
        journal.Outputs["each"].GetProperty("results").EnumerateArray().Select(result => result.GetProperty("m").GetString()).Should().Equal("no price", "no price");
    }

    [TestMethod]
    public async Task Interpreter_ForEach_BodyStepsCountAgainstTheBudget()
    {
        var journal = new RecordingJournal();
        WorkflowGraph graph = LoopGraph(Node("b", "step@1"));

        WorkflowOutcome outcome = await Run(graph, journal, Catalog(new TestLoopNode()), triggerInput: """{"tags":[1,2,3]}""", maxSteps: 4);

        WorkflowOutcome.Failed failed = outcome.Should().BeOfType<WorkflowOutcome.Failed>().Subject;
        (failed.NodeId, failed.Failure.Kind).Should().Be(("each", WorkflowFailureKind.StepBudgetExhausted));
        journal.Entries.Should().Equal("started t", "completed t", "started each", "started b #0", "completed b", "started b #1", "completed b", "started b #2", "failed b StepBudgetExhausted", "failed each StepBudgetExhausted");
    }

    [TestMethod]
    public async Task Interpreter_ForEach_ConditionInsideTheBody_TakesItsBranchPerItem()
    {
        var journal = new RecordingJournal();
        WorkflowGraph graph = Graph(
            [Node("t", "trigger@1"), Node("each", "each@1", """{"items":"{{trigger.tags}}"}"""), Node("c", "if@1", """{"take":"{{item}}"}"""), Node("yes", "step@1", """{"v":"yes"}"""), Node("no", "step@1", """{"v":"no"}"""), Node("after", "step@1")],
            Edge("t", "each"), Edge("each", "c", LoopBranches.Body), Edge("c", "yes", "true"), Edge("c", "no", "false"), Edge("each", "after", LoopBranches.Done));

        await Run(graph, journal, Catalog(new TestLoopNode()), triggerInput: """{"tags":["true","false","true"]}""");

        journal.Outputs["each"].GetProperty("results").EnumerateArray().Select(result => result.GetProperty("v").GetString()).Should().Equal("yes", "no", "yes");
    }

    [TestMethod]
    public async Task Interpreter_ForEach_Replay_KeepsCollectedOutput()
    {
        var journal = new RecordingJournal();
        var ask = new TestSuspendingNode("ask");
        WorkflowGraph graph = Graph(
            [Node("t", "trigger@1"), Node("each", "each@1", """{"items":"{{trigger.tags}}"}"""), Node("b", "step@1"), Node("ask", "ask@1", """{"n":"{{steps.each.output.count}}"}"""), Node("after", "step@1", """{"r":"{{steps.each.output.results}}"}""")],
            Edge("t", "each"), Edge("each", "b", LoopBranches.Body), Edge("each", "ask", LoopBranches.Done), Edge("ask", "after"));
        var collected = Json("""{"count":2,"results":[{"a":1},{"a":2}]}""");
        var suspension = new WorkflowSuspension(
            Json("""{"tags":[1,2]}"""),
            [new CompletedStep("t", Json("""{"tags":[1,2]}"""), null), new CompletedStep("each", collected, LoopBranches.Done)],
            "ask",
            Json("""{"n":2}"""));

        WorkflowOutcome outcome = await Interpreter().ResumeAsync(Execution(graph, journal, Catalog(new TestLoopNode(), ask)), suspension, Json("""{"ok":true}"""), CancellationToken);

        outcome.Should().Be(new WorkflowOutcome.Completed());
        journal.Entries.Should().Equal("completed ask", "started after", "completed after");
        journal.Inputs["after"].GetProperty("r").GetArrayLength().Should().Be(2);
    }

    [TestMethod]
    public async Task Interpreter_ForEach_PinnedLoop_CompletesWithThePinAndRunsNoBody()
    {
        var journal = new RecordingJournal();
        var spy = new TestNode("spy");
        WorkflowGraph graph = LoopGraph(Node("b", "spy@1"));
        var pins = new Dictionary<string, PinnedStep>(StringComparer.Ordinal) { ["each"] = new(Json("""{"count":1,"results":[{"x":1}]}"""), LoopBranches.Done) };

        WorkflowOutcome outcome = await Run(graph, journal, Catalog(new TestLoopNode(), spy), triggerInput: """{"tags":[1,2,3]}""", pins: pins);

        outcome.Should().Be(new WorkflowOutcome.Completed());
        spy.Calls.Should().BeEmpty();
        journal.Entries.Should().Equal("started t", "completed t", "started each", "pinned each done", "started after", "completed after");
    }

    [TestMethod]
    public async Task Interpreter_ForEach_PinnedBodyNode_PinnedPerIteration()
    {
        var journal = new RecordingJournal();
        WorkflowGraph graph = LoopGraph(Node("b", "step@1"));
        var pins = new Dictionary<string, PinnedStep>(StringComparer.Ordinal) { ["b"] = new(Json("""{"x":7}"""), null) };

        await Run(graph, journal, Catalog(new TestLoopNode()), triggerInput: """{"tags":[1,2]}""", pins: pins);

        journal.Entries.Should().Equal("started t", "completed t", "started each", "started b #0", "pinned b", "started b #1", "pinned b", "completed each done", "started after", "completed after");
        journal.Outputs["each"].GetProperty("results").EnumerateArray().Select(result => result.GetProperty("x").GetInt32()).Should().Equal(7, 7);
    }

    // The trigger (output: its input, with `tags`), a loop over `{{trigger.tags}}` with the given body nodes chained in order, and `after` on done.
    private static WorkflowGraph LoopGraph(NodeDefinition first, NodeDefinition? second = null, int? max = null)
    {
        string config = max is { } cap ? $$$"""{"items":"{{trigger.tags}}","max":{{{cap}}}}""" : """{"items":"{{trigger.tags}}"}""";
        List<NodeDefinition> nodes = [Node("t", "trigger@1"), Node("each", "each@1", config), first];
        List<EdgeDefinition> edges = [Edge("t", "each"), Edge("each", first.Id, LoopBranches.Body), Edge("each", "after", LoopBranches.Done)];
        if (second is not null)
        {
            nodes.Add(second);
            edges.Add(Edge(first.Id, second.Id));
        }

        nodes.Add(Node("after", "step@1", """{"r":"{{steps.each.output.results}}"}"""));
        return new WorkflowGraph(nodes, edges);
    }

    [TestMethod]
    public async Task Interpreter_FanOut_RunsBothInDefinitionOrder()
    {
        var journal = new RecordingJournal();
        WorkflowGraph graph = Graph([Node("t", "trigger@1"), Node("b", "step@1"), Node("a", "step@1")], Edge("t", "a"), Edge("t", "b"));

        await Run(graph, journal, Catalog());

        journal.Entries.Where(entry => entry.StartsWith("started", StringComparison.Ordinal)).Should().Equal("started t", "started b", "started a");
    }

    [TestMethod]
    public async Task Interpreter_Condition_VisitsTakenBranchOnly()
    {
        var journal = new RecordingJournal();
        WorkflowGraph graph = Graph(
            [Node("t", "trigger@1"), Node("c", "if@1", """{"take":"false"}"""), Node("yes", "step@1"), Node("no", "step@1"), Node("join", "step@1")],
            Edge("t", "c"), Edge("c", "yes", "true"), Edge("c", "no", "false"), Edge("yes", "join"), Edge("no", "join"));

        await Run(graph, journal, Catalog());

        journal.Entries.Should().Equal("started t", "completed t", "started c", "completed c false", "started no", "completed no", "started join", "completed join");
    }

    [TestMethod]
    public async Task Interpreter_References_ResolvedWithTypes()
    {
        var journal = new RecordingJournal();
        WorkflowGraph graph = Graph(
            [
                Node("t", "trigger@1"),
                Node("s", "step@1", """{"total":12.5,"tags":["a","b"],"flag":true,"obj":{"k":1},"none":null}"""),
                Node("u", "step@1", """{"whole":"{{steps.s.output.total}}","text":"{{steps.s.output.total}} / {{steps.s.output.tags.1}} / {{steps.s.output.flag}} / {{steps.s.output.obj}} / {{steps.s.output.none}}[{{steps.s.output.missing}}]","doc":"{{trigger.documentId}}","gone":"{{steps.s.output.tags.9}}","list":["{{steps.s.output.obj}}"]}"""),
            ],
            Edge("t", "s"), Edge("s", "u"));

        await Run(graph, journal, Catalog(), triggerInput: """{"documentId":"d1"}""");

        JsonElement input = journal.Inputs["u"];
        input.GetProperty("whole").GetDecimal().Should().Be(12.5m);
        input.GetProperty("text").GetString().Should().Be("""12.5 / b / true / {"k":1} / []""");
        input.GetProperty("doc").GetString().Should().Be("d1");
        input.GetProperty("gone").ValueKind.Should().Be(JsonValueKind.Null);
        input.GetProperty("list")[0].GetProperty("k").GetInt32().Should().Be(1);
    }

    [TestMethod]
    public async Task Interpreter_NodeFails_RunFailsWithStepError()
    {
        var journal = new RecordingJournal();
        var broken = new TestNode("broken") { Execute = (_, _) => Task.FromResult<NodeResult>(new NodeResult.Failed("no such document")) };
        WorkflowGraph graph = Graph([Node("t", "trigger@1"), Node("a", "broken@1"), Node("b", "step@1")], Edge("t", "a"), Edge("a", "b"));

        WorkflowOutcome outcome = await Run(graph, journal, Catalog(broken));

        outcome.Should().Be(new WorkflowOutcome.Failed("a", new WorkflowFailure(WorkflowFailureKind.NodeFailed, "no such document")));
        journal.Entries.Should().Equal("started t", "completed t", "started a", "failed a NodeFailed");
    }

    [TestMethod]
    public async Task Interpreter_NodeThrows_RunFailsWithMessage()
    {
        var journal = new RecordingJournal();
        var broken = new TestNode("broken") { Execute = (_, _) => throw new InvalidOperationException("boom") };
        WorkflowGraph graph = Graph([Node("t", "trigger@1"), Node("a", "broken@1")], Edge("t", "a"));

        WorkflowOutcome outcome = await Run(graph, journal, Catalog(broken));

        outcome.Should().BeOfType<WorkflowOutcome.Failed>();
        journal.Failures["a"].Message.Should().Be("InvalidOperationException: boom");
    }

    [TestMethod]
    public async Task Interpreter_ForeignCancellationInNode_FailsNodeOnly()
    {
        var journal = new RecordingJournal();
        var broken = new TestNode("broken") { Execute = (_, _) => throw new TaskCanceledException("HTTP timeout") };
        WorkflowGraph graph = Graph([Node("t", "trigger@1"), Node("a", "broken@1")], Edge("t", "a"));

        WorkflowOutcome outcome = await Run(graph, journal, Catalog(broken));

        outcome.Should().BeOfType<WorkflowOutcome.Failed>().Which.Failure.Kind.Should().Be(WorkflowFailureKind.NodeFailed);
    }

    [TestMethod]
    [DataRow(null)]
    [DataRow("maybe")]
    public async Task Interpreter_BranchingNodeChoosesUndeclaredBranch_Fails(string? branch)
    {
        var journal = new RecordingJournal();
        var odd = new TestNode("odd") { Branches = ["true", "false"], Execute = (context, _) => Task.FromResult<NodeResult>(new NodeResult.Completed(context.Input, branch)) };
        WorkflowGraph graph = Graph([Node("t", "trigger@1"), Node("c", "odd@1"), Node("a", "step@1")], Edge("t", "c"), Edge("c", "a", "true"));

        WorkflowOutcome outcome = await Run(graph, journal, Catalog(odd));

        outcome.Should().BeOfType<WorkflowOutcome.Failed>().Which.NodeId.Should().Be("c");
    }

    [TestMethod]
    public async Task Interpreter_PlainNodeChoosesBranch_Fails()
    {
        var journal = new RecordingJournal();
        var odd = new TestNode("odd") { Execute = (context, _) => Task.FromResult<NodeResult>(new NodeResult.Completed(context.Input, "true")) };
        WorkflowGraph graph = Graph([Node("t", "trigger@1"), Node("a", "odd@1")], Edge("t", "a"));

        WorkflowOutcome outcome = await Run(graph, journal, Catalog(odd));

        journal.Failures["a"].Message.Should().Contain("does not branch");
        outcome.Should().BeOfType<WorkflowOutcome.Failed>();
    }

    [TestMethod]
    public async Task Interpreter_PlainNodeSuspends_Fails()
    {
        var journal = new RecordingJournal();
        var odd = new TestNode("odd") { Execute = (context, _) => Task.FromResult<NodeResult>(new NodeResult.Suspended(context.Input)) };
        WorkflowGraph graph = Graph([Node("t", "trigger@1"), Node("a", "odd@1")], Edge("t", "a"));

        WorkflowOutcome outcome = await Run(graph, journal, Catalog(odd));

        journal.Failures["a"].Message.Should().Be("A 'odd' node cannot suspend.");
        outcome.Should().BeOfType<WorkflowOutcome.Failed>();
    }

    [TestMethod]
    public async Task Interpreter_Suspended_StopsAndResumesAtNode()
    {
        var journal = new RecordingJournal();
        var ask = new TestSuspendingNode("ask");
        WorkflowGraph graph = Graph(
            [Node("t", "trigger@1"), Node("a", "step@1", """{"v":1}"""), Node("q", "ask@1", """{"question":"ok {{steps.a.output.v}}?"}"""), Node("after", "step@1", """{"answer":"{{steps.q.output.yes}}"}""")],
            Edge("t", "a"), Edge("a", "q"), Edge("q", "after"));
        IReadOnlyCollection<INodeType> catalog = Catalog(ask);

        WorkflowOutcome first = await Run(graph, journal, catalog);

        WorkflowOutcome.Suspended suspended = first.Should().BeOfType<WorkflowOutcome.Suspended>().Subject;
        suspended.NodeId.Should().Be("q");
        journal.Entries.Should().Equal("started t", "completed t", "started a", "completed a", "started q", "suspended q");

        var resumeJournal = new RecordingJournal();
        var suspension = new WorkflowSuspension(Json("{}"), [new CompletedStep("t", Json("{}"), null), new CompletedStep("a", journal.Outputs["a"], null)], "q", suspended.Bookmark);
        WorkflowOutcome second = await Interpreter().ResumeAsync(Execution(graph, resumeJournal, catalog), suspension, Json("""{"yes":true}"""), CancellationToken);

        second.Should().Be(new WorkflowOutcome.Completed());
        resumeJournal.Entries.Should().Equal("completed q", "started after", "completed after");
        ask.Resumes.Should().ContainSingle().Which.Bookmark.GetProperty("question").GetString().Should().Be("ok 1?");
        resumeJournal.Inputs["after"].GetProperty("answer").GetBoolean().Should().BeTrue();
    }

    [TestMethod]
    public async Task Interpreter_ResumeMissingCompletedStep_Throws()
    {
        WorkflowGraph graph = Graph([Node("t", "trigger@1"), Node("a", "step@1"), Node("q", "ask@1")], Edge("t", "a"), Edge("a", "q"));
        var suspension = new WorkflowSuspension(Json("{}"), [new CompletedStep("t", Json("{}"), null)], "q", Json("{}"));

        Func<Task> act = () => Interpreter().ResumeAsync(Execution(graph, new RecordingJournal(), Catalog(new TestSuspendingNode("ask"))), suspension, Json("{}"), CancellationToken);

        await act.Should().ThrowAsync<ArgumentException>().WithMessage("*does not fit*");
    }

    [TestMethod]
    public async Task Interpreter_ResumeExtraCompletedStep_Throws()
    {
        WorkflowGraph graph = Graph([Node("t", "trigger@1"), Node("q", "ask@1"), Node("b", "step@1")], Edge("t", "q"), Edge("q", "b"));
        var suspension = new WorkflowSuspension(Json("{}"), [new CompletedStep("t", Json("{}"), null), new CompletedStep("b", Json("{}"), null)], "q", Json("{}"));

        Func<Task> act = () => Interpreter().ResumeAsync(Execution(graph, new RecordingJournal(), Catalog(new TestSuspendingNode("ask"))), suspension, Json("{}"), CancellationToken);

        await act.Should().ThrowAsync<ArgumentException>().WithMessage("*does not fit*");
    }

    [TestMethod]
    public async Task Interpreter_ResumeNodeOnSkippedBranch_Throws()
    {
        WorkflowGraph graph = Graph(
            [Node("t", "trigger@1"), Node("c", "if@1", """{"take":"true"}"""), Node("q", "ask@1"), Node("b", "step@1")],
            Edge("t", "c"), Edge("c", "q", "false"), Edge("c", "b", "true"));
        var suspension = new WorkflowSuspension(Json("{}"), [new CompletedStep("t", Json("{}"), null), new CompletedStep("c", Json("{}"), "true")], "q", Json("{}"));

        Func<Task> act = () => Interpreter().ResumeAsync(Execution(graph, new RecordingJournal(), Catalog(new TestSuspendingNode("ask"))), suspension, Json("{}"), CancellationToken);

        await act.Should().ThrowAsync<ArgumentException>().WithMessage("*does not fit*");
    }

    [TestMethod]
    [DataRow("a")]
    [DataRow("ghost")]
    public async Task Interpreter_ResumeNonSuspendingNode_Throws(string nodeId)
    {
        WorkflowGraph graph = Graph([Node("t", "trigger@1"), Node("a", "step@1")], Edge("t", "a"));
        var suspension = new WorkflowSuspension(Json("{}"), [new CompletedStep("t", Json("{}"), null)], nodeId, Json("{}"));

        Func<Task> act = () => Interpreter().ResumeAsync(Execution(graph, new RecordingJournal(), Catalog()), suspension, Json("{}"), CancellationToken);

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [TestMethod]
    public async Task Interpreter_ResumeDuplicateOrSuspendedCompletedStep_Throws()
    {
        WorkflowGraph graph = Graph([Node("t", "trigger@1"), Node("q", "ask@1")], Edge("t", "q"));
        IReadOnlyCollection<INodeType> catalog = Catalog(new TestSuspendingNode("ask"));
        var duplicate = new WorkflowSuspension(Json("{}"), [new CompletedStep("t", Json("{}"), null), new CompletedStep("t", Json("{}"), null)], "q", Json("{}"));
        var alsoCompleted = new WorkflowSuspension(Json("{}"), [new CompletedStep("t", Json("{}"), null), new CompletedStep("q", Json("{}"), null)], "q", Json("{}"));

        Func<Task> first = () => Interpreter().ResumeAsync(Execution(graph, new RecordingJournal(), catalog), duplicate, Json("{}"), CancellationToken);
        Func<Task> second = () => Interpreter().ResumeAsync(Execution(graph, new RecordingJournal(), catalog), alsoCompleted, Json("{}"), CancellationToken);

        await first.Should().ThrowAsync<ArgumentException>();
        await second.Should().ThrowAsync<ArgumentException>();
    }

    [TestMethod]
    public async Task Interpreter_StepBudget_Fails()
    {
        var journal = new RecordingJournal();
        WorkflowGraph graph = Graph([Node("t", "trigger@1"), Node("a", "step@1"), Node("b", "step@1")], Edge("t", "a"), Edge("a", "b"));

        WorkflowOutcome outcome = await Run(graph, journal, Catalog(), maxSteps: 2);

        outcome.Should().BeOfType<WorkflowOutcome.Failed>().Which.Failure.Should().Be(new WorkflowFailure(WorkflowFailureKind.StepBudgetExhausted, "The run reached its limit of 2 steps."));
        journal.Entries.Should().Equal("started t", "completed t", "started a", "completed a", "started b", "failed b StepBudgetExhausted");
    }

    [TestMethod]
    public async Task Interpreter_StepBudgetOnResume_CountsEarlierSteps()
    {
        var journal = new RecordingJournal();
        WorkflowGraph graph = Graph([Node("t", "trigger@1"), Node("q", "ask@1"), Node("b", "step@1")], Edge("t", "q"), Edge("q", "b"));
        var suspension = new WorkflowSuspension(Json("{}"), [new CompletedStep("t", Json("{}"), null)], "q", Json("{}"));

        WorkflowOutcome outcome = await Interpreter().ResumeAsync(Execution(graph, journal, Catalog(new TestSuspendingNode("ask")), maxSteps: 2), suspension, Json("{}"), CancellationToken);

        outcome.Should().BeOfType<WorkflowOutcome.Failed>().Which.NodeId.Should().Be("b");
    }

    [TestMethod]
    public async Task Interpreter_Cancellation_LeavesJournalConsistent()
    {
        var journal = new RecordingJournal();
        using var source = new CancellationTokenSource();
        var slow = new TestNode("slow")
        {
            Execute = async (_, token) =>
            {
                await source.CancelAsync();
                token.ThrowIfCancellationRequested();
                return new NodeResult.Failed("unreachable");
            },
        };
        WorkflowGraph graph = Graph([Node("t", "trigger@1"), Node("a", "slow@1"), Node("b", "step@1")], Edge("t", "a"), Edge("a", "b"));

        Func<Task> act = () => Interpreter().RunAsync(Execution(graph, journal, Catalog(slow)), Json("{}"), source.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        journal.Entries.Should().Equal("started t", "completed t", "started a", "failed a Cancelled");
    }

    [TestMethod]
    public async Task Interpreter_CancelledBeforeStart_JournalsNothing()
    {
        var journal = new RecordingJournal();
        using var source = new CancellationTokenSource();
        await source.CancelAsync();
        WorkflowGraph graph = Graph([Node("t", "trigger@1")]);

        Func<Task> act = () => Interpreter().RunAsync(Execution(graph, journal, Catalog()), Json("{}"), source.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        journal.Entries.Should().BeEmpty();
    }

    [TestMethod]
    public async Task Interpreter_InvalidGraph_Throws()
    {
        WorkflowGraph graph = Graph([Node("a", "step@1")]);

        Func<Task> act = () => Run(graph, new RecordingJournal(), Catalog());

        await act.Should().ThrowAsync<ArgumentException>().WithMessage("*exactly one trigger*");
    }

    [TestMethod]
    public async Task Interpreter_SameInputs_SameVisit()
    {
        WorkflowGraph graph = Graph(
            [Node("t", "trigger@1"), Node("c", "if@1", """{"take":"true"}"""), Node("x", "step@1"), Node("y", "step@1"), Node("z", "step@1")],
            Edge("t", "c"), Edge("c", "y", "true"), Edge("c", "x", "true"), Edge("x", "z"), Edge("y", "z"));
        var first = new RecordingJournal();
        var second = new RecordingJournal();

        await Run(graph, first, Catalog());
        await Run(graph, second, Catalog());

        second.Entries.Should().Equal(first.Entries);
        first.Entries.Count(entry => entry == "started z").Should().Be(1);
    }


    [TestMethod]
    public async Task Run_PinnedNode_JournalsPinnedAndTakesBranch()
    {
        var journal = new RecordingJournal();
        var spy = new TestNode("if-spy") { Branches = ["true", "false"], Execute = (_, _) => throw new InvalidOperationException("a pinned node never executes") };
        WorkflowGraph graph = Graph(
            [Node("t", "trigger@1"), Node("c", "if-spy@1", """{"x":"{{trigger.k}}"}"""), Node("yes", "step@1"), Node("no", "step@1")],
            Edge("t", "c"), Edge("c", "yes", "true"), Edge("c", "no", "false"));
        var pins = new Dictionary<string, PinnedStep>(StringComparer.Ordinal) { ["c"] = new(Json("""{"stub":1}"""), "false") };

        WorkflowOutcome outcome = await Run(graph, journal, Catalog(spy), triggerInput: """{"k":"v"}""", pins: pins);

        outcome.Should().Be(new WorkflowOutcome.Completed());
        journal.Entries.Should().Equal("started t", "completed t", "started c", "pinned c false", "started no", "completed no");
        journal.Inputs["c"].GetProperty("x").GetString().Should().Be("v");
        journal.Outputs["c"].GetProperty("stub").GetInt32().Should().Be(1);
        spy.Calls.Should().BeEmpty();
    }

    [TestMethod]
    public async Task Run_PinnedTrigger_ReplacesTriggerOutput()
    {
        var journal = new RecordingJournal();
        var trigger = new TestNode("trigger") { IsTrigger = true, Execute = (_, _) => throw new InvalidOperationException("a pinned trigger never executes") };
        WorkflowGraph graph = Graph([Node("t", "trigger@1"), Node("s", "step@1", """{"d":"{{trigger.documentId}}"}""")], Edge("t", "s"));
        var pins = new Dictionary<string, PinnedStep>(StringComparer.Ordinal) { ["t"] = new(Json("""{"documentId":"pinned"}"""), null) };

        await Run(graph, journal, [trigger, new TestNode("step")], triggerInput: """{"documentId":"pinned"}""", pins: pins);

        journal.Entries.Should().Equal("started t", "pinned t", "started s", "completed s");
        journal.Inputs["s"].GetProperty("d").GetString().Should().Be("pinned");
        trigger.Calls.Should().BeEmpty();
    }

    [TestMethod]
    public async Task Run_PinnedSuspendingNode_CompletesWithPin()
    {
        var journal = new RecordingJournal();
        var ask = new TestSuspendingNode("ask");
        WorkflowGraph graph = Graph([Node("t", "trigger@1"), Node("a", "ask@1"), Node("s", "step@1", """{"v":"{{steps.a.output.answer}}"}""")], Edge("t", "a"), Edge("a", "s"));
        var pins = new Dictionary<string, PinnedStep>(StringComparer.Ordinal) { ["a"] = new(Json("""{"answer":42}"""), null) };

        WorkflowOutcome outcome = await Run(graph, journal, Catalog(ask), pins: pins);

        outcome.Should().Be(new WorkflowOutcome.Completed());
        journal.Entries.Should().Equal("started t", "completed t", "started a", "pinned a", "started s", "completed s");
        journal.Inputs["s"].GetProperty("v").GetInt32().Should().Be(42);
        ask.Calls.Should().BeEmpty();
    }

    [TestMethod]
    public async Task Run_PinnedUndeclaredBranch_FailsTheStep()
    {
        var journal = new RecordingJournal();
        WorkflowGraph graph = Graph([Node("t", "trigger@1"), Node("c", "if@1", """{"take":"true"}""")], Edge("t", "c"));
        var pins = new Dictionary<string, PinnedStep>(StringComparer.Ordinal) { ["c"] = new(Json("{}"), "maybe") };

        WorkflowOutcome outcome = await Run(graph, journal, Catalog(), pins: pins);

        outcome.Should().BeOfType<WorkflowOutcome.Failed>().Which.Failure.Kind.Should().Be(WorkflowFailureKind.NodeFailed);
        journal.Entries.Should().Equal("started t", "completed t", "started c", "failed c NodeFailed");
    }

    [TestMethod]
    public async Task Run_PinnedNode_CountsTowardsTheStepBudget()
    {
        var journal = new RecordingJournal();
        WorkflowGraph graph = Graph([Node("t", "trigger@1"), Node("a", "step@1"), Node("b", "step@1")], Edge("t", "a"), Edge("a", "b"));
        var pins = new Dictionary<string, PinnedStep>(StringComparer.Ordinal) { ["a"] = new(Json("{}"), null) };

        WorkflowOutcome outcome = await Run(graph, journal, Catalog(), maxSteps: 2, pins: pins);

        outcome.Should().BeOfType<WorkflowOutcome.Failed>().Which.Failure.Kind.Should().Be(WorkflowFailureKind.StepBudgetExhausted);
    }

    [TestMethod]
    public async Task Run_JournalWithoutStepPinned_GetsStepCompleted()
    {
        var journal = new LegacyJournal();
        WorkflowGraph graph = Graph([Node("t", "trigger@1"), Node("a", "step@1")], Edge("t", "a"));
        var pins = new Dictionary<string, PinnedStep>(StringComparer.Ordinal) { ["a"] = new(Json("{}"), null) };

        await Interpreter().RunAsync(new WorkflowExecution(graph, Catalog(), journal, 50, false) { Pins = pins }, Json("{}"), CancellationToken);

        journal.Entries.Should().Equal("started t", "completed t", "started a", "completed a");
    }

    [TestMethod]
    public void PinnedStep_UndefinedOutput_Throws()
    {
        Action act = () => _ = new PinnedStep(default, null);

        act.Should().Throw<ArgumentException>().WithParameterName("output");
    }

    [TestMethod]
    public async Task Run_TwoMajorsRegistered_BindsPinnedMajor()
    {
        var first = new TestNode("step") { Execute = (_, _) => Task.FromResult<NodeResult>(new NodeResult.Completed(Json("""{"major":1}"""))) };
        var second = new TestNode("step") { Major = 2, Execute = (_, _) => Task.FromResult<NodeResult>(new NodeResult.Completed(Json("""{"major":2}"""))) };
        var journal = new RecordingJournal();
        WorkflowGraph graph = Graph([Node("t", "trigger@1"), Node("old", "step@1"), Node("new", "step@2")], Edge("t", "old"), Edge("old", "new"));

        WorkflowOutcome outcome = await Run(graph, journal, [Catalog().First(type => type.IsTrigger), first, second]);

        outcome.Should().BeOfType<WorkflowOutcome.Completed>();
        first.Calls.Should().ContainSingle().Which.NodeType.Should().Be("step@1");
        second.Calls.Should().ContainSingle().Which.NodeType.Should().Be("step@2");
        journal.Outputs["old"].GetProperty("major").GetInt32().Should().Be(1);
        journal.Outputs["new"].GetProperty("major").GetInt32().Should().Be(2);
    }

    private static IReadOnlyCollection<INodeType> Catalog(params INodeType[] extra) =>
    [
        new TestNode("trigger") { IsTrigger = true, Execute = (context, _) => Task.FromResult<NodeResult>(new NodeResult.Completed(context.TriggerInput)) },
        new TestNode("step"),
        new TestNode("if")
        {
            Branches = ["true", "false"],
            Execute = (context, _) => Task.FromResult<NodeResult>(new NodeResult.Completed(Json("{}"), context.Input.GetProperty("take").GetString())),
        },
        .. extra,
    ];

    private static JsonElement Json(string json)
    {
        using JsonDocument document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }

    private static NodeDefinition Node(string id, string type, string config = "{}") => new(id, type, Json(config));

    private static EdgeDefinition Edge(string from, string to, string? branch = null) => new(from, to, branch);

    private static WorkflowGraph Graph(NodeDefinition[] nodes, params EdgeDefinition[] edges) => new(nodes, edges);

    private static WorkflowExecution Execution(
        WorkflowGraph graph, IStepJournal journal, IReadOnlyCollection<INodeType> catalog, int maxSteps = 50, bool dryRun = false, IReadOnlyDictionary<string, PinnedStep>? pins = null) =>
        new WorkflowExecution(graph, catalog, journal, maxSteps, dryRun) { Pins = pins ?? new Dictionary<string, PinnedStep>(StringComparer.Ordinal) };

    private IWorkflowInterpreter Interpreter() => GetServices().GetRequiredService<IWorkflowInterpreter>();

    private Task<WorkflowOutcome> Run(
        WorkflowGraph graph,
        IStepJournal journal,
        IReadOnlyCollection<INodeType> catalog,
        string triggerInput = "{}",
        int maxSteps = 50,
        bool dryRun = false,
        IReadOnlyDictionary<string, PinnedStep>? pins = null) =>
        Interpreter().RunAsync(Execution(graph, journal, catalog, maxSteps, dryRun, pins), Json(triggerInput), CancellationToken);
}
