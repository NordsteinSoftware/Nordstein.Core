using System.Text.Json;
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Nordstein.Core.Testing;

namespace Nordstein.Core.Workflows.Tests;

[TestClass]
public sealed class NodeTrialTests : BaseTest<Module>
{
    private const string MailOutput = """{"type":"object","properties":{"from":{"type":"object","properties":{"domain":{"type":"string"}}},"subject":{"type":"string"}},"required":["from","subject"]}""";

    [TestMethod]
    public async Task Try_ConditionWithOutputs_CompletesWithBranch()
    {
        WorkflowGraph graph = Graph(
            [Node("t", "trigger@1"), Node("s", "step@1", """{"total":3}"""), Node("c", "if@1", """{"take":"{{steps.s.output.take}}","left":"{{trigger.name}}"}""")],
            Edge("t", "s"), Edge("s", "c"));

        NodeTrialResult result = await Try(graph, Catalog(), "c", triggerOutput: """{"name":"huber"}""", outputs: new() { ["s"] = Json("""{"take":"true"}""") });

        result.Problems.Should().BeEmpty();
        result.ResolvedInput.Should().NotBeNull();
        result.ResolvedInput.GetValueOrDefault().GetProperty("take").GetString().Should().Be("true");
        result.ResolvedInput.GetValueOrDefault().GetProperty("left").GetString().Should().Be("huber");
        result.Result.Should().BeOfType<NodeResult.Completed>().Which.Branch.Should().Be("true");
        result.OutputProblems.Should().BeEmpty();
    }

    [TestMethod]
    public async Task Try_ReferenceToMissingOutput_ReportsNodeAndExecutesNothing()
    {
        var step = new TestNode("spy");
        WorkflowGraph graph = Graph(
            [Node("t", "trigger@1"), Node("s", "step@1"), Node("u", "spy@1", """{"x":"{{steps.s.output.total}}"}""")],
            Edge("t", "s"), Edge("s", "u"));

        NodeTrialResult result = await Try(graph, [.. Catalog(), step], "u", outputs: new());

        result.Problems.Should().ContainSingle().Which.Should().Match<WorkflowValidationError>(error => error.NodeId == "u" && error.Message.Contains("'s'", StringComparison.Ordinal));
        result.Result.Should().BeNull();
        result.ResolvedInput.Should().BeNull();
        step.Calls.Should().BeEmpty();
    }

    [TestMethod]
    public async Task Try_NodeWithRetry_ExecutesOnce()
    {
        var failing = new TestNode("failing") { Execute = (_, _) => Task.FromResult<NodeResult>(new NodeResult.Failed("down")) };
        WorkflowGraph graph = Graph([Node("t", "trigger@1"), Node("a", "failing@1") with { Retry = new RetryPolicy(3, TimeSpan.Zero) }], Edge("t", "a"));

        NodeTrialResult result = await Try(graph, [.. Catalog(), failing], "a");

        result.Result.Should().BeOfType<NodeResult.Failed>();
        failing.Calls.Should().ContainSingle();
    }

    [TestMethod]
    public async Task NodeTrial_BodyNode_TakesItemFromStubs()
    {
        var loop = new TestLoopNode();
        WorkflowGraph graph = Graph(
            [Node("t", "trigger@1"), Node("s", "step@1"), Node("each", "each@1", """{"items":"{{steps.s.output.tags}}"}"""), Node("b", "step@1", """{"name":"{{item.name}}","at":"{{index}}","text":"#{{index}}: {{item.name}}"}""")],
            Edge("t", "s"), Edge("s", "each"), Edge("each", "b", LoopBranches.Body));

        NodeTrialResult result = await Try(graph, [.. Catalog(), loop], "b", iteration: new IterationStub(Json("""{"name":"Zimmer"}"""), 2));

        result.Problems.Should().BeEmpty();
        JsonElement input = result.ResolvedInput.GetValueOrDefault();
        input.GetProperty("name").GetString().Should().Be("Zimmer");
        input.GetProperty("at").GetInt32().Should().Be(2);
        input.GetProperty("text").GetString().Should().Be("#2: Zimmer");
    }

