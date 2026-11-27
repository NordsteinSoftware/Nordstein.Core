using System.Text.Json;
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Nordstein.Core.Testing;

namespace Nordstein.Core.Workflows.Tests;

/// <summary>
/// The kernel's <c>condition</c> and <c>for-each</c> node types in runs: branches, loops over data, empty collections,
/// missing and wrongly typed data, suspend and resume through them, the step budget, and cancellation.
/// </summary>
[TestClass]
public sealed class StandardNodeTypeRunTests : BaseTest<Module>
{
    [TestMethod]
    public async Task Run_ConditionOnTriggerData_TakesItsBranch()
    {
        var journal = new RecordingJournal();
        WorkflowGraph graph = ConditionGraph("""{"all":[{"left":"{{trigger.total}}","op":"gt","right":100},{"left":"{{trigger.status}}","op":"in","right":["open","late"]}]}""");

        WorkflowOutcome outcome = await RunAsync(graph, journal, """{"total":150,"status":"Late"}""");

        outcome.Should().Be(new WorkflowOutcome.Completed());
        journal.Entries.Should().Equal("started t", "completed t", "started c", "completed c true", "started yes", "completed yes");
        journal.Outputs["c"].GetRawText().Should().Be("""{"result":true}""");
    }

    [TestMethod]
    public async Task Run_ConditionOnMissingData_ComparesNullAsEmptyText()
    {
        var journal = new RecordingJournal();
        WorkflowGraph graph = ConditionGraph("""{"any":[{"left":"{{trigger.missing}}","op":"gt","right":0},{"left":"{{trigger.absent.deeper}}","op":"eq","right":""}]}""");

        await RunAsync(graph, journal, "{}");

        journal.Entries.Should().Contain("completed c true");
        journal.Inputs["c"].GetProperty("any")[0].GetProperty("left").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [TestMethod]
    public async Task Run_ConditionOnWronglyTypedData_IsFalseNotAFailure()
    {
        var journal = new RecordingJournal();
        WorkflowGraph graph = ConditionGraph("""{"any":[{"left":"{{trigger.total}}","op":"gt","right":100},{"left":"{{trigger.status}}","op":"in","right":"{{trigger.allowed}}"},{"left":"{{trigger.status}}","op":"matches","right":"{{trigger.pattern}}"}]}""");

        WorkflowOutcome outcome = await RunAsync(graph, journal, """{"total":"a lot","status":{"code":1},"allowed":"open","pattern":42}""");

        outcome.Should().Be(new WorkflowOutcome.Completed());
        journal.Entries.Should().Equal("started t", "completed t", "started c", "completed c false", "started no", "completed no");
    }

    [TestMethod]
    public async Task Run_ConditionWithAPatternFromDataThatDoesNotCompile_FailsTheStepAndTakesTheErrorEdge()
    {
        var journal = new RecordingJournal();
        WorkflowGraph graph = new(
            [Node("t", "start@1"), Node("c", "condition@1", """{"all":[{"left":"x","op":"matches","right":"{{trigger.pattern}}"}]}"""), Node("yes", "step@1"), Node("onError", "step@1", """{"why":"{{steps.c.output.message}}"}""")],
            [Edge("t", "c"), Edge("c", "yes", "true"), Edge("c", "onError", ReservedBranches.Error)]);

        WorkflowOutcome outcome = await RunAsync(graph, journal, """{"pattern":"(["}""");

        outcome.Should().Be(new WorkflowOutcome.Completed());
        journal.Entries.Should().Equal("started t", "completed t", "started c", "recovered c", "started onError", "completed onError");
        journal.Inputs["onError"].GetProperty("why").GetString().Should().Be("config.all[0].right is not a regular expression.");
    }

    [TestMethod]
    public async Task Run_ForEachOverTriggerItems_RunsTheBodyPerItemAndCollects()
    {
        var journal = new RecordingJournal();
        WorkflowGraph graph = LoopGraph("""{"items":"{{trigger.rows}}","maxItems":3}""", Node("tell", "step@1", """{"text":"{{index}}: {{item.name}} costs {{item.price}}"}"""));

        WorkflowOutcome outcome = await RunAsync(graph, journal, """{"rows":[{"name":"Room","price":99},{"name":"Breakfast","price":12.5},{"name":"Parking","price":8}]}""");

        outcome.Should().Be(new WorkflowOutcome.Completed());
        journal.Entries.Should().Equal(
            "started t", "completed t", "started each",
            "started tell #0", "completed tell", "started tell #1", "completed tell", "started tell #2", "completed tell",
            "completed each done", "started after", "completed after");
        JsonElement collected = journal.Outputs["each"];
        collected.GetProperty("count").GetInt32().Should().Be(3);
        collected.GetProperty("results").EnumerateArray().Select(result => result.GetProperty("text").GetString())
            .Should().Equal("0: Room costs 99", "1: Breakfast costs 12.5", "2: Parking costs 8");
        journal.Inputs["after"].GetProperty("n").GetInt32().Should().Be(3);
    }

    [TestMethod]
    public async Task Run_ForEachOverAnEmptyCollection_RunsNoBodyAndCollectsNothing()
    {
        var journal = new RecordingJournal();
        WorkflowGraph graph = LoopGraph("""{"items":"{{trigger.rows}}"}""", Node("tell", "step@1", """{"text":"{{item}}"}"""));

        WorkflowOutcome outcome = await RunAsync(graph, journal, """{"rows":[]}""");

        outcome.Should().Be(new WorkflowOutcome.Completed());
        journal.Entries.Should().Equal("started t", "completed t", "started each", "completed each done", "started after", "completed after");
        journal.Outputs["each"].GetRawText().Should().Be("""{"count":0,"results":[]}""");
    }

    [TestMethod]
    public async Task Run_ForEachOverALiteralArray_IteratesIt()
    {
        var journal = new RecordingJournal();
        WorkflowGraph graph = LoopGraph("""{"items":["a","b"]}""", Node("tell", "step@1", """{"text":"{{item}}"}"""));

        await RunAsync(graph, journal, "{}");

        journal.Outputs["each"].GetProperty("results").EnumerateArray().Select(result => result.GetProperty("text").GetString()).Should().Equal("a", "b");
    }

    [TestMethod]
    [DataRow("""{}""", "items is not an array once resolved.")]
    [DataRow("""{"rows":"none"}""", "items is not an array once resolved.")]
    [DataRow("""{"rows":[1,2,3,4]}""", "items holds 4 items; at most 3.")]
    public async Task Run_ForEachOverMissingWronglyTypedOrTooManyItems_FailsTheStep(string triggerInput, string reason)
    {
        var journal = new RecordingJournal();
        WorkflowGraph graph = LoopGraph("""{"items":"{{trigger.rows}}","maxItems":3}""", Node("tell", "step@1"));

        WorkflowOutcome outcome = await RunAsync(graph, journal, triggerInput);

        outcome.Should().Be(new WorkflowOutcome.Failed("each", new WorkflowFailure(WorkflowFailureKind.NodeFailed, reason)));
        journal.Entries.Should().Equal("started t", "completed t", "started each", "failed each NodeFailed");
    }

    [TestMethod]
    public async Task Run_ForEachDefaultLimit_AllowsTwentyItems()
    {
        var journal = new RecordingJournal();
        WorkflowGraph graph = LoopGraph("""{"items":"{{trigger.rows}}"}""", Node("tell", "step@1"));
        string twenty = JsonSerializer.Serialize(new { rows = Enumerable.Range(0, IForEachNodeType.DefaultMaxItems).ToArray() });
        string twentyOne = JsonSerializer.Serialize(new { rows = Enumerable.Range(0, IForEachNodeType.DefaultMaxItems + 1).ToArray() });

        WorkflowOutcome allowed = await RunAsync(graph, journal, twenty);
        WorkflowOutcome refused = await RunAsync(graph, new RecordingJournal(), twentyOne);

        allowed.Should().Be(new WorkflowOutcome.Completed());
        refused.Should().Be(new WorkflowOutcome.Failed("each", new WorkflowFailure(WorkflowFailureKind.NodeFailed, "items holds 21 items; at most 20.")));
    }

    [TestMethod]
    public async Task Run_ConditionInsideTheLoopBody_BranchesPerItem()
    {
        var journal = new RecordingJournal();
        WorkflowGraph graph = new(
            [
                Node("t", "start@1"),
                Node("each", "for-each@1", """{"items":"{{trigger.rows}}"}"""),
                Node("c", "condition@1", """{"all":[{"left":"{{item.price}}","op":"gt","right":10}]}"""),
                Node("high", "step@1", """{"v":"high {{index}}"}"""),
                Node("low", "step@1", """{"v":"low {{index}}"}"""),
                Node("after", "step@1", """{"r":"{{steps.each.output.results}}"}"""),
            ],
            [Edge("t", "each"), Edge("each", "c", LoopBranches.Body), Edge("c", "high", "true"), Edge("c", "low", "false"), Edge("each", "after", LoopBranches.Done)]);

        await RunAsync(graph, journal, """{"rows":[{"price":99},{"price":5},{"price":"n/a"}]}""");

        journal.Outputs["each"].GetProperty("results").EnumerateArray().Select(result => result.GetProperty("v").GetString()).Should().Equal("high 0", "low 1", "low 2");
    }

    [TestMethod]
    public async Task Run_SuspendAfterAConditionThenResume_ReplaysTheConditionWithoutExecutingIt()
    {
        IServiceProvider services = GetServices();
        var ask = new TestSuspendingNode("ask");
        WorkflowGraph graph = new(
            [Node("t", "start@1"), Node("c", "condition@1", """{"all":[{"left":"{{trigger.total}}","op":"gt","right":100}]}"""), Node("ask", "ask@1", """{"q":"approve {{trigger.total}}?"}"""), Node("done", "step@1", """{"ok":"{{steps.ask.output.ok}}","r":"{{steps.c.output.result}}"}"""), Node("skip", "step@1")],
            [Edge("t", "c"), Edge("c", "ask", "true"), Edge("c", "skip", "false"), Edge("ask", "done")]);
        IReadOnlyCollection<INodeType> catalog = Catalog(services, ask);
        var first = new RecordingJournal();

        WorkflowOutcome suspended = await services.GetRequiredService<IWorkflowInterpreter>().RunAsync(Execution(graph, first, catalog), Json("""{"total":150}"""), CancellationToken);

        WorkflowOutcome.Suspended at = suspended.Should().BeOfType<WorkflowOutcome.Suspended>().Subject;
        at.NodeId.Should().Be("ask");
        first.Entries.Should().Equal("started t", "completed t", "started c", "completed c true", "started ask", "suspended ask");

        // The stored steps say the condition took `true`; the resume replays that even though the trigger input it is
        // given would now make the condition false: a replayed step is never executed again.
        var second = new RecordingJournal();
        var suspension = new WorkflowSuspension(
            Json("""{"total":150}"""),
            [new CompletedStep("t", first.Outputs["t"], null), new CompletedStep("c", first.Outputs["c"], "true")],
            "ask",
            at.Bookmark);
        WorkflowOutcome resumed = await services.GetRequiredService<IWorkflowInterpreter>().ResumeAsync(Execution(graph, second, catalog), suspension, Json("""{"ok":true}"""), CancellationToken);

        resumed.Should().Be(new WorkflowOutcome.Completed());
        second.Entries.Should().Equal("completed ask", "started done", "completed done");
        second.Inputs["done"].GetRawText().Should().Be("""{"ok":true,"r":true}""");
    }

    [TestMethod]
    public async Task Run_ResumeWithAConditionStepOnTheWrongBranch_IsRefused()
    {
        IServiceProvider services = GetServices();
        var ask = new TestSuspendingNode("ask");
        WorkflowGraph graph = new(
            [Node("t", "start@1"), Node("c", "condition@1", """{"all":[{"left":1,"op":"eq","right":1}]}"""), Node("ask", "ask@1"), Node("skip", "step@1")],
            [Edge("t", "c"), Edge("c", "ask", "true"), Edge("c", "skip", "false")]);
        var suspension = new WorkflowSuspension(
            Json("{}"),
            [new CompletedStep("t", Json("{}"), null), new CompletedStep("c", Json("""{"result":false}"""), "false")],
            "ask",
            Json("{}"));

        Func<Task> act = () => services.GetRequiredService<IWorkflowInterpreter>().ResumeAsync(Execution(graph, new RecordingJournal(), Catalog(services, ask)), suspension, Json("{}"), CancellationToken);

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [TestMethod]
    public async Task Run_SuspendBeforeTheLoopThenResume_LoopsOverTheAnswer()
    {
        IServiceProvider services = GetServices();
        var ask = new TestSuspendingNode("ask");
        WorkflowGraph graph = new(
            [Node("t", "start@1"), Node("ask", "ask@1"), Node("each", "for-each@1", """{"items":"{{steps.ask.output.picked}}"}"""), Node("tell", "step@1", """{"text":"{{item}}"}"""), Node("after", "step@1", """{"n":"{{steps.each.output.count}}"}""")],
            [Edge("t", "ask"), Edge("ask", "each"), Edge("each", "tell", LoopBranches.Body), Edge("each", "after", LoopBranches.Done)]);
        IReadOnlyCollection<INodeType> catalog = Catalog(services, ask);
        var journal = new RecordingJournal();
        var suspension = new WorkflowSuspension(Json("{}"), [new CompletedStep("t", Json("{}"), null)], "ask", Json("{}"));

        WorkflowOutcome outcome = await services.GetRequiredService<IWorkflowInterpreter>().ResumeAsync(Execution(graph, journal, catalog), suspension, Json("""{"picked":["x","y"]}"""), CancellationToken);

        outcome.Should().Be(new WorkflowOutcome.Completed());
        journal.Entries.Should().Equal(
            "completed ask", "started each", "started tell #0", "completed tell", "started tell #1", "completed tell", "completed each done", "started after", "completed after");
        journal.Inputs["after"].GetProperty("n").GetInt32().Should().Be(2);
    }

    [TestMethod]
    public async Task Run_SuspendAfterTheLoopThenResume_ReplaysTheCollectedOutputAndRunsNoBody()
    {
        IServiceProvider services = GetServices();
        var ask = new TestSuspendingNode("ask");
        var spy = new TestNode("spy");
        WorkflowGraph graph = new(
            [Node("t", "start@1"), Node("each", "for-each@1", """{"items":"{{trigger.rows}}"}"""), Node("tell", "spy@1", """{"text":"{{item}}"}"""), Node("ask", "ask@1", """{"n":"{{steps.each.output.count}}"}"""), Node("after", "step@1", """{"r":"{{steps.each.output.results}}"}""")],
            [Edge("t", "each"), Edge("each", "tell", LoopBranches.Body), Edge("each", "ask", LoopBranches.Done), Edge("ask", "after")]);
        IReadOnlyCollection<INodeType> catalog = Catalog(services, ask, spy);
        var first = new RecordingJournal();

        WorkflowOutcome suspended = await services.GetRequiredService<IWorkflowInterpreter>().RunAsync(Execution(graph, first, catalog), Json("""{"rows":["a","b"]}"""), CancellationToken);

        suspended.Should().BeOfType<WorkflowOutcome.Suspended>().Which.NodeId.Should().Be("ask");
        spy.Calls.Should().HaveCount(2);
        var second = new RecordingJournal();
        var suspension = new WorkflowSuspension(
            Json("""{"rows":["a","b"]}"""),
            [new CompletedStep("t", first.Outputs["t"], null), new CompletedStep("each", first.Outputs["each"], LoopBranches.Done)],
            "ask",
            Json("""{"n":2}"""));

        WorkflowOutcome resumed = await services.GetRequiredService<IWorkflowInterpreter>().ResumeAsync(Execution(graph, second, catalog), suspension, Json("""{"ok":true}"""), CancellationToken);

        resumed.Should().Be(new WorkflowOutcome.Completed());
        spy.Calls.Should().HaveCount(2, "the body is not replayed");
        second.Entries.Should().Equal("completed ask", "started after", "completed after");
        second.Inputs["after"].GetProperty("r").GetRawText().Should().Be("""[{"text":"a"},{"text":"b"}]""");
    }

    [TestMethod]
    public void Validate_SuspendingNodeInsideAForEachBody_Refused()
    {
        IServiceProvider services = GetServices();
        WorkflowGraph graph = new(
            [Node("t", "start@1"), Node("each", "for-each@1", """{"items":"{{trigger.rows}}"}"""), Node("ask", "ask@1")],
            [Edge("t", "each"), Edge("each", "ask", LoopBranches.Body)]);

        IReadOnlyList<WorkflowValidationError> errors = services.GetRequiredService<IWorkflowValidator>().Validate(graph, Catalog(services, new TestSuspendingNode("ask")), 10);

        errors.Should().ContainSingle().Which.Should().Be(new WorkflowValidationError("ask", "Node 'ask' waits for an answer inside the body of 'each'; a loop body does not suspend."));
    }

    [TestMethod]
    public async Task Run_ForEachBodyBeyondTheStepBudget_FailsTheLoopWithTheBudget()
    {
        var journal = new RecordingJournal();
        WorkflowGraph graph = LoopGraph("""{"items":"{{trigger.rows}}"}""", Node("tell", "step@1"));

        WorkflowOutcome outcome = await RunAsync(graph, journal, """{"rows":[1,2,3]}""", maxSteps: 3);

        outcome.Should().BeOfType<WorkflowOutcome.Failed>().Which.Failure.Kind.Should().Be(WorkflowFailureKind.StepBudgetExhausted);
        journal.Entries.Should().Equal("started t", "completed t", "started each", "started tell #0", "completed tell", "started tell #1", "failed tell StepBudgetExhausted", "failed each StepBudgetExhausted");
    }

    [TestMethod]
    public async Task Run_CancelledInsideTheLoopBody_JournalsCancelledAndPropagates()
    {
        IServiceProvider services = GetServices();
        using var cancellation = new CancellationTokenSource();
        var cancelling = new TestNode("cancelling")
        {
            Execute = (context, token) =>
            {
                if (context.Input.GetProperty("i").GetInt32() == 1)
                {
                    cancellation.Cancel();
                    token.ThrowIfCancellationRequested();
                }

                return Task.FromResult<NodeResult>(new NodeResult.Completed(context.Input));
            },
        };
        WorkflowGraph graph = LoopGraph("""{"items":"{{trigger.rows}}"}""", Node("tell", "cancelling@1", """{"i":"{{index}}"}"""));
        var journal = new RecordingJournal();

        Func<Task> act = () => services.GetRequiredService<IWorkflowInterpreter>().RunAsync(Execution(graph, journal, Catalog(services, cancelling)), Json("""{"rows":[1,2,3]}"""), cancellation.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        journal.Entries.Should().Equal("started t", "completed t", "started each", "started tell #0", "completed tell", "started tell #1", "failed tell Cancelled", "failed each Cancelled");
    }

    [TestMethod]
    public async Task Run_CancelledBeforeTheCondition_NeverStartsIt()
    {
        IServiceProvider services = GetServices();
        using var cancellation = new CancellationTokenSource();
        var trigger = new TestNode("start")
        {
            IsTrigger = true,
            Execute = (context, _) =>
            {
                cancellation.Cancel();
                return Task.FromResult<NodeResult>(new NodeResult.Completed(context.TriggerInput));
            },
        };
        WorkflowGraph graph = ConditionGraph("""{"all":[{"left":1,"op":"eq","right":1}]}""");
        IReadOnlyCollection<INodeType> catalog = [trigger, services.GetRequiredService<IConditionNodeType>(), new TestNode("step")];
        var journal = new RecordingJournal();

        Func<Task> act = () => services.GetRequiredService<IWorkflowInterpreter>().RunAsync(Execution(graph, journal, catalog), Json("{}"), cancellation.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        journal.Entries.Should().Equal("started t", "completed t");
    }

    [TestMethod]
    public async Task Try_ConditionNode_TriesItAgainstTheStubbedTrigger()
    {
        IServiceProvider services = GetServices();
        WorkflowGraph graph = ConditionGraph("""{"all":[{"left":"{{trigger.total}}","op":"lt","right":10}]}""");

        NodeTrialResult result = await services.GetRequiredService<INodeTrial>().TryAsync(graph, Catalog(services), "c", Json("""{"total":3}"""), new Dictionary<string, JsonElement>(), false, null, null, CancellationToken);

        result.Problems.Should().BeEmpty();
        result.Result.Should().BeOfType<NodeResult.Completed>().Which.Branch.Should().Be("true");
        result.OutputProblems.Should().BeEmpty();
    }

    // The trigger `t` (its output: the run's input), the condition `c` with the given configuration, `yes` on true, `no` on false.
    private static WorkflowGraph ConditionGraph(string config) =>
        new(
            [Node("t", "start@1"), Node("c", "condition@1", config), Node("yes", "step@1"), Node("no", "step@1")],
            [Edge("t", "c"), Edge("c", "yes", IConditionNodeType.True), Edge("c", "no", IConditionNodeType.False)]);

    // The trigger `t`, the loop `each` with the given configuration and the one body node, and `after` on done reading the count.
    private static WorkflowGraph LoopGraph(string config, NodeDefinition body) =>
        new(
            [Node("t", "start@1"), Node("each", "for-each@1", config), body, Node("after", "step@1", """{"n":"{{steps.each.output.count}}"}""")],
            [Edge("t", "each"), Edge("each", body.Id, LoopBranches.Body), Edge("each", "after", LoopBranches.Done)]);

    private static IReadOnlyCollection<INodeType> Catalog(IServiceProvider services, params INodeType[] extra) =>
    [
        new TestNode("start") { IsTrigger = true, Execute = (context, _) => Task.FromResult<NodeResult>(new NodeResult.Completed(context.TriggerInput)) },
        new TestNode("step"),
        services.GetRequiredService<IConditionNodeType>(),
        services.GetRequiredService<IForEachNodeType>(),
        .. extra,
    ];

    private static WorkflowExecution Execution(WorkflowGraph graph, IStepJournal journal, IReadOnlyCollection<INodeType> catalog, int maxSteps = 50) =>
        new(graph, catalog, journal, maxSteps, dryRun: false);

    private Task<WorkflowOutcome> RunAsync(WorkflowGraph graph, IStepJournal journal, string triggerInput, int maxSteps = 50)
    {
        IServiceProvider services = GetServices();
        return services.GetRequiredService<IWorkflowInterpreter>().RunAsync(Execution(graph, journal, Catalog(services), maxSteps), Json(triggerInput), CancellationToken);
    }

    private static NodeDefinition Node(string id, string type, string config = "{}") => new(id, type, Json(config));

    private static EdgeDefinition Edge(string from, string to, string? branch = null) => new(from, to, branch);

    private static JsonElement Json(string text)
    {
        using JsonDocument document = JsonDocument.Parse(text);
        return document.RootElement.Clone();
    }
}
