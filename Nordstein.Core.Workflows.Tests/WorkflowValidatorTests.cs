using System.Text.Json;
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Nordstein.Core.Testing;

namespace Nordstein.Core.Workflows.Tests;

// WHY: one test passes null where the contract forbids it, to prove the runtime guards; the `!` operator is banned (rule 5).
#pragma warning disable CS8625

[TestClass]
public sealed class WorkflowValidatorTests : BaseTest<Module>
{
    private const string SourceOutput = """{"type":"object","properties":{"total":{"type":"number"},"name":{"type":"string"},"meta":{"type":"object","properties":{"tags":{"type":"array","items":{"type":"string"}}}},"free":{"type":"object"}}}""";

    [TestMethod]
    public void Validate_LinearGraph_NoErrors()
    {
        IReadOnlyList<WorkflowValidationError> errors = Validate(Graph([Node("t", "trigger@1"), Node("a", "step@1"), Node("b", "step@1")], Edge("t", "a"), Edge("a", "b")));

        errors.Should().BeEmpty();
    }

    [TestMethod]
    public void Validator_Cycle_Rejected()
    {
        IReadOnlyList<WorkflowValidationError> errors = Validate(Graph(
            [Node("t", "trigger@1"), Node("a", "step@1"), Node("b", "step@1"), Node("c", "step@1")],
            Edge("t", "a"), Edge("a", "b"), Edge("b", "a"), Edge("b", "c")));

        errors.Where(error => error.Message.Contains("cycle", StringComparison.Ordinal)).Select(error => error.NodeId).Should().Equal("a", "b");
    }

    [TestMethod]
    public void Validator_ErrorEdgeOnTrigger_Refused()
    {
        IReadOnlyList<WorkflowValidationError> errors = Validate(Graph([Node("t", "trigger@1"), Node("a", "step@1")], Edge("t", "a", ReservedBranches.Error)));

        errors.Should().ContainSingle().Which.Should().Be(new WorkflowValidationError("t", "The edge from 't' to 'a' names the branch 'error', but a trigger does not fail into a branch."));
    }

    [TestMethod]
    public void Validator_ErrorEdgeOnAnyOtherNode_Allowed()
    {
        IReadOnlyList<WorkflowValidationError> errors = Validate(Graph(
            [Node("t", "trigger@1"), Node("a", "step@1"), Node("c", "if@1", """{"take":"true"}"""), Node("onError", "step@1"), Node("yes", "step@1"), Node("alsoError", "step@1")],
            Edge("t", "a"), Edge("a", "onError", ReservedBranches.Error), Edge("a", "c"), Edge("c", "yes", "true"), Edge("c", "alsoError", ReservedBranches.Error)));

        errors.Should().BeEmpty();
    }

    [TestMethod]
    public void Validator_TypeDeclaringTheErrorBranch_Refused()
    {
        var reserved = new TestNode("bad") { Branches = ["ok", ReservedBranches.Error] };

        IReadOnlyList<WorkflowValidationError> errors = Validate(Graph([Node("t", "trigger@1"), Node("a", "bad@1")], Edge("t", "a")), extra: reserved);

        errors.Should().ContainSingle().Which.Message.Should().Be("Node 'a' has the type 'bad@1', which declares the reserved branch 'error'.");
    }

    [TestMethod]
    public void Validator_ReferenceReachedOnlyThroughTheErrorEdge_TypedByTheErrorSchema()
    {
        IReadOnlyList<WorkflowValidationError> errors = Validate(Graph(
            [Node("t", "trigger@1"), Node("a", "source@1"), Node("onError", "step@1", """{"m":"{{steps.a.output.message}}","k":"{{steps.a.output.nodeType}}","bad":"{{steps.a.output.total}}"}"""), Node("next", "step@1", """{"ok":"{{steps.a.output.total}}","bad":"{{steps.a.output.message}}"}""")],
            Edge("t", "a"), Edge("a", "onError", ReservedBranches.Error), Edge("a", "next")));

        errors.Select(error => error.Message).Should().Equal(
            "Node 'onError': config.bad references 'total' in {{steps.a.output.total}}, which the output of 'a' does not have.",
            "Node 'next': config.bad references 'message' in {{steps.a.output.message}}, which the output of 'a' does not have.");
    }

    [TestMethod]
    public void Validator_RetryOnSideEffectNode_Refused()
    {
        var writer = new TestNode("writer") { HasSideEffects = true };
        NodeDefinition node = Node("w", "writer@1") with { Retry = new RetryPolicy(3, TimeSpan.Zero) };

        IReadOnlyList<WorkflowValidationError> errors = Validate(Graph([Node("t", "trigger@1"), node], Edge("t", "w")), extra: writer);

        errors.Should().ContainSingle().Which.Should().Be(new WorkflowValidationError("w", "Node 'w' retries, but a 'writer@1' node has side effects; only a read-only node retries."));
    }