    [TestMethod]
    public async Task NodeTrial_BodyNodeWithoutAnIterationStub_ReportsProblem()
    {
        var loop = new TestLoopNode();
        WorkflowGraph graph = Graph(
            [Node("t", "trigger@1"), Node("each", "each@1", """{"items":"{{trigger.tags}}"}"""), Node("b", "step@1", """{"name":"{{item.name}}"}""")],
            Edge("t", "each"), Edge("each", "b", LoopBranches.Body));

        NodeTrialResult result = await Try(graph, [.. Catalog(), loop], "b");

        result.Problems.Should().ContainSingle().Which.Message.Should().Be("Node 'b': config.name references {{item.name}}, which has no item to try with.");
        result.Result.Should().BeNull();
    }

    [TestMethod]
    public async Task NodeTrial_LoopNode_ReportsAnEmptyCollectionOnDone()
    {
        var loop = new TestLoopNode();
        WorkflowGraph graph = Graph([Node("t", "trigger@1"), Node("each", "each@1", """{"items":"{{trigger.tags}}"}""")], Edge("t", "each"));

        NodeTrialResult result = await Try(graph, [.. Catalog(), loop], "each", triggerOutput: """{"tags":["a","b"]}""");

        NodeResult.Completed completed = result.Result.Should().BeOfType<NodeResult.Completed>().Subject;
        completed.Branch.Should().Be(LoopBranches.Done);
        completed.Output.GetProperty("count").GetInt32().Should().Be(0);
        loop.Calls.Should().BeEmpty();
    }

    [TestMethod]
    public async Task Try_UnknownNodeId_Throws()
    {
        WorkflowGraph graph = Graph([Node("t", "trigger@1")]);

        Func<Task> act = () => Try(graph, Catalog(), "nope");

        await act.Should().ThrowAsync<ArgumentException>().WithParameterName("nodeId");
    }

    [TestMethod]
    public async Task Try_UnknownType_ReportsProblem()
    {
        WorkflowGraph graph = Graph([Node("t", "trigger@1"), Node("x", "alien@1")], Edge("t", "x"));

        NodeTrialResult result = await Try(graph, Catalog(), "x");

        result.Problems.Should().ContainSingle().Which.Message.Should().Contain("alien");
        result.Result.Should().BeNull();
    }

    [TestMethod]
    public async Task Try_UnknownMajor_ReportsProblem()
    {
        WorkflowGraph graph = Graph([Node("t", "trigger@1"), Node("x", "step@7")], Edge("t", "x"));

        NodeTrialResult result = await Try(graph, Catalog(), "x");

        result.Problems.Should().ContainSingle().Which.Message.Should().Be("Node 'x' has the unknown type 'step@7'; this catalogue offers 'step@1'.");
        result.Result.Should().BeNull();
    }

    [TestMethod]
    public async Task Try_ConfigFailsSchema_ReportsProblem()
    {
        var typed = new TestNode("typed", inputSchema: """{"type":"object","properties":{"n":{"type":"number"}},"required":["n"]}""");
        WorkflowGraph graph = Graph([Node("t", "trigger@1"), Node("x", "typed@1", """{"n":"text"}""")], Edge("t", "x"));

        NodeTrialResult result = await Try(graph, [.. Catalog(), typed], "x");

        result.Problems.Should().ContainSingle().Which.Message.Should().Contain("config.n must be number");
        typed.Calls.Should().BeEmpty();
    }

    [TestMethod]
    public async Task Try_ReferencePathMissingInSchema_ReportsProblem()
    {
        var mail = new TestNode("mail", outputSchema: MailOutput) { IsTrigger = true };
        WorkflowGraph graph = Graph([Node("t", "mail@1"), Node("u", "step@1", """{"x":"{{trigger.nope}}"}""")], Edge("t", "u"));

        NodeTrialResult result = await Try(graph, [new TestNode("step"), mail], "u");

        result.Problems.Should().ContainSingle().Which.Message.Should().Contain("'nope'");
    }

