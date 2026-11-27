using System.Globalization;
using System.Text.Json;
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Nordstein.Core.Testing;

namespace Nordstein.Core.Workflows.Tests;

/// <summary>
/// The kernel's <c>condition</c> node type on its own: its shape, every operator's comparison rules, <c>all</c> against
/// <c>any</c>, the configuration it refuses, run-time data of the wrong shape, and cancellation.
/// </summary>
[TestClass]
public sealed class ConditionNodeTypeTests : BaseTest<Module>
{
    [TestMethod]
    public void Shape_IsAReadOnlyBranchingNodeWithABooleanOutput()
    {
        IConditionNodeType node = Condition();

        (node.Type, node.Major, node.IsTrigger, node.HasSideEffects, node.ReadOnly).Should().Be(("condition", 1, false, false, true));
        node.Type.Should().Be(IConditionNodeType.TypeName);
        node.Branches.Should().Equal(IConditionNodeType.True, IConditionNodeType.False);
        node.InputSchema.GetProperty("properties").EnumerateObject().Select(property => property.Name).Should().Equal("all", "any");
        node.InputSchema.GetProperty("properties").GetProperty("all").GetProperty("items").GetProperty("properties").GetProperty("op").GetProperty("enum")
            .EnumerateArray().Select(op => op.GetString()).Should().Equal("eq", "neq", "contains", "gt", "lt", "in", "matches");
        JsonElement output = node.GetOutputSchema(Json("{}"));
        output.GetProperty("properties").GetProperty("result").GetProperty("type").GetString().Should().Be("boolean");
        output.GetProperty("required").EnumerateArray().Select(name => name.GetString()).Should().Equal("result");
    }

    [TestMethod]
    public void Resolve_Twice_IsOneSingleton()
    {
        IServiceProvider services = GetServices();

        services.GetRequiredService<IConditionNodeType>().Should().BeSameAs(services.GetRequiredService<IConditionNodeType>());
    }

    [TestMethod]
    [DataRow("""{"left":"Order","op":"eq","right":"order"}""", true)]
    [DataRow("""{"left":"Order","op":"eq","right":"Quote"}""", false)]
    [DataRow("""{"left":5,"op":"eq","right":5.0}""", true)]
    [DataRow("""{"left":"5","op":"eq","right":5}""", true)]
    [DataRow("""{"left":"0.10","op":"eq","right":0.1}""", true)]
    [DataRow("""{"left":true,"op":"eq","right":"true"}""", true)]
    [DataRow("""{"left":false,"op":"eq","right":"TRUE"}""", false)]
    [DataRow("""{"left":null,"op":"eq","right":""}""", true)]
    [DataRow("""{"left":null,"op":"eq","right":null}""", true)]
    [DataRow("""{"left":{"a":1},"op":"eq","right":{"a":1.0}}""", true)]
    [DataRow("""{"left":{"a":1},"op":"eq","right":{"A":1}}""", false)]
    [DataRow("""{"left":[1,2],"op":"eq","right":[1,2]}""", true)]
    [DataRow("""{"left":[1,2],"op":"eq","right":"[1,2]"}""", false)]
    [DataRow("""{"left":5,"op":"eq","right":"five"}""", false)]
    [DataRow("""{"left":"x","op":"eq","right":{"a":1}}""", false)]
    [DataRow("""{"left":"a","op":"neq","right":"b"}""", true)]
    [DataRow("""{"left":"a","op":"neq","right":"A"}""", false)]
    [DataRow("""{"left":"Order No. 4","op":"contains","right":"no."}""", true)]
    [DataRow("""{"left":"Order","op":"contains","right":"Quote"}""", false)]
    [DataRow("""{"left":12345,"op":"contains","right":"34"}""", true)]
    [DataRow("""{"left":["red","Green"],"op":"contains","right":"green"}""", true)]
    [DataRow("""{"left":[1,2],"op":"contains","right":"2.0"}""", true)]
    [DataRow("""{"left":["red,green"],"op":"contains","right":"green"}""", false)]
    [DataRow("""{"left":[],"op":"contains","right":""}""", false)]
    [DataRow("""{"left":10,"op":"gt","right":9}""", true)]
    [DataRow("""{"left":9,"op":"gt","right":10}""", false)]
    [DataRow("""{"left":10,"op":"gt","right":10}""", false)]
    [DataRow("""{"left":"10","op":"gt","right":"9"}""", true)]
    [DataRow("""{"left":"ten","op":"gt","right":9}""", false)]
    [DataRow("""{"left":10,"op":"gt","right":"nine"}""", false)]
    [DataRow("""{"left":1.5,"op":"lt","right":2}""", true)]
    [DataRow("""{"left":2,"op":"lt","right":1.5}""", false)]
    [DataRow("""{"left":"a","op":"lt","right":2}""", false)]
    [DataRow("""{"left":1,"op":"lt","right":"b"}""", false)]
    [DataRow("""{"left":null,"op":"lt","right":1}""", false)]
    [DataRow("""{"left":"b","op":"in","right":["a","B"]}""", true)]
    [DataRow("""{"left":3,"op":"in","right":["3.0"]}""", true)]
    [DataRow("""{"left":"c","op":"in","right":["a","b"]}""", false)]
    [DataRow("""{"left":"c","op":"in","right":[]}""", false)]
    [DataRow("""{"left":"A-2026-001","op":"matches","right":"^A-\\d{4}-\\d+$"}""", true)]
    [DataRow("""{"left":"Order","op":"matches","right":"^\\d+$"}""", false)]
    [DataRow("""{"left":"Order","op":"matches","right":"order"}""", false)]
    [DataRow("""{"left":7,"op":"matches","right":"^7$"}""", true)]
    [DataRow("""{"left":99999999999999999999999999999,"op":"gt","right":1}""", false)]
    [DataRow("""{"left":1e400,"op":"eq","right":"1e400"}""", true)]
    [DataRow("""{"left":1e400,"op":"gt","right":1}""", false)]
    [DataRow("""{"left":"1,5","op":"lt","right":2}""", false)]
    [DataRow("""{"left":"1,000","op":"eq","right":1000}""", false)]
    [DataRow("""{"left":" -2.5e1 ","op":"lt","right":-20}""", true)]
    [DataRow("""{"left":"NaN","op":"eq","right":"nan"}""", true)]
    [DataRow("""{"left":"Infinity","op":"gt","right":1}""", false)]
    public async Task Execute_OneRule_TakesTheBranchOfItsComparison(string rule, bool expected)
    {
        NodeResult.Completed result = await CompletedAsync("{\"all\":[" + rule + "]}");

        result.Branch.Should().Be(expected ? "true" : "false");
        result.Output.GetProperty("result").GetBoolean().Should().Be(expected);
    }