    [TestMethod]
    public void Validator_ReadOnlyTrigger_NodeNotReadOnly_Refused()
    {
        var start = new TestNode("look") { IsTrigger = true, ReadOnly = true };
        var reader = new TestNode("reader") { ReadOnly = true };
        var writer = new TestNode("writer") { HasSideEffects = true };
        WorkflowGraph graph = Graph([Node("t", "look@1"), Node("r", "reader@1"), Node("s", "step@1"), Node("w", "writer@1")], Edge("t", "r"), Edge("r", "s"), Edge("s", "w"));

        IReadOnlyList<WorkflowValidationError> errors = GetServices().GetRequiredService<IWorkflowValidator>().Validate(graph, [.. Catalog(start), reader, writer], 50);

        errors.Should().Equal(
            new WorkflowValidationError("s", "Node 's' has the type 'step@1', which is not read-only; a graph started by the read-only trigger 'look@1' holds only read-only nodes."),
            new WorkflowValidationError("w", "Node 'w' has the type 'writer@1', which is not read-only; a graph started by the read-only trigger 'look@1' holds only read-only nodes."));
    }

    [TestMethod]
    public void Validator_ReadOnlyTrigger_EveryNodeReadOnly_Valid()
    {
        var start = new TestNode("look") { IsTrigger = true, ReadOnly = true };
        var reader = new TestNode("reader") { ReadOnly = true };

        IReadOnlyList<WorkflowValidationError> errors = GetServices().GetRequiredService<IWorkflowValidator>().Validate(Graph([Node("t", "look@1"), Node("r", "reader@1")], Edge("t", "r")), [.. Catalog(start), reader], 50);

        errors.Should().BeEmpty();
    }

    [TestMethod]
    public void Validator_TriggerNotReadOnly_ReadOnlyAndOtherNodesMix_Valid()
    {
        var reader = new TestNode("reader") { ReadOnly = true };
        var writer = new TestNode("writer") { HasSideEffects = true };

        IReadOnlyList<WorkflowValidationError> errors = Validate(Graph([Node("t", "trigger@1"), Node("r", "reader@1"), Node("w", "writer@1")], Edge("t", "r"), Edge("r", "w")), extra: reader, more: writer);

        errors.Should().BeEmpty();
    }

    [TestMethod]
    public void Validator_ReadOnlyTypeWithSideEffects_Throws()
    {
        var contradiction = new TestNode("both") { ReadOnly = true, HasSideEffects = true };

        FluentActions.Invoking(() => Validate(Graph([Node("t", "trigger@1")]), extra: contradiction)).Should().Throw<ArgumentException>().WithMessage("*'both@1'*read-only*side effects*");
    }

    [TestMethod]
    public void Validator_ReadOnlySuspendingType_Throws()
    {
        var waiting = new TestSuspendingNode("ask") { ReadOnly = true };

        FluentActions.Invoking(() => Validate(Graph([Node("t", "trigger@1")]), extra: waiting)).Should().Throw<ArgumentException>().WithMessage("*'ask@1'*read-only*suspends*");
    }

    [TestMethod]
    public void NodeType_ReadOnlyNotDeclared_DefaultsToFalse()
    {
        INodeType type = new MinimalNode();

        type.ReadOnly.Should().BeFalse();
    }

    [TestMethod]
    public void Validator_RetryOnTrigger_Refused()
    {
        NodeDefinition trigger = Node("t", "trigger@1") with { Retry = new RetryPolicy(2, TimeSpan.Zero) };

        IReadOnlyList<WorkflowValidationError> errors = Validate(Graph([trigger, Node("a", "step@1")], Edge("t", "a")));

        errors.Should().ContainSingle().Which.Should().Be(new WorkflowValidationError("t", "Node 't' retries, but a trigger starts the run and does not retry."));
    }

    [TestMethod]
    public void Validator_RetryOnLoop_Refused()
    {
        WorkflowGraph graph = Graph(
            [Node("t", "trigger@1"), Node("each", "each@1", """{"items":"{{trigger.a}}"}""") with { Retry = new RetryPolicy(2, TimeSpan.Zero) }, Node("b", "step@1")],
            Edge("t", "each"), Edge("each", "b", LoopBranches.Body));

        IReadOnlyList<WorkflowValidationError> errors = Validate(graph, extra: new TestLoopNode());

        errors.Should().ContainSingle().Which.Message.Should().Be("Node 'each' retries, but a 'each@1' node is a loop the kernel runs; a body node retries instead.");
    }