    [TestMethod]
    public async Task Try_OutputMissingRequired_ReportsPath()
    {
        var mail = new TestNode("mail", outputSchema: MailOutput)
        {
            Execute = (_, _) => Task.FromResult<NodeResult>(new NodeResult.Completed(Json("""{"from":{"domain":"huber.at"}}"""))),
        };
        WorkflowGraph graph = Graph([Node("t", "trigger@1"), Node("m", "mail@1")], Edge("t", "m"));

        NodeTrialResult result = await Try(graph, [.. Catalog(), mail], "m");

        result.Result.Should().BeOfType<NodeResult.Completed>();
        result.OutputProblems.Should().ContainSingle().Which.Should().Be(new OutputProblem("$.subject", "$.subject is required."));
    }

    [TestMethod]
    public async Task Try_OutputWrongTypeAndExtraProperty_ReportsEach()
    {
        var mail = new TestNode("mail", outputSchema: """{"type":"object","properties":{"n":{"type":"number"},"k":{"enum":["a","b"]},"list":{"type":"array","items":{"type":"string"}}},"additionalProperties":false}""")
        {
            Execute = (_, _) => Task.FromResult<NodeResult>(new NodeResult.Completed(Json("""{"n":"{{steps.x.output.y}}","k":"c","list":["ok",2],"extra":1}"""))),
        };
        WorkflowGraph graph = Graph([Node("t", "trigger@1"), Node("m", "mail@1")], Edge("t", "m"));

        NodeTrialResult result = await Try(graph, [.. Catalog(), mail], "m");

        result.OutputProblems.Select(problem => problem.Path).Should().Equal("$.n", "$.k", "$.list[1]", "$.extra");
    }

    [TestMethod]
    public async Task Try_SuspendingWithoutAnswer_ReportsBookmark()
    {
        var ask = new TestSuspendingNode("ask");
        WorkflowGraph graph = Graph([Node("t", "trigger@1"), Node("a", "ask@1", """{"q":"ok?"}""")], Edge("t", "a"));

        NodeTrialResult result = await Try(graph, [.. Catalog(), ask], "a");

        result.Result.Should().BeOfType<NodeResult.Suspended>().Which.Bookmark.GetProperty("q").GetString().Should().Be("ok?");
        ask.Resumes.Should().BeEmpty();
    }

    [TestMethod]
    public async Task Try_SuspendingWithAnswer_Resumes()
    {
        var ask = new TestSuspendingNode("ask");
        WorkflowGraph graph = Graph([Node("t", "trigger@1"), Node("a", "ask@1", """{"q":"ok?"}""")], Edge("t", "a"));

        NodeTrialResult result = await Try(graph, [.. Catalog(), ask], "a", answer: """{"yes":true}""");

        result.Result.Should().BeOfType<NodeResult.Completed>().Which.Output.GetProperty("yes").GetBoolean().Should().BeTrue();
        ask.Resumes.Should().ContainSingle().Which.Bookmark.GetProperty("q").GetString().Should().Be("ok?");
    }

    [TestMethod]
    public async Task Try_InvalidGraphElsewhere_StillTries()
    {
        WorkflowGraph graph = Graph(
            [Node("t", "trigger@1"), Node("a", "step@1"), Node("b", "step@1"), Node("u", "step@1", """{"x":"{{steps.a.output.v}}"}"""), Node("alien", "nope@1")],
            Edge("t", "a"), Edge("a", "b"), Edge("b", "a"), Edge("a", "u"));

        NodeTrialResult result = await Try(graph, Catalog(), "u", outputs: new() { ["a"] = Json("""{"v":1}""") });

        result.Problems.Should().BeEmpty();
        result.Result.Should().BeOfType<NodeResult.Completed>().Which.Output.GetProperty("x").GetInt32().Should().Be(1);
    }

    [TestMethod]
    public async Task NodeTrial_LoopNodeWhoseCollectThrows_FailsLikeAThrowingNode()
    {
        var loop = new TestLoopNode { CollectFailure = new InvalidOperationException("no sum") };
        WorkflowGraph graph = Graph([Node("t", "trigger@1"), Node("each", "each@1", """{"items":"{{trigger.tags}}"}""")], Edge("t", "each"));

        NodeTrialResult result = await Try(graph, [.. Catalog(), loop], "each", triggerOutput: """{"tags":["a"]}""");

        result.Result.Should().BeOfType<NodeResult.Failed>().Which.Reason.Should().Be("InvalidOperationException: no sum");
    }

