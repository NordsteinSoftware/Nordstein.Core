using System.Text.Json;
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Nordstein.Core.Testing;

namespace Nordstein.Core.Workflows.Tests;

/// <summary>
/// The validator on the edges of the kernel's JSON Schema subset and of its graph analysis: keywords of an unexpected
/// form, the <c>null</c> and <c>integer</c> types, numbers beyond a <see cref="decimal"/>, boolean schemas, indexes beyond
/// an <see cref="int"/>, and cycles with a diamond behind them.
/// </summary>
[TestClass]
public sealed class SchemaSubsetTests : BaseTest<Module>
{
    private const string LenientSchema = """
        {"type":"object","properties":{
          "a":{"type":["string",5,"null"]},
          "b":{"type":7},
          "c":true,
          "d":{"type":"null"},
          "e":{"type":"integer"},
          "f":{"type":"widget"},
          "g":{"type":"array","items":[{"type":"string"}]},
          "h":{"type":"object","required":[5,"x"],"properties":[]}}}
        """;

    [TestMethod]
    public void Validate_KeywordsOfAnUnexpectedForm_AreIgnoredNotFatal()
    {
        const string config = """{"a":null,"b":"anything","c":{"deep":"{{trigger.x}}"},"d":null,"e":123456789012345678901234567890123,"f":1,"g":[1,true],"h":{"x":1,"y":2}}""";

        Validate(LenientSchema, config).Should().BeEmpty();
    }

    [TestMethod]
    [DataRow("""{"d":1}""", "Node 'n': config.d must be null.")]
    [DataRow("""{"e":1e400}""", "Node 'n': config.e must be integer.")]
    [DataRow("""{"e":2.5}""", "Node 'n': config.e must be integer.")]
    [DataRow("""{"h":{}}""", "Node 'n': config.h.x is required.")]
    public void Validate_NullAndIntegerTypesAndRequiredNames_Checked(string config, string message)
    {
        Validate(LenientSchema, config).Should().Equal(new WorkflowValidationError("n", message));
    }

    [TestMethod]
    public void Validate_ReferenceThroughABooleanSchema_AllowsAnyChild()
    {
        var source = new TestNode("source", outputSchema: """{"type":"object","properties":{"x":true}}""");

        Validate("{}", """{"v":"{{steps.s.output.x.y.z}}"}""", source).Should().BeEmpty();
    }

    [TestMethod]
    public void Validate_IndexBeyondAnInt_IsNoPathOfTheOutput()
    {
        var source = new TestNode("source", outputSchema: """{"type":"object","properties":{"items":{"type":"array","items":{"type":"string"}}}}""");

        IReadOnlyList<WorkflowValidationError> errors = Validate("{}", """{"v":"{{steps.s.output.items.99999999999}}"}""", source);

        errors.Should().ContainSingle().Which.NodeId.Should().Be("n");
    }

    [TestMethod]
    public void Validate_CycleWithADiamondBehindIt_NamesOnlyTheCycle()
    {
        IServiceProvider services = GetServices();
        WorkflowGraph graph = new(
            [Node("t", "trigger@1"), Node("a", "step@1"), Node("b", "step@1"), Node("x", "step@1"), Node("p", "step@1"), Node("q", "step@1"), Node("r", "step@1")],
            [Edge("t", "a"), Edge("a", "b"), Edge("b", "a"), Edge("a", "x"), Edge("x", "p"), Edge("x", "q"), Edge("p", "r"), Edge("q", "r")]);

        IReadOnlyList<WorkflowValidationError> errors = services.GetRequiredService<IWorkflowValidator>().Validate(graph, [Trigger(), new TestNode("step")], 10);

        errors.Where(error => error.Message.Contains("is on a cycle", StringComparison.Ordinal)).Select(error => error.NodeId).Should().BeEquivalentTo(["a", "b"]);
    }

    [TestMethod]
    public void Validate_EdgeFromAnUnreachableNode_ReportsItAndStillChecksTheRest()
    {
        IServiceProvider services = GetServices();
        WorkflowGraph graph = new(
            [Node("t", "trigger@1"), Node("a", "step@1"), Node("stray", "step@1"), Node("b", "step@1", """{"v":"{{steps.a.output.x}}"}""")],
            [Edge("t", "a"), Edge("a", "b"), Edge("stray", "b")]);

        IReadOnlyList<WorkflowValidationError> errors = services.GetRequiredService<IWorkflowValidator>().Validate(graph, [Trigger(), new TestNode("step")], 10);

        errors.Should().Equal(new WorkflowValidationError("stray", "Node 'stray' cannot be reached from the trigger."));
    }

    private IReadOnlyList<WorkflowValidationError> Validate(string inputSchema, string config, TestNode? source = null)
    {
        IServiceProvider services = GetServices();
        var subject = new TestNode("subject", inputSchema);
        List<NodeDefinition> nodes = [Node("t", "trigger@1")];
        List<EdgeDefinition> edges = [];
        List<INodeType> catalog = [Trigger(), subject];
        if (source is not null)
        {
            nodes.Add(Node("s", "source@1"));
            edges.Add(Edge("t", "s"));
            catalog.Add(source);
        }

        nodes.Add(Node("n", "subject@1", config));
        edges.Add(Edge(source is null ? "t" : "s", "n"));
        return services.GetRequiredService<IWorkflowValidator>().Validate(new WorkflowGraph(nodes, edges), catalog, 10);
    }

    private static TestNode Trigger() => new("trigger") { IsTrigger = true };

    private static NodeDefinition Node(string id, string type, string config = "{}") => new(id, type, Json(config));

    private static EdgeDefinition Edge(string from, string to) => new(from, to);

    private static JsonElement Json(string text)
    {
        using JsonDocument document = JsonDocument.Parse(text);
        return document.RootElement.Clone();
    }
}