    [TestMethod]
    public void Validator_RetryOnReadOnlyNode_Allowed()
    {
        NodeDefinition node = Node("a", "step@1") with { Retry = new RetryPolicy(5, TimeSpan.FromSeconds(1)) };

        Validate(Graph([Node("t", "trigger@1"), node], Edge("t", "a"))).Should().BeEmpty();
    }

    [TestMethod]
    public void Validator_LoopWithBody_NoErrors()
    {
        IReadOnlyList<WorkflowValidationError> errors = Validate(LoopGraph(Node("b", "step@1", """{"x":"{{item}}","i":"{{index}}","t":"{{steps.s.output.total}}","text":"#{{index}} {{item}}"}""")), extra: new TestLoopNode());

        errors.Should().BeEmpty();
    }

    [TestMethod]
    public void Validator_EdgeLeavingBody_Refused()
    {
        IReadOnlyList<WorkflowValidationError> errors = Validate(Graph(
            [Node("t", "trigger@1"), Node("s", "source@1"), Node("each", "each@1", """{"items":"{{steps.s.output.meta.tags}}"}"""), Node("b", "step@1"), Node("after", "step@1")],
            Edge("t", "s"), Edge("s", "each"), Edge("each", "b", LoopBranches.Body), Edge("each", "after", LoopBranches.Done), Edge("b", "after")), extra: new TestLoopNode());

        errors.Should().ContainSingle().Which.Message.Should().Be("Node 'after' is reached from the body of 'each' and from outside it; a body node runs only inside its loop, and the loop's done branch continues after the body.");
    }

    [TestMethod]
    public void Validator_NestedForEach_Refused()
    {
        IReadOnlyList<WorkflowValidationError> errors = Validate(Graph(
            [Node("t", "trigger@1"), Node("s", "source@1"), Node("each", "each@1", """{"items":"{{steps.s.output.meta.tags}}"}"""), Node("inner", "each@1", """{"items":"{{steps.s.output.meta.tags}}"}"""), Node("b", "step@1")],
            Edge("t", "s"), Edge("s", "each"), Edge("each", "inner", LoopBranches.Body), Edge("inner", "b", LoopBranches.Body)), extra: new TestLoopNode());

        errors.Should().ContainSingle().Which.Message.Should().Be("Node 'inner' is a loop inside the body of 'each'; loops do not nest.");
    }

    [TestMethod]
    public void Validator_SuspendingNodeInBody_Refused()
    {
        IReadOnlyList<WorkflowValidationError> errors = Validate(LoopGraph(Node("b", "ask@1")), extra: new TestLoopNode(), more: new TestSuspendingNode("ask"));

        errors.Should().ContainSingle().Which.Message.Should().Be("Node 'b' waits for an answer inside the body of 'each'; a loop body does not suspend.");
    }

    [TestMethod]
    public void Validator_BodyNodeReachedAnotherWay_Refused()
    {
        IReadOnlyList<WorkflowValidationError> errors = Validate(Graph(
            [Node("t", "trigger@1"), Node("s", "source@1"), Node("each", "each@1", """{"items":"{{steps.s.output.meta.tags}}"}"""), Node("b", "step@1")],
            Edge("t", "s"), Edge("s", "each"), Edge("each", "b", LoopBranches.Body), Edge("s", "b")), extra: new TestLoopNode());

        errors.Should().ContainSingle().Which.Message.Should().Be("Node 'b' is reached from the body of 'each' and from outside it; a body node runs only inside its loop, and the loop's done branch continues after the body.");
    }

    [TestMethod]
    public void Validator_ItemReferenceOutsideBody_Refused()
    {
        IReadOnlyList<WorkflowValidationError> errors = Validate(Graph(
            [Node("t", "trigger@1"), Node("a", "step@1", """{"x":"{{item.name}}","i":"{{index}}"}""")], Edge("t", "a")));

        errors.Select(error => error.Message).Should().Equal(
            "Node 'a': config.x references {{item.name}} outside a loop body.",
            "Node 'a': config.i references {{index}} outside a loop body.");
    }

    [TestMethod]
    public void Validator_ItemReference_TypedByTheArraysItems()
    {
        IReadOnlyList<WorkflowValidationError> errors = Validate(LoopGraph(Node("b", "typed@1", """{"label":"{{item}}","count":"{{item.name}}","amount":"{{index.x}}"}""")), extra: new TestLoopNode());

        errors.Select(error => error.Message).Should().Equal(
            "Node 'b': config.count references 'name' in {{item.name}}, which the output of 'item' does not have.",
            "Node 'b': config.amount references 'x' in {{index.x}}, which the output of 'index' does not have.");
    }