    [TestMethod]
    public async Task Try_NodeThrows_Fails()
    {
        var broken = new TestNode("broken") { Execute = (_, _) => throw new InvalidOperationException("boom") };
        WorkflowGraph graph = Graph([Node("t", "trigger@1"), Node("b", "broken@1")], Edge("t", "b"));

        NodeTrialResult result = await Try(graph, [.. Catalog(), broken], "b");

        result.Result.Should().BeOfType<NodeResult.Failed>().Which.Reason.Should().Be("InvalidOperationException: boom");
    }

    [TestMethod]
    public async Task Try_Cancelled_Propagates()
    {
        using var cancellation = new CancellationTokenSource();
        var slow = new TestNode("slow") { Execute = async (_, token) => { await Task.Delay(Timeout.InfiniteTimeSpan, token); return new NodeResult.Completed(Json("{}")); } };
        WorkflowGraph graph = Graph([Node("t", "trigger@1"), Node("s", "slow@1")], Edge("t", "s"));
        await cancellation.CancelAsync();

        Func<Task> act = () => Trial().TryAsync(graph, [.. Catalog(), slow], "s", Json("{}"), new Dictionary<string, JsonElement>(), false, null, null, cancellation.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [TestMethod]
    public async Task Try_UndeclaredBranch_Fails()
    {
        WorkflowGraph graph = Graph([Node("t", "trigger@1"), Node("c", "if@1", """{"take":"maybe"}""")], Edge("t", "c"));

        NodeTrialResult result = await Try(graph, Catalog(), "c");

        result.Result.Should().BeOfType<NodeResult.Failed>().Which.Reason.Should().Contain("'maybe'");
    }

    [TestMethod]
    public async Task Try_DryRunAndTriggerInput_ReachTheNode()
    {
        var step = new TestNode("step");
        WorkflowGraph graph = Graph([Node("t", "trigger@1"), Node("s", "step@1")], Edge("t", "s"));

        await Try(graph, [new TestNode("trigger") { IsTrigger = true }, step], "s", triggerOutput: """{"k":1}""", dryRun: true);

        NodeContext call = step.Calls.Should().ContainSingle().Which;
        call.DryRun.Should().BeTrue();
        call.TriggerInput.GetProperty("k").GetInt32().Should().Be(1);
        call.NodeId.Should().Be("s");
    }

    private static IReadOnlyCollection<INodeType> Catalog() =>
    [
        new TestNode("trigger") { IsTrigger = true, Execute = (context, _) => Task.FromResult<NodeResult>(new NodeResult.Completed(context.TriggerInput)) },
        new TestNode("step"),
        new TestNode("if")
        {
            Branches = ["true", "false"],
            Execute = (context, _) => Task.FromResult<NodeResult>(new NodeResult.Completed(Json("{}"), context.Input.GetProperty("take").GetString())),
        },
    ];

    private static JsonElement Json(string json)
    {
        using JsonDocument document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }

    private static NodeDefinition Node(string id, string type, string config = "{}") => new(id, type, Json(config));

    private static EdgeDefinition Edge(string from, string to, string? branch = null) => new(from, to, branch);

    private static WorkflowGraph Graph(NodeDefinition[] nodes, params EdgeDefinition[] edges) => new(nodes, edges);

    private INodeTrial Trial() => GetServices().GetRequiredService<INodeTrial>();

    private Task<NodeTrialResult> Try(
        WorkflowGraph graph,
        IReadOnlyCollection<INodeType> catalog,
        string nodeId,
        string triggerOutput = "{}",
        Dictionary<string, JsonElement>? outputs = null,
        bool dryRun = false,
        string? answer = null,
        IterationStub? iteration = null) =>
        Trial().TryAsync(graph, catalog, nodeId, Json(triggerOutput), outputs ?? [], dryRun, answer is null ? null : Json(answer), iteration, CancellationToken);
}
