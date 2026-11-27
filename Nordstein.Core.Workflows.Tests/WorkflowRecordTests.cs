using System.Text.Json;
using AwesomeAssertions;

namespace Nordstein.Core.Workflows.Tests;

// WHY: these tests pass null where the contract forbids it, to prove the runtime guards; the `!` operator is banned (rule 5).
#pragma warning disable CS8625

[TestClass]
public sealed class WorkflowRecordTests
{
    [TestMethod]
    public void NodeDefinition_SameConfigContent_Equal()
    {
        var first = new NodeDefinition("a", "step", Json("""{"x": 1, "y": [true]}"""));
        var second = new NodeDefinition("a", "step", Json("""{"x":1,"y":[true]}"""));

        first.Should().Be(second);
        first.GetHashCode().Should().Be(second.GetHashCode());
        first.Should().NotBe(new NodeDefinition("a", "step", Json("""{"x":2}""")));
        first.Equals(null).Should().BeFalse();
    }

    [TestMethod]
    public void NodeDefinition_DifferentRetry_NotEqual()
    {
        var a = new NodeDefinition("a", "step", Json("{}")) with { Retry = new RetryPolicy(3, TimeSpan.FromSeconds(2)) };
        var same = new NodeDefinition("a", "step", Json("{}")) with { Retry = new RetryPolicy(3, TimeSpan.FromSeconds(2)) };
        var other = new NodeDefinition("a", "step", Json("{}")) with { Retry = new RetryPolicy(2, TimeSpan.FromSeconds(2)) };

        a.Should().Be(same);
        a.Should().NotBe(other);
        a.Should().NotBe(new NodeDefinition("a", "step", Json("{}")));
    }

    [TestMethod]
    public void RetryPolicy_InvalidArguments_Throw()
    {
        Action one = () => _ = new RetryPolicy(1, TimeSpan.Zero);
        Action negative = () => _ = new RetryPolicy(2, TimeSpan.FromSeconds(-1));

        one.Should().Throw<ArgumentOutOfRangeException>().WithParameterName("attempts");
        negative.Should().Throw<ArgumentOutOfRangeException>().WithParameterName("backoff");
    }

    [TestMethod]
    public void WorkflowGraph_SameContent_Equal()
    {
        WorkflowGraph first = new([new NodeDefinition("t", "trigger", Json("{}"))], [new EdgeDefinition("t", "a")]);
        WorkflowGraph second = new([new NodeDefinition("t", "trigger", Json("{}"))], [new EdgeDefinition("t", "a")]);

        first.Should().Be(second);
        first.GetHashCode().Should().Be(second.GetHashCode());
        first.Should().NotBe(new WorkflowGraph([new NodeDefinition("t", "trigger", Json("{}"))], [new EdgeDefinition("t", "b")]));
        first.Equals(null).Should().BeFalse();
    }

    [TestMethod]
    public void WorkflowGraph_CopiesItsLists()
    {
        List<NodeDefinition> nodes = [new NodeDefinition("t", "trigger", Json("{}"))];
        var graph = new WorkflowGraph(nodes, []);

        nodes.Clear();

        graph.Nodes.Should().ContainSingle();
    }

    [TestMethod]
    public void WorkflowGraph_NullItems_Throw()
    {
        FluentActions.Invoking(() => new WorkflowGraph(null, [])).Should().Throw<ArgumentNullException>();
        FluentActions.Invoking(() => new WorkflowGraph([], null)).Should().Throw<ArgumentNullException>();
        FluentActions.Invoking(() => new WorkflowGraph([null], [])).Should().Throw<ArgumentNullException>();
        FluentActions.Invoking(() => new WorkflowGraph([], [null])).Should().Throw<ArgumentNullException>();
    }

    [TestMethod]
    public void Definitions_InvalidArguments_Throw()
    {
        FluentActions.Invoking(() => new NodeDefinition(null, "t", Json("{}"))).Should().Throw<ArgumentNullException>();
        FluentActions.Invoking(() => new NodeDefinition("a", null, Json("{}"))).Should().Throw<ArgumentNullException>();
        FluentActions.Invoking(() => new NodeDefinition("a", "t", default)).Should().Throw<ArgumentException>();
        FluentActions.Invoking(() => new EdgeDefinition(null, "b")).Should().Throw<ArgumentNullException>();
        FluentActions.Invoking(() => new EdgeDefinition("a", null)).Should().Throw<ArgumentNullException>();
    }

    [TestMethod]
    public void NodeResults_InvalidArguments_Throw()
    {
        FluentActions.Invoking(() => new NodeResult.Completed(default)).Should().Throw<ArgumentException>();
        FluentActions.Invoking(() => new NodeResult.Suspended(default)).Should().Throw<ArgumentException>();
        FluentActions.Invoking(() => new NodeResult.Failed(" ")).Should().Throw<ArgumentException>();
        new NodeResult.Completed(Json("1"), "true").Branch.Should().Be("true");
        new NodeResult.Failed("why").Reason.Should().Be("why");
    }

    [TestMethod]
    public void WorkflowExecution_InvalidArguments_Throw()
    {
        var graph = new WorkflowGraph([], []);
        var journal = new RecordingJournal();

        FluentActions.Invoking(() => new WorkflowExecution(null, [], journal, 1, false)).Should().Throw<ArgumentNullException>();
        FluentActions.Invoking(() => new WorkflowExecution(graph, null, journal, 1, false)).Should().Throw<ArgumentNullException>();
        FluentActions.Invoking(() => new WorkflowExecution(graph, [], null, 1, false)).Should().Throw<ArgumentNullException>();
        FluentActions.Invoking(() => new WorkflowExecution(graph, [], journal, 0, false)).Should().Throw<ArgumentOutOfRangeException>();
    }

    [TestMethod]
    public void WorkflowSuspension_InvalidArguments_Throw()
    {
        FluentActions.Invoking(() => new WorkflowSuspension(Json("{}"), null, "q", Json("{}"))).Should().Throw<ArgumentNullException>();
        FluentActions.Invoking(() => new WorkflowSuspension(Json("{}"), [null], "q", Json("{}"))).Should().Throw<ArgumentNullException>();
        FluentActions.Invoking(() => new WorkflowSuspension(Json("{}"), [], null, Json("{}"))).Should().Throw<ArgumentNullException>();
        FluentActions.Invoking(() => new WorkflowSuspension(default, [], "q", Json("{}"))).Should().Throw<ArgumentException>();
        FluentActions.Invoking(() => new WorkflowSuspension(Json("{}"), [], "q", default)).Should().Throw<ArgumentException>();
    }

    private static JsonElement Json(string json)
    {
        using JsonDocument document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }
}