    [TestMethod]
    public void Validator_BodyNodeReadingItsLoopsOutput_Refused()
    {
        IReadOnlyList<WorkflowValidationError> errors = Validate(LoopGraph(Node("b", "step@1", """{"n":"{{steps.each.output.count}}"}""")), extra: new TestLoopNode());

        errors.Should().ContainSingle().Which.Message.Should().Be("Node 'b': config.n references the output of its loop 'each' in {{steps.each.output.count}}, which exists only after the loop.");
    }

    [TestMethod]
    public void Validator_LoopTypeWithoutBodyAndDone_Refused()
    {
        var odd = new OddLoopNode();

        IReadOnlyList<WorkflowValidationError> errors = Validate(Graph([Node("t", "trigger@1"), Node("each", "odd@1", """{"items":[]}""")], Edge("t", "each")), extra: odd);

        errors.Should().ContainSingle().Which.Message.Should().Be("Node 'each' has the type 'odd@1', which iterates but does not declare exactly the branches 'body' and 'done'.");
    }

    // The trigger, a source with `meta.tags` (an array of strings), a loop over them with `body`, and `after` on `done`.
    private static WorkflowGraph LoopGraph(NodeDefinition body) => Graph(
        [Node("t", "trigger@1"), Node("s", "source@1"), Node("each", "each@1", """{"items":"{{steps.s.output.meta.tags}}"}"""), body, Node("after", "step@1", """{"r":"{{steps.each.output.results}}"}""")],
        Edge("t", "s"), Edge("s", "each"), Edge("each", body.Id, LoopBranches.Body), Edge("each", "after", LoopBranches.Done));

    private sealed class OddLoopNode : TestNode, IIteratingNodeType
    {
        public OddLoopNode()
            : base("odd")
        {
            Branches = ["loop"];
        }

        public string ItemsProperty => "items";

        public int MaxItems(JsonElement config) => 5;

        public JsonElement Collect(NodeContext context, IReadOnlyList<JsonElement> results) => JsonSerializer.SerializeToElement(results);
    }

    [TestMethod]
    public void Validator_SelfEdge_Rejected()
    {
        IReadOnlyList<WorkflowValidationError> errors = Validate(Graph([Node("t", "trigger@1"), Node("a", "step@1")], Edge("t", "a"), Edge("a", "a")));

        errors.Should().ContainSingle().Which.NodeId.Should().Be("a");
    }

    [TestMethod]
    public void Validator_TwoTriggers_Rejected()
    {
        IReadOnlyList<WorkflowValidationError> errors = Validate(Graph([Node("t", "trigger@1"), Node("u", "trigger@1"), Node("a", "step@1")], Edge("t", "a"), Edge("u", "a")));

        errors.Should().Contain(error => error.NodeId == "u" && error.Message.Contains("exactly one trigger", StringComparison.Ordinal));
    }

    [TestMethod]
    public void Validator_NoTrigger_Rejected()
    {
        IReadOnlyList<WorkflowValidationError> errors = Validate(Graph([Node("a", "step@1")]));

        errors.Should().ContainSingle().Which.Should().Be(new WorkflowValidationError(null, "A workflow has exactly one trigger node; this one has none."));
    }

    [TestMethod]
    public void Validator_UnreachableNode_Rejected()
    {
        IReadOnlyList<WorkflowValidationError> errors = Validate(Graph([Node("t", "trigger@1"), Node("a", "step@1"), Node("lost", "step@1")], Edge("t", "a")));

        errors.Should().ContainSingle().Which.NodeId.Should().Be("lost");
    }

    [TestMethod]
    public void Validator_TooManyNodes_Rejected()
    {
        IReadOnlyList<WorkflowValidationError> errors = Validate(Graph([Node("t", "trigger@1"), Node("a", "step@1"), Node("b", "step@1")], Edge("t", "a"), Edge("a", "b")), maxNodes: 2);

        errors.Should().ContainSingle().Which.Message.Should().Be("A workflow has at most 2 nodes; this one has 3.");
    }

    [TestMethod]
    public void Validator_Empty_Rejected()
    {
        Validate(Graph([])).Should().ContainSingle().Which.Message.Should().Be("A workflow has at least one node.");
    }

    [TestMethod]
    [DataRow("1a")]
    [DataRow("a.b")]
    [DataRow("")]
    public void Validator_MalformedNodeId_Rejected(string id)
    {
        IReadOnlyList<WorkflowValidationError> errors = Validate(Graph([Node("t", "trigger@1"), Node(id, "step@1")], Edge("t", id)));

        errors.Should().ContainSingle().Which.NodeId.Should().Be(id);
    }