    [TestMethod]
    public async Task Execute_UnderATurkishCulture_ComparesCultureIndependently()
    {
        CultureInfo culture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("tr-TR");

            (await CompletedAsync("""{"all":[{"left":"FILE","op":"eq","right":"file"}]}""")).Branch.Should().Be("true");
            (await CompletedAsync("""{"all":[{"left":"1.5","op":"gt","right":"1.25"}]}""")).Branch.Should().Be("true");
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("de-DE");
            (await CompletedAsync("""{"all":[{"left":"1,5","op":"gt","right":1}]}""")).Branch.Should().Be("false");
        }
        finally
        {
            CultureInfo.CurrentCulture = culture;
        }
    }

    [TestMethod]
    public async Task Execute_All_NeedsEveryRule()
    {
        NodeResult.Completed allTrue = await CompletedAsync("""{"all":[{"left":1,"op":"eq","right":1},{"left":"a","op":"eq","right":"a"}]}""");
        NodeResult.Completed oneFalse = await CompletedAsync("""{"all":[{"left":1,"op":"eq","right":1},{"left":"a","op":"eq","right":"b"}]}""");

        allTrue.Branch.Should().Be("true");
        oneFalse.Branch.Should().Be("false");
    }

    [TestMethod]
    public async Task Execute_Any_NeedsOneRule()
    {
        NodeResult.Completed oneTrue = await CompletedAsync("""{"any":[{"left":1,"op":"eq","right":2},{"left":"a","op":"eq","right":"a"}]}""");
        NodeResult.Completed noneTrue = await CompletedAsync("""{"any":[{"left":1,"op":"eq","right":2},{"left":"a","op":"eq","right":"b"}]}""");

        oneTrue.Branch.Should().Be("true");
        noneTrue.Branch.Should().Be("false");
        noneTrue.Output.GetProperty("result").GetBoolean().Should().BeFalse();
    }

    [TestMethod]
    public async Task Execute_AllDecidedByAnEarlierRule_NeverEvaluatesTheLaterOne()
    {
        // The later rule's pattern does not compile; evaluation stops at the first false rule, so it never fails the step.
        NodeResult.Completed result = await CompletedAsync("""{"all":[{"left":1,"op":"eq","right":2},{"left":"a","op":"matches","right":"(["}]}""");

        result.Branch.Should().Be("false");
    }

    [TestMethod]
    public async Task Execute_AnyDecidedByAnEarlierRule_NeverEvaluatesTheLaterOne()
    {
        NodeResult.Completed result = await CompletedAsync("""{"any":[{"left":1,"op":"eq","right":1},{"left":"a","op":"matches","right":"(["}]}""");

        result.Branch.Should().Be("true");
    }

    [TestMethod]
    [DataRow("""{"all":[{"left":"a","op":"matches","right":"(["}]}""", "config.all[0].right is not a regular expression.")]
    [DataRow("""{"any":[{"left":1,"op":"eq","right":2},{"left":"a","op":"matches","right":"(["}]}""", "config.any[1].right is not a regular expression.")]
    [DataRow("""{"all":[{"left":"a","op":"between","right":"b"}]}""", "config.all[0].op is one of eq, neq, contains, gt, lt, in, matches.")]
    [DataRow("""{"all":[{"left":"a","op":5,"right":"b"}]}""", "config.all[0].op is one of eq, neq, contains, gt, lt, in, matches.")]
    [DataRow("""{"all":[{"op":"eq","right":"b"}]}""", "config.all[0] is a rule with left, op and right.")]
    [DataRow("""{"all":[{"left":"a","op":"eq"}]}""", "config.all[0] is a rule with left, op and right.")]
    [DataRow("""{"all":["{{trigger.rule}}"]}""", "config.all[0] is a rule with left, op and right.")]
    [DataRow("""{"all":null}""", "config.all is a list of rules.")]
    [DataRow("""{"all":[]}""", "config.all has no rule.")]
    [DataRow("""{"all":[],"any":[]}""", "config holds exactly one of all and any.")]
    [DataRow("""{}""", "config holds exactly one of all and any.")]
    [DataRow("""null""", "config holds exactly one of all and any.")]
    public async Task Execute_InputOfTheWrongShape_FailsTheStepNamingThePlace(string input, string reason)
    {
        NodeResult result = await ExecuteAsync(input);

        result.Should().Be(new NodeResult.Failed(reason));
    }

    [TestMethod]
    public async Task Execute_PatternThatTimesOut_FailsTheStep()
    {
        string left = new string('a', 40) + "!";
        string input = JsonSerializer.Serialize(new { all = new[] { new { left, op = "matches", right = "^(a+)+$" } } });

        NodeResult result = await ExecuteAsync(input);

        result.Should().Be(new NodeResult.Failed("config.all[0].right took longer than 1 second to match."));
    }

    [TestMethod]
    public async Task Execute_CancelledToken_IsCancelledWithoutEvaluating()
    {
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        Func<Task> act = () => Condition().ExecuteAsync(Context("""{"all":[{"left":1,"op":"eq","right":1}]}"""), cancellation.Token);

        (await act.Should().ThrowAsync<OperationCanceledException>()).Which.CancellationToken.Should().Be(cancellation.Token);
    }

    [TestMethod]
    public void ValidateConfig_ExactlyOneOfAllAndAny_NoProblem()
    {
        IConditionNodeType node = Condition();

        node.ValidateConfig(Json("""{"all":[{"left":"a","op":"eq","right":"a"}]}""")).Should().BeEmpty();
        node.ValidateConfig(Json("""{"any":[{"left":"{{trigger.a}}","op":"eq","right":"x {{trigger.b}}"}]}""")).Should().BeEmpty();
    }

    [TestMethod]
    [DataRow("{}")]
    [DataRow("""{"all":[{"left":1,"op":"eq","right":1}],"any":[{"left":1,"op":"eq","right":1}]}""")]
    public void ValidateConfig_NeitherOrBoth_Refused(string config)
    {
        Condition().ValidateConfig(Json(config)).Should().Equal("config holds exactly one of all and any.");
    }

    [TestMethod]
    [DataRow("""{"any":[]}""", "config.any has no rule.")]
    [DataRow("""{"all":"{{trigger.rules}}"}""", "config.all is a list of rules, not a data reference.")]
    [DataRow("""{"any":["{{trigger.rule}}"]}""", "config.any[0] is a rule, not a data reference.")]
    [DataRow("""{"all":[{"left":"a","op":"{{trigger.op}}","right":"a"}]}""", "config.all[0].op is one of eq, neq, contains, gt, lt, in, matches, not a data reference.")]
    [DataRow("""{"all":[{"left":"a","op":"e{{trigger.op}}","right":"a"}]}""", "config.all[0].op is one of eq, neq, contains, gt, lt, in, matches, not a data reference.")]
    public void ValidateConfig_LiteralPartsGivenAsReferences_Refused(string config, string problem)
    {
        Condition().ValidateConfig(Json(config)).Should().Equal(problem);
    }

    [TestMethod]
    public void ValidateConfig_EveryOperator_Accepted()
    {
        const string config = """
            {"all":[{"left":1,"op":"eq","right":1},{"left":1,"op":"neq","right":2},{"left":"ab","op":"contains","right":"a"},
                    {"left":2,"op":"gt","right":1},{"left":1,"op":"lt","right":2},{"left":1,"op":"in","right":[1]},{"left":"a","op":"matches","right":"a"}]}
            """;

        Condition().ValidateConfig(Json(config)).Should().BeEmpty();
    }

    [TestMethod]
    [DataRow("""{"all":[{"op":"eq","right":1}]}""", "config.all[0] is a rule with left, op and right.")]
    [DataRow("""{"all":[{"left":1,"right":1}]}""", "config.all[0] is a rule with left, op and right.")]
    [DataRow("""{"all":[{"left":1,"op":"eq"}]}""", "config.all[0] is a rule with left, op and right.")]
    [DataRow("""{"all":[{"left":1,"op":5,"right":1}]}""", "config.all[0].op is one of eq, neq, contains, gt, lt, in, matches.")]
    [DataRow("""{"all":[{"left":1,"op":"between","right":1}]}""", "config.all[0].op is one of eq, neq, contains, gt, lt, in, matches.")]
    [DataRow("""null""", "config holds exactly one of all and any.")]
    public void ValidateConfig_ShapesTheInputSchemaRefuses_AreRefusedToo(string config, string problem)
    {
        // The validator checks the input schema first; called on its own, ValidateConfig still never throws.
        Condition().ValidateConfig(Json(config)).Should().Equal(problem);
    }

    [TestMethod]
    public void ValidateConfig_BadPatternsAndNonArrayIn_NamedByPosition()
    {
        string config = """
            {"all":[
              {"left":"a","op":"eq","right":"a"},
              {"left":"a","op":"matches","right":"(["},
              {"left":"a","op":"matches","right":5},
              {"left":"a","op":"in","right":"abc"},
              {"left":"a","op":"in","right":"x{{trigger.list}}"}]}
            """;

        Condition().ValidateConfig(Json(config)).Should().Equal(
            "config.all[1].right is not a regular expression.",
            "config.all[2].right is not a regular expression.",
            "config.all[3].right is not an array.",
            "config.all[4].right is not an array.");
    }

    [TestMethod]
    public void ValidateConfig_ReferencesAndValidPatterns_Accepted()
    {
        string config = """
            {"any":[
              {"left":"a","op":"matches","right":"{{trigger.pattern}}"},
              {"left":"a","op":"matches","right":"^{{trigger.prefix}}-\\d+$"},
              {"left":"a","op":"in","right":"{{trigger.list}}"},
              {"left":"a","op":"matches","right":"^a+$"},
              {"left":"a","op":"in","right":["a"]}]}
            """;

        Condition().ValidateConfig(Json(config)).Should().BeEmpty();
    }

    [TestMethod]
    public void Validator_ConditionInAGraph_ChecksItsConfigurationAndAcceptsAReadOnlyGraph()
    {
        IServiceProvider services = GetServices();
        var trigger = new TestNode("ask") { IsTrigger = true, ReadOnly = true };
        INodeType[] catalog = [trigger, services.GetRequiredService<IConditionNodeType>(), new TestNode("answer") { ReadOnly = true }];
        WorkflowGraph valid = new(
            [Node("t", "ask@1"), Node("c", "condition@1", """{"all":[{"left":"{{trigger.total}}","op":"gt","right":100}]}"""), Node("yes", "answer@1"), Node("no", "answer@1")],
            [new EdgeDefinition("t", "c"), new EdgeDefinition("c", "yes", "true"), new EdgeDefinition("c", "no", "false")]);
        WorkflowGraph invalid = new(
            [Node("t", "ask@1"), Node("c", "condition@1", """{"all":[{"left":1,"op":"like","right":1}],"none":[]}""")],
            [new EdgeDefinition("t", "c")]);

        IWorkflowValidator validator = services.GetRequiredService<IWorkflowValidator>();

        validator.Validate(valid, catalog, 10).Should().BeEmpty();
        validator.Validate(invalid, catalog, 10).Select(error => (error.NodeId, error.Message)).Should().Equal(
            ("c", "Node 'c': config.all[0].op must be one of [\"eq\",\"neq\",\"contains\",\"gt\",\"lt\",\"in\",\"matches\"]."),
            ("c", "Node 'c': config.none is not allowed."));
    }

    private IConditionNodeType Condition() => GetServices().GetRequiredService<IConditionNodeType>();

    private async Task<NodeResult.Completed> CompletedAsync(string input) =>
        (await ExecuteAsync(input)).Should().BeOfType<NodeResult.Completed>().Subject;

    private Task<NodeResult> ExecuteAsync(string input) => Condition().ExecuteAsync(Context(input), CancellationToken);

    private static NodeContext Context(string input) => new("if", "condition@1", Json(input), Json("{}"), false);

    private static NodeDefinition Node(string id, string type, string config = "{}") => new(id, type, Json(config));

    private static JsonElement Json(string text)
    {
        using JsonDocument document = JsonDocument.Parse(text);
        return document.RootElement.Clone();
    }
}
