using System.Text.Json;
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Nordstein.Core.Testing;

namespace Nordstein.Core.Workflows.Tests;

/// <summary>
/// The kernel's <c>for-each</c> node type on its own: its shape and limits, the configuration it refuses, what it
/// collects, and that it never executes on its own.
/// </summary>
[TestClass]
public sealed class ForEachNodeTypeTests : BaseTest<Module>
{
    [TestMethod]
    public void Shape_IteratesItemsWithBodyAndDone()
    {
        IForEachNodeType node = ForEach();

        (node.Type, node.Major, node.IsTrigger, node.HasSideEffects, node.ReadOnly, node.ItemsProperty).Should().Be(("for-each", 1, false, false, false, "items"));
        node.Type.Should().Be(IForEachNodeType.TypeName);
        node.Should().BeAssignableTo<IIteratingNodeType>();
        node.Branches.Should().Equal(LoopBranches.Body, LoopBranches.Done);
        node.InputSchema.GetProperty("required").EnumerateArray().Select(name => name.GetString()).Should().Equal("items");
        node.InputSchema.GetProperty("properties").EnumerateObject().Select(property => property.Name).Should().Equal("items", "maxItems");
        JsonElement output = node.GetOutputSchema(Json("{}"));
        output.GetProperty("properties").EnumerateObject().Select(property => (property.Name, property.Value.GetProperty("type").GetString())).Should().Equal(("count", "integer"), ("results", "array"));
        output.GetProperty("required").EnumerateArray().Select(name => name.GetString()).Should().Equal("count", "results");
    }

    [TestMethod]
    public void Resolve_Twice_IsOneSingleton()
    {
        IServiceProvider services = GetServices();

        services.GetRequiredService<IForEachNodeType>().Should().BeSameAs(services.GetRequiredService<IForEachNodeType>());
    }

    [TestMethod]
    [DataRow("""{"items":"{{trigger.rows}}"}""", IForEachNodeType.DefaultMaxItems)]
    [DataRow("""{"items":"{{trigger.rows}}","maxItems":1}""", 1)]
    [DataRow("""{"items":"{{trigger.rows}}","maxItems":5}""", 5)]
    [DataRow("""{"items":[1,2],"maxItems":100}""", IForEachNodeType.MaxItemsLimit)]
    public void MaxItems_ValidConfig_IsTheConfiguredOrTheDefaultLimit(string config, int expected)
    {
        ForEach().MaxItems(Json(config)).Should().Be(expected);
    }

    [TestMethod]
    [DataRow("""{"items":"{{trigger.rows}}","maxItems":0}""")]
    [DataRow("""{"items":"{{trigger.rows}}","maxItems":101}""")]
    [DataRow("""{"items":"{{trigger.rows}}","maxItems":"{{trigger.max}}"}""")]
    [DataRow("""null""")]
    public void MaxItems_ConfigThatFailsValidation_Throws(string config)
    {
        Action act = () => ForEach().MaxItems(Json(config));

        act.Should().Throw<ArgumentException>().WithParameterName("config");
    }

    [TestMethod]
    public void ValidateConfig_ItemsReferenceOrLiteralWithinTheLimit_NoProblem()
    {
        IForEachNodeType node = ForEach();

        node.ValidateConfig(Json("""{"items":"{{trigger.rows}}"}""")).Should().BeEmpty();
        node.ValidateConfig(Json("""{"items":[1,2,3],"maxItems":100}""")).Should().BeEmpty();
        node.ValidateConfig(Json("""{"items":[],"maxItems":1}""")).Should().BeEmpty();
        node.ValidateConfig(Json("null")).Should().BeEmpty("the input schema refuses a configuration that is not an object");
    }