    [TestMethod]
    public void Validator_DuplicateId_Rejected()
    {
        IReadOnlyList<WorkflowValidationError> errors = Validate(Graph([Node("t", "trigger@1"), Node("a", "step@1"), Node("a", "step@1")], Edge("t", "a")));

        errors.Should().ContainSingle().Which.Message.Should().Contain("used twice");
    }

    [TestMethod]
    public void Validator_UnknownType_Rejected()
    {
        IReadOnlyList<WorkflowValidationError> errors = Validate(Graph([Node("t", "trigger@1"), Node("a", "mystery@1")], Edge("t", "a")));

        errors.Should().ContainSingle().Which.Message.Should().Contain("unknown type 'mystery@1'");
    }

    [TestMethod]
    public void Validate_UnknownMajor_RefusesNamingNode()
    {
        IReadOnlyList<WorkflowValidationError> errors = Validate(Graph([Node("t", "trigger@1"), Node("a", "step@9")], Edge("t", "a")));

        errors.Should().ContainSingle().Which.Should().Be(new WorkflowValidationError("a", "Node 'a' has the unknown type 'step@9'; this catalogue offers 'step@1'."));
    }

    [TestMethod]
    public void Validate_MalformedType_RefusesNamingNode()
    {
        IReadOnlyList<WorkflowValidationError> errors = Validate(Graph([Node("t", "trigger@1"), Node("a", "step")], Edge("t", "a")));

        errors.Should().ContainSingle().Which.Should().Be(new WorkflowValidationError("a", "Node 'a' has the type 'step', which is not of the form name@major."));
    }

    [TestMethod]
    public void Validate_TwoMajorsRegistered_EachBinds()
    {
        var second = new TestNode("step", inputSchema: """{"type":"object","required":["why"],"properties":{"why":{"type":"string"}}}""") { Major = 2 };

        Validate(Graph([Node("t", "trigger@1"), Node("a", "step@1"), Node("b", "step@2", """{"why":"x"}""")], Edge("t", "a"), Edge("a", "b")), extra: second).Should().BeEmpty();
        Validate(Graph([Node("t", "trigger@1"), Node("b", "step@2")], Edge("t", "b")), extra: second)
            .Should().ContainSingle().Which.Message.Should().Contain("Node 'b':").And.Contain("why");
    }

    [TestMethod]
    public void Validate_DeprecatedMajor_Warns()
    {
        var second = new TestNode("step") { Major = 2 };
        WorkflowGraph graph = Graph([Node("t", "trigger@1"), Node("a", "step@1"), Node("b", "step@2"), Node("c", "step@1")], Edge("t", "a"), Edge("a", "b"), Edge("b", "c"));

        Validate(graph, extra: second).Should().BeEmpty();
        IReadOnlyList<WorkflowValidationError> warnings = Warnings(graph, extra: second);

        warnings.Should().Equal(
            new WorkflowValidationError("a", "Node 'a' uses 'step@1'; 'step@2' supersedes it."),
            new WorkflowValidationError("c", "Node 'c' uses 'step@1'; 'step@2' supersedes it."));
    }

    [TestMethod]
    public void Warnings_CurrentOrUnknownTypes_None()
    {
        var second = new TestNode("step") { Major = 2 };
        WorkflowGraph graph = Graph([Node("t", "trigger@1"), Node("b", "step@2"), Node("x", "mystery@1"), Node("y", "step")], Edge("t", "b"));

        Warnings(graph, extra: second).Should().BeEmpty();
    }

    [TestMethod]
    public void Warnings_InvalidArguments_Throw()
    {
        IWorkflowValidator validator = GetServices().GetRequiredService<IWorkflowValidator>();

        FluentActions.Invoking(() => validator.Warnings(null, Catalog())).Should().Throw<ArgumentNullException>();
        FluentActions.Invoking(() => validator.Warnings(Graph([Node("t", "trigger@1")]), null)).Should().Throw<ArgumentNullException>();
    }

    [TestMethod]
    public void Catalogue_SameNameAndMajorTwice_Throws()
    {
        WorkflowGraph graph = Graph([Node("t", "trigger@1")]);

        FluentActions.Invoking(() => Validate(graph, extra: new TestNode("step"))).Should().Throw<ArgumentException>().WithMessage("*'step@1' twice*");
    }

    [TestMethod]
    public void Catalogue_TypeNameWithMajorOrMajorBelowOne_Throws()
    {
        WorkflowGraph graph = Graph([Node("t", "trigger@1")]);

        FluentActions.Invoking(() => Validate(graph, extra: new TestNode("odd@1"))).Should().Throw<ArgumentException>().WithMessage("*'odd@1'*");
        FluentActions.Invoking(() => Validate(graph, extra: new TestNode("odd") { Major = 0 })).Should().Throw<ArgumentException>().WithMessage("*'odd'*major*");
    }

    [TestMethod]
    public void Validator_EdgeToUnknownNode_Rejected()
    {
        IReadOnlyList<WorkflowValidationError> errors = Validate(Graph([Node("t", "trigger@1")], Edge("t", "ghost")));

        errors.Should().ContainSingle().Which.Should().Be(new WorkflowValidationError("t", "An edge from 't' to 'ghost' names the unknown node 'ghost'."));
    }

    [TestMethod]
    public void Validator_EdgeFromUnknownNode_NamesNoNode()
    {
        IReadOnlyList<WorkflowValidationError> errors = Validate(Graph([Node("t", "trigger@1")], Edge("ghost", "t")));

        errors.Should().ContainSingle().Which.NodeId.Should().BeNull();
    }

    [TestMethod]
    public void Validator_DuplicateEdge_Rejected()
    {
        IReadOnlyList<WorkflowValidationError> errors = Validate(Graph([Node("t", "trigger@1"), Node("a", "step@1")], Edge("t", "a"), Edge("t", "a")));

        errors.Should().ContainSingle().Which.Message.Should().Contain("listed twice");
    }

    [TestMethod]
    public void Validator_EdgeIntoTrigger_Rejected()
    {
        IReadOnlyList<WorkflowValidationError> errors = Validate(Graph([Node("t", "trigger@1"), Node("a", "step@1")], Edge("t", "a"), Edge("a", "t")));

        errors.Should().Contain(error => error.NodeId == "t" && error.Message.Contains("edge into it", StringComparison.Ordinal));
    }

    [TestMethod]
    public void Validator_BranchOnPlainNode_Rejected()
    {
        IReadOnlyList<WorkflowValidationError> errors = Validate(Graph([Node("t", "trigger@1"), Node("a", "step@1")], Edge("t", "a", "true")));

        errors.Should().ContainSingle().Which.Message.Should().Contain("does not branch");
    }

    [TestMethod]
    [DataRow(null)]
    [DataRow("maybe")]
    public void Validator_BranchingNodeEdgeWithoutDeclaredBranch_Rejected(string? branch)
    {
        IReadOnlyList<WorkflowValidationError> errors = Validate(Graph([Node("t", "trigger@1"), Node("c", "if@1"), Node("a", "step@1")], Edge("t", "c"), Edge("c", "a", branch)));

        errors.Should().ContainSingle().Which.Should().Be(new WorkflowValidationError("c", "The edge from 'c' to 'a' must name one of the branches 'true', 'false'."));
    }

    [TestMethod]
    public void Validator_ConfigAgainstSchema_ReportsEachProblem()
    {
        IReadOnlyList<WorkflowValidationError> errors = Validate(Graph(
            [Node("t", "trigger@1"), Node("a", "typed@1", """{"count":"x","mode":"loud","extra":1,"list":[1,"two"]}""")],
            Edge("t", "a")));

        errors.Select(error => error.Message).Should().Equal(
            "Node 'a': config.label is required.",
            "Node 'a': config.count must be integer.",
            "Node 'a': config.mode must be one of [\"quiet\",\"normal\"].",
            "Node 'a': config.extra is not allowed.",
            "Node 'a': config.list[1] must be number.");
    }

    [TestMethod]
    public void Validator_ConfigNotAnObject_Rejected()
    {
        IReadOnlyList<WorkflowValidationError> errors = Validate(Graph([Node("t", "trigger@1"), Node("a", "typed@1", "[]")], Edge("t", "a")));

        errors.Should().ContainSingle().Which.Message.Should().Be("Node 'a': config must be object.");
    }

    [TestMethod]
    public void Validator_ValidateConfigProblem_Reported()
    {
        var picky = new TestNode("picky") { ConfigCheck = _ => ["the cron expression does not parse."] };

        IReadOnlyList<WorkflowValidationError> errors = Validate(Graph([Node("t", "trigger@1"), Node("a", "picky@1")], Edge("t", "a")), extra: picky);

        errors.Should().ContainSingle().Which.Should().Be(new WorkflowValidationError("a", "Node 'a': the cron expression does not parse."));
    }