    [TestMethod]
    [DataRow("""{"items":"{{trigger.rows}}","maxItems":0}""")]
    [DataRow("""{"items":"{{trigger.rows}}","maxItems":101}""")]
    [DataRow("""{"items":"{{trigger.rows}}","maxItems":-1}""")]
    [DataRow("""{"items":"{{trigger.rows}}","maxItems":5.0}""")]
    [DataRow("""{"items":"{{trigger.rows}}","maxItems":99999999999}""")]
    [DataRow("""{"items":"{{trigger.rows}}","maxItems":"{{trigger.max}}"}""")]
    public void ValidateConfig_MaxItemsNotAWholeNumberWithinTheLimit_Refused(string config)
    {
        ForEach().ValidateConfig(Json(config)).Should().Equal("config.maxItems is a whole number from 1 to 100, not a data reference.");
    }

    [TestMethod]
    public void Collect_CountsTheResultsInOrder()
    {
        JsonElement output = ForEach().Collect(Context(), [Json("""{"a":1}"""), Json("null"), Json("""[2]""")]);

        output.GetProperty("count").GetInt32().Should().Be(3);
        output.GetProperty("results").GetRawText().Should().Be("""[{"a":1},null,[2]]""");
    }

    [TestMethod]
    public void Collect_UndefinedResult_IsNull()
    {
        JsonElement output = ForEach().Collect(Context(), [default, Json("1")]);

        output.GetProperty("results").GetRawText().Should().Be("[null,1]");
    }

    [TestMethod]
    public void Collect_NoResults_IsAnEmptyCollection()
    {
        JsonElement output = ForEach().Collect(Context(), []);

        output.GetRawText().Should().Be("""{"count":0,"results":[]}""");
    }

    // WHY: the test passes null where the contract forbids it, to prove the runtime guard; the `!` operator is banned.
#pragma warning disable CS8625
    [TestMethod]
    public void Collect_NullArguments_Throw()
    {
        FluentActions.Invoking(() => ForEach().Collect(Context(), null)).Should().Throw<ArgumentNullException>().WithParameterName("results");
        FluentActions.Invoking(() => ForEach().Collect(null, [])).Should().Throw<ArgumentNullException>().WithParameterName("context");
    }
#pragma warning restore CS8625

    [TestMethod]
    public async Task Execute_OnItsOwn_Fails()
    {
        NodeResult result = await ForEach().ExecuteAsync(Context(), CancellationToken);

        result.Should().Be(new NodeResult.Failed("The kernel runs a for-each node's loop; the node never executes on its own."));
    }

    [TestMethod]
    public void Validator_NestedForEach_Refused()
    {
        IServiceProvider services = GetServices();
        IForEachNodeType forEach = services.GetRequiredService<IForEachNodeType>();
        INodeType[] catalog = [new TestNode("start") { IsTrigger = true, }, forEach, new TestNode("step")];
        WorkflowGraph graph = new(
            [
                Node("t", "start@1"),
                Node("outer", "for-each@1", """{"items":"{{trigger.rows}}"}"""),
                Node("inner", "for-each@1", """{"items":"{{item.lines}}"}"""),
                Node("leaf", "step@1", """{"x":"{{item}}"}"""),
            ],
            [new EdgeDefinition("t", "outer"), new EdgeDefinition("outer", "inner", LoopBranches.Body), new EdgeDefinition("inner", "leaf", LoopBranches.Body)]);

        IReadOnlyList<WorkflowValidationError> errors = services.GetRequiredService<IWorkflowValidator>().Validate(graph, catalog, 10);

        errors.Should().Contain(error => error.NodeId == "inner" && error.Message.Contains("loops do not nest", StringComparison.Ordinal));
    }

    private IForEachNodeType ForEach() => GetServices().GetRequiredService<IForEachNodeType>();

    private static NodeContext Context() => new("each", "for-each@1", Json("{}"), Json("{}"), false);

    private static NodeDefinition Node(string id, string type, string config = "{}") => new(id, type, Json(config));

    private static JsonElement Json(string text)
    {
        using JsonDocument document = JsonDocument.Parse(text);
        return document.RootElement.Clone();
    }
}