    [TestMethod]
    public void Validator_ValidReferences_NoErrors()
    {
        IReadOnlyList<WorkflowValidationError> errors = Validate(Graph(
            [
                Node("t", "trigger@1"),
                Node("s", "source@1"),
                Node("a", "typed@1", """{"label":"{{steps.s.output.name}}, {{steps.s.output.total}}","count":"{{trigger.n}}","amount":"{{ steps.s.output.total }}","list":["{{steps.s.output.total}}"],"any":"{{steps.s.output.meta.tags.0}} {{steps.s.output.free.deep.path}}"}"""),
            ],
            Edge("t", "s"), Edge("s", "a")));

        errors.Should().BeEmpty();
    }

    [TestMethod]
    public void Validator_UnknownReference_Rejected()
    {
        IReadOnlyList<WorkflowValidationError> errors = Validate(Graph(
            [Node("t", "trigger@1"), Node("s", "source@1"), Node("a", "step@1", """{"x":"{{steps.s.output.missing}}","y":"{{steps.nobody.output}}"}""")],
            Edge("t", "s"), Edge("s", "a")));

        errors.Select(error => error.Message).Should().Equal(
            "Node 'a': config.x references 'missing' in {{steps.s.output.missing}}, which the output of 's' does not have.",
            "Node 'a': config.y references the unknown node 'nobody' in {{steps.nobody.output}}.");
    }

    [TestMethod]
    public void Validator_PathIntoScalar_Rejected()
    {
        IReadOnlyList<WorkflowValidationError> errors = Validate(Graph(
            [Node("t", "trigger@1"), Node("s", "source@1"), Node("a", "step@1", """{"x":"{{steps.s.output.name.first}}"}""")],
            Edge("t", "s"), Edge("s", "a")));

        errors.Should().ContainSingle().Which.Message.Should().Contain("'first'");
    }

    [TestMethod]
    public void Validator_ForwardReference_Rejected()
    {
        IReadOnlyList<WorkflowValidationError> errors = Validate(Graph(
            [Node("t", "trigger@1"), Node("a", "step@1", """{"x":"{{steps.s.output.total}}"}"""), Node("s", "source@1")],
            Edge("t", "a"), Edge("a", "s")));

        errors.Should().ContainSingle().Which.Message.Should().Be("Node 'a': config.x references 's' in {{steps.s.output.total}}, which does not run before this node.");
    }

    [TestMethod]
    public void Validator_ReferenceToOneBranch_Rejected()
    {
        IReadOnlyList<WorkflowValidationError> errors = Validate(Graph(
            [Node("t", "trigger@1"), Node("c", "if@1"), Node("s", "source@1"), Node("join", "step@1", """{"x":"{{steps.s.output.total}}","y":"{{steps.c.output}}"}""")],
            Edge("t", "c"), Edge("c", "s", "true"), Edge("c", "join", "false"), Edge("s", "join")));

        errors.Should().ContainSingle().Which.Message.Should().Contain("does not run on every path");
    }

    [TestMethod]
    public void Validator_OwnOutputReference_Rejected()
    {
        IReadOnlyList<WorkflowValidationError> errors = Validate(Graph(
            [Node("t", "trigger@1", """{"x":"{{trigger.a}}"}"""), Node("a", "step@1", """{"x":"{{steps.a.output}}"}""")],
            Edge("t", "a")));

        errors.Select(error => error.NodeId).Should().Equal("t", "a");
        errors.Should().OnlyContain(error => error.Message.Contains("own output", StringComparison.Ordinal));
    }

    [TestMethod]
    public void Validator_TypeMismatch_Rejected()
    {
        IReadOnlyList<WorkflowValidationError> errors = Validate(Graph(
            [Node("t", "trigger@1"), Node("s", "source@1"), Node("a", "typed@1", """{"label":"x","count":"{{steps.s.output.name}}","amount":"{{steps.s.output.meta}}"}""")],
            Edge("t", "s"), Edge("s", "a")));

        errors.Select(error => error.Message).Should().Equal(
            "Node 'a': config.count expects integer, but {{steps.s.output.name}} is string.",
            "Node 'a': config.amount expects number, but {{steps.s.output.meta}} is object.");
    }

    [TestMethod]
    public void Validator_NumberIntoInteger_Rejected()
    {
        IReadOnlyList<WorkflowValidationError> errors = Validate(Graph(
            [Node("t", "trigger@1"), Node("s", "source@1"), Node("a", "typed@1", """{"label":"x","count":"{{steps.s.output.total}}"}""")],
            Edge("t", "s"), Edge("s", "a")));

        errors.Should().ContainSingle().Which.Message.Should().Contain("expects integer, but {{steps.s.output.total}} is number");
    }

    [TestMethod]
    public void Validator_ObjectIntoText_Rejected()
    {
        IReadOnlyList<WorkflowValidationError> errors = Validate(Graph(
            [Node("t", "trigger@1"), Node("s", "source@1"), Node("a", "step@1", """{"x":"meta: {{steps.s.output.meta}}"}""")],
            Edge("t", "s"), Edge("s", "a")));

        errors.Should().ContainSingle().Which.Message.Should().Contain("into text");
    }

    [TestMethod]
    public void Validator_TextWhereNumberExpected_Rejected()
    {
        IReadOnlyList<WorkflowValidationError> errors = Validate(Graph(
            [Node("t", "trigger@1"), Node("s", "source@1"), Node("a", "typed@1", """{"label":"x","amount":"about {{steps.s.output.total}}"}""")],
            Edge("t", "s"), Edge("s", "a")));

        errors.Should().ContainSingle().Which.Message.Should().Be("Node 'a': config.amount must be number, not text with references.");
    }

    [TestMethod]
    [DataRow("{{steps.s.output.name")]
    [DataRow("{{ step.s }}")]
    [DataRow("a }} b")]
    public void Validator_MalformedReference_Rejected(string text)
    {
        string config = JsonSerializer.Serialize(new { x = text });

        IReadOnlyList<WorkflowValidationError> errors = Validate(Graph([Node("t", "trigger@1"), Node("s", "source@1"), Node("a", "step@1", config)], Edge("t", "s"), Edge("s", "a")));

        errors.Should().ContainSingle().Which.Message.Should().Contain("not a reference");
    }

    [TestMethod]
    public void Validator_ReferenceToInvalidNode_OnlyThatNodeReported()
    {
        IReadOnlyList<WorkflowValidationError> errors = Validate(Graph(
            [Node("t", "trigger@1"), Node("bad", "typed@1", "{}"), Node("a", "step@1", """{"x":"{{steps.bad.output.whatever}}"}""")],
            Edge("t", "bad"), Edge("bad", "a")));

        errors.Should().ContainSingle().Which.NodeId.Should().Be("bad");
    }

    [TestMethod]
    public void Validator_DuplicateCatalogType_Throws()
    {
        IWorkflowValidator validator = GetServices().GetRequiredService<IWorkflowValidator>();

        Action act = () => validator.Validate(Graph([Node("t", "trigger@1")]), [new TestNode("trigger"), new TestNode("trigger")], 50);

        act.Should().Throw<ArgumentException>().WithMessage("*twice*");
    }

    [TestMethod]
    public void Validator_ZeroMaxNodes_Throws()
    {
        IWorkflowValidator validator = GetServices().GetRequiredService<IWorkflowValidator>();

        Action act = () => validator.Validate(Graph([Node("t", "trigger@1")]), Catalog(), 0);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    private static IReadOnlyCollection<INodeType> Catalog(INodeType? extra = null)
    {
        List<INodeType> types =
        [
            new TestNode("trigger", outputSchema: """{"type":"object","properties":{"n":{"type":"integer"},"a":{}}}""") { IsTrigger = true },
            new TestNode("step"),
            new TestNode("source", outputSchema: SourceOutput),
            new TestNode("if") { Branches = ["true", "false"] },
            new TestNode(
                "typed",
                """{"type":"object","required":["label"],"additionalProperties":false,"properties":{"label":{"type":"string"},"count":{"type":"integer"},"amount":{"type":"number"},"mode":{"enum":["quiet","normal"]},"list":{"type":"array","items":{"type":"number"}},"any":{}}}"""),
        ];
        if (extra is not null)
        {
            types.Add(extra);
        }

        return types;
    }

    private static JsonElement Json(string json)
    {
        using JsonDocument document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }

    private static NodeDefinition Node(string id, string type, string config = "{}") => new(id, type, Json(config));

    private static EdgeDefinition Edge(string from, string to, string? branch = null) => new(from, to, branch);

    private static WorkflowGraph Graph(NodeDefinition[] nodes, params EdgeDefinition[] edges) => new(nodes, edges);

    private IReadOnlyList<WorkflowValidationError> Validate(WorkflowGraph graph, int maxNodes = 50, INodeType? extra = null, INodeType? more = null) =>
        GetServices().GetRequiredService<IWorkflowValidator>().Validate(graph, more is null ? Catalog(extra) : [.. Catalog(extra), more], maxNodes);

    private IReadOnlyList<WorkflowValidationError> Warnings(WorkflowGraph graph, INodeType? extra = null) =>
        GetServices().GetRequiredService<IWorkflowValidator>().Warnings(graph, Catalog(extra));
}
