using System.Globalization;
using System.Text;
using System.Text.Json;
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Nordstein.Core.Testing;

namespace Nordstein.Core.Workflows.Tests;

/// <summary>
/// The graph format reader: the shape it reads into <see cref="WorkflowGraph"/>, the keys it ignores, and one error with
/// a JSON path for every other shape.
/// </summary>
[TestClass]
public sealed class WorkflowGraphReaderTests : BaseTest<Module>
{
    [TestMethod]
    public void Read_NodesAndEdges_ReadsIdsTypesConfigsRetriesAndBranches()
    {
        const string text = """
            {"nodes":[{"id":"start","type":"manual@1","config":{"a":1}},
                      {"id":"if","type":"condition@1"},
                      {"id":"call","type":"fetch@2","config":[1,"x"],"retry":{"attempts":3,"backoff":"PT5S"}}],
             "edges":[{"from":"start","to":"if"},{"from":"if","to":"call","branch":"true"}]}
            """;

        WorkflowGraph graph = Reader().Read(text);

        graph.Nodes.Select(node => (node.Id, node.Type)).Should().Equal(("start", "manual@1"), ("if", "condition@1"), ("call", "fetch@2"));
        graph.Nodes[0].Config.GetRawText().Should().Be("""{"a":1}""");
        graph.Nodes[1].Config.GetRawText().Should().Be("{}");
        graph.Nodes[2].Config.GetRawText().Should().Be("""[1,"x"]""");
        graph.Nodes.Select(node => node.Retry).Should().Equal(null, null, new RetryPolicy(3, TimeSpan.FromSeconds(5)));
        graph.Edges.Select(edge => (edge.From, edge.To, edge.Branch)).Should().Equal(("start", "if", (string?)null), ("if", "call", "true"));
    }

    [TestMethod]
    public void Read_TextAndElement_ReadTheSameGraph()
    {
        const string text = """{"nodes":[{"id":"a","type":"t@1","config":{"x":[1,{"y":null}]}}],"edges":[{"from":"a","to":"a"}]}""";
        using JsonDocument document = JsonDocument.Parse(text);

        Reader().Read(document.RootElement).Should().Be(Reader().Read(text));
    }

    [TestMethod]
    public void Read_Element_ConfigOutlivesTheCallersDocument()
    {
        WorkflowGraph graph;
        using (JsonDocument document = JsonDocument.Parse("""{"nodes":[{"id":"a","type":"t@1","config":{"x":1}}]}"""))
        {
            graph = Reader().Read(document.RootElement);
        }

        graph.Nodes[0].Config.GetProperty("x").GetInt32().Should().Be(1);
    }

    [TestMethod]
    public void Read_NoEdges_NoEdges()
    {
        Reader().Read("""{"nodes":[{"id":"a","type":"t@1"}]}""").Edges.Should().BeEmpty();
    }

    [TestMethod]
    public void Read_EmptyGraph_IsAGraphWithoutNodesForTheValidatorToRefuse()
    {
        WorkflowGraph graph = Reader().Read("""{"nodes":[],"edges":[]}""");

        graph.Nodes.Should().BeEmpty();
        graph.Edges.Should().BeEmpty();
    }

    [TestMethod]
    public void Read_ConfigNull_IsTheNullValue()
    {
        Reader().Read("""{"nodes":[{"id":"a","type":"t@1","config":null}]}""").Nodes[0].Config.ValueKind.Should().Be(JsonValueKind.Null);
    }

    [TestMethod]
    public void Read_UnknownKeysAtEveryLevel_AreIgnored()
    {
        const string text = """
            {"version":7,"layout":{"a":{"x":1,"y":2}},
             "nodes":[{"id":"a","type":"t@1","pin":{"output":1},"disabled":true,"retry":{"attempts":2,"jitter":"PT1S"}}],
             "edges":[{"from":"a","to":"b","label":"go"}]}
            """;

        WorkflowGraph graph = Reader().Read(text);

        graph.Should().Be(new WorkflowGraph(
            [new NodeDefinition("a", "t@1", Json("{}")) { Retry = new RetryPolicy(2, TimeSpan.Zero) }],
            [new EdgeDefinition("a", "b")]));
    }

    [TestMethod]
    public void Read_KeysDifferingOnlyInCase_AreUnknownKeys()
    {
        WorkflowGraph graph = Reader().Read("""{"Nodes":[],"nodes":[{"ID":"a","id":"b","Type":"x@1","type":"t@1"}]}""");

        graph.Nodes.Select(node => (node.Id, node.Type)).Should().Equal(("b", "t@1"));
        ReadInvalid("""{"Nodes":[]}""").Errors.Should().Equal(new WorkflowGraphFormatError("$.nodes", "$.nodes is missing; it is an array of nodes."));
    }

    [TestMethod]
    public void Read_UnknownNodeType_IsReadForTheValidatorToRefuse()
    {
        Reader().Read("""{"nodes":[{"id":"a","type":"no such type"}]}""").Nodes[0].Type.Should().Be("no such type");
    }

    [TestMethod]
    public void Read_KeysInAnyOrder_ReadTheSameGraph()
    {
        WorkflowGraph first = Reader().Read("""{"edges":[{"branch":"b","to":"y","from":"x"}],"nodes":[{"config":{"k":1},"type":"t@1","id":"x"}]}""");
        WorkflowGraph second = Reader().Read("""{"nodes":[{"id":"x","type":"t@1","config":{"k":1}}],"edges":[{"from":"x","to":"y","branch":"b"}]}""");

        first.Should().Be(second);
    }

    [TestMethod]
    [DataRow("[]", "$", "The graph is a JSON object, not an array.")]
    [DataRow("\"text\"", "$", "The graph is a JSON object, not a string.")]
    [DataRow("42", "$", "The graph is a JSON object, not a number.")]
    [DataRow("null", "$", "The graph is a JSON object, not null.")]
    [DataRow("true", "$", "The graph is a JSON object, not a boolean.")]
    [DataRow("{}", "$.nodes", "$.nodes is missing; it is an array of nodes.")]
    [DataRow("""{"nodes":{}}""", "$.nodes", "$.nodes is an object, not an array of nodes.")]
    [DataRow("""{"nodes":null}""", "$.nodes", "$.nodes is null, not an array of nodes.")]
    [DataRow("""{"nodes":[],"edges":{}}""", "$.edges", "$.edges is an object, not an array of edges.")]
    [DataRow("""{"nodes":[],"edges":null}""", "$.edges", "$.edges is null, not an array of edges.")]
    [DataRow("""{"nodes":["start"]}""", "$.nodes[0]", "$.nodes[0] is a string, not a node object.")]
    [DataRow("""{"nodes":[{"type":"t@1"}]}""", "$.nodes[0].id", "$.nodes[0].id is missing; it is a string.")]
    [DataRow("""{"nodes":[{"id":5,"type":"t@1"}]}""", "$.nodes[0].id", "$.nodes[0].id is a number, not a string.")]
    [DataRow("""{"nodes":[{"id":null,"type":"t@1"}]}""", "$.nodes[0].id", "$.nodes[0].id is null, not a string.")]
    [DataRow("""{"nodes":[{"id":"a"}]}""", "$.nodes[0].type", "$.nodes[0].type is missing; it is a string.")]
    [DataRow("""{"nodes":[{"id":"a","type":["t@1"]}]}""", "$.nodes[0].type", "$.nodes[0].type is an array, not a string.")]
    [DataRow("""{"nodes":[{"id":"a","type":"t@1","retry":[]}]}""", "$.nodes[0].retry", "$.nodes[0].retry is an array, not a retry object.")]
    [DataRow("""{"nodes":[{"id":"a","type":"t@1","retry":null}]}""", "$.nodes[0].retry", "$.nodes[0].retry is null, not a retry object.")]
    [DataRow("""{"nodes":[],"edges":[7]}""", "$.edges[0]", "$.edges[0] is a number, not an edge object.")]
    [DataRow("""{"nodes":[],"edges":[{"to":"b"}]}""", "$.edges[0].from", "$.edges[0].from is missing; it is a string.")]
    [DataRow("""{"nodes":[],"edges":[{"from":"a"}]}""", "$.edges[0].to", "$.edges[0].to is missing; it is a string.")]
    [DataRow("""{"nodes":[],"edges":[{"from":"a","to":2}]}""", "$.edges[0].to", "$.edges[0].to is a number, not a string.")]
    [DataRow("""{"nodes":[],"edges":[{"from":"a","to":"b","branch":true}]}""", "$.edges[0].branch", "$.edges[0].branch is a boolean, not a string.")]
    [DataRow("""{"nodes":[],"edges":[{"from":"a","to":"b","branch":null}]}""", "$.edges[0].branch", "$.edges[0].branch is null, not a string.")]
    public void Read_WrongTokenTypeOrMissingKey_OneErrorWithItsPath(string text, string path, string message)
    {
        WorkflowGraphFormatException exception = ReadInvalid(text);

        exception.Errors.Should().Equal(new WorkflowGraphFormatError(path, message));
    }

    [TestMethod]
    public void Read_SeveralProblems_ReportsEveryOneInDocumentOrder()
    {
        const string text = """
            {"nodes":[{"id":"a"},{"type":"t@1"},{"id":"c","type":"t@1","retry":{"attempts":1}}],
             "edges":[{"from":"a"},{"from":1,"to":"b"}]}
            """;

        WorkflowGraphFormatException exception = ReadInvalid(text);

        exception.Errors.Select(error => error.Path).Should().Equal(
            "$.nodes[0].type", "$.nodes[1].id", "$.nodes[2].retry.attempts", "$.edges[0].to", "$.edges[1].from");
        exception.Message.Should().Be(
            "The text is not a workflow graph: " + string.Join(" ", exception.Errors.Select(error => error.Message)));
    }

    [TestMethod]
    [DataRow("""{"nodes":[],"nodes":[]}""", "$", "nodes")]
    [DataRow("""{"nodes":[],"x":1,"x":2,"x":3}""", "$", "x")]
    [DataRow("""{"nodes":[{"id":"a","type":"t@1","config":{"k":1,"k":2,"k":3}}]}""", "$.nodes[0].config", "k")]
    [DataRow("""{"nodes":[],"layout":{},"layout":{}}""", "$", "layout")]
    [DataRow("""{"nodes":[{"id":"a","id":"b","type":"t@1"}]}""", "$.nodes[0]", "id")]
    [DataRow("""{"nodes":[],"edges":[{"from":"a","to":"b","to":"c"}]}""", "$.edges[0]", "to")]
    [DataRow("""{"nodes":[{"id":"a","type":"t@1","retry":{"attempts":2,"attempts":3}}]}""", "$.nodes[0].retry", "attempts")]
    [DataRow("""{"nodes":[{"id":"a","type":"t@1","config":{"k":1,"k":2}}]}""", "$.nodes[0].config", "k")]
    [DataRow("""{"nodes":[{"id":"a","type":"t@1","config":{"x":[{"deep":{"k":1,"k":1}}]}}]}""", "$.nodes[0].config.x[0].deep", "k")]
    [DataRow("""{"nodes":[{"id":"a","type":"t@1","config":{"odd key":{"k":1,"k":1}}}]}""", "$.nodes[0].config['odd key']", "k")]
    [DataRow("""{"nodes":[{"id":"a","type":"t@1","config":{"":{"k":1,"k":1}}}]}""", "$.nodes[0].config['']", "k")]
    [DataRow("""{"nodes":[{"id":"a","type":"t@1","config":{"1st":{"k":1,"k":1}}}]}""", "$.nodes[0].config['1st']", "k")]
    [DataRow("""{"nodes":[{"id":"a","type":"t@1","config":{"_a1":{"k":1,"k":1}}}]}""", "$.nodes[0].config._a1", "k")]
    [DataRow("""{"nodes":[{"id":"a","type":"t@1","config":{"it's \\ x":{"k":1,"k":1}}}]}""", "$.nodes[0].config['it\\'s \\\\ x']", "k")]
    public void Read_DuplicateProperty_RefusedAtItsObject(string text, string path, string name)
    {
        WorkflowGraphFormatException exception = ReadInvalid(text);

        exception.Errors.Should().Equal(new WorkflowGraphFormatError(path, $"{path} names the property '{name}' twice."));
    }

    [TestMethod]
    public void Read_DuplicatePropertyInsideAnIgnoredKey_IsTheProductsToJudge()
    {
        Reader().Read("""{"nodes":[{"id":"a","type":"t@1","pin":{"o":1,"o":2}}],"layout":{"a":1,"a":2}}""").Nodes.Should().ContainSingle();
    }

    [TestMethod]
    public void Read_DuplicatePropertyInAnElement_RefusedLikeInText()
    {
        using JsonDocument document = JsonDocument.Parse("""{"nodes":[{"id":"a","type":"t@1","config":{"k":1,"k":2}}]}""");

        Action act = () => Reader().Read(document.RootElement);

        act.Should().Throw<WorkflowGraphFormatException>().Which.Errors.Should().ContainSingle().Which.Path.Should().Be("$.nodes[0].config");
    }

    [TestMethod]
    [DataRow("PT5S", 0, 0, 0, 5, 0)]
    [DataRow("PT0S", 0, 0, 0, 0, 0)]
    [DataRow("PT1.5S", 0, 0, 0, 1, 500)]
    [DataRow("PT0.0000001S", 0, 0, 0, 0, 0)]
    [DataRow("PT90M", 0, 0, 90, 0, 0)]
    [DataRow("PT36H", 0, 36, 0, 0, 0)]
    [DataRow("P2D", 2, 0, 0, 0, 0)]
    [DataRow("P1DT2H3M4S", 1, 2, 3, 4, 0)]
    [DataRow("P60D", 60, 0, 0, 0, 0)]
    public void Read_Backoff_IsTheDuration(string backoff, int days, int hours, int minutes, int seconds, int milliseconds)
    {
        WorkflowGraph graph = Reader().Read($$$"""{"nodes":[{"id":"a","type":"t@1","retry":{"attempts":2,"backoff":"{{{backoff}}}"}}]}""");

        TimeSpan expected = new TimeSpan(days, hours, minutes, seconds, milliseconds) + (backoff == "PT0.0000001S" ? TimeSpan.FromTicks(1) : TimeSpan.Zero);
        graph.Nodes[0].Retry.Should().Be(new RetryPolicy(2, expected));
    }

    [TestMethod]
    public void Read_LargestBackoffATimeSpanHolds_IsRead()
    {
        string days = ((long)TimeSpan.MaxValue.TotalDays).ToString(CultureInfo.InvariantCulture);

        WorkflowGraph graph = Reader().Read($$$"""{"nodes":[{"id":"a","type":"t@1","retry":{"attempts":2,"backoff":"P{{{days}}}D"}}]}""");

        graph.Nodes[0].Retry?.Backoff.Should().Be(TimeSpan.FromDays((long)TimeSpan.MaxValue.TotalDays));
    }

    [TestMethod]
    [DataRow("5 seconds")]
    [DataRow("")]
    [DataRow("P")]
    [DataRow("PT")]
    [DataRow("-PT5S")]
    [DataRow("pt5s")]
    [DataRow("P1Y")]
    [DataRow("P1M")]
    [DataRow("P1W")]
    [DataRow("PT1,5S")]
    [DataRow("PT1.S")]
    [DataRow("PT.5S")]
    [DataRow("PT1.12345678S")]
    [DataRow("PT5S ")]
    [DataRow("PT5H5H")]
    [DataRow("PT5S5M")]
    [DataRow("P1DT")]
    [DataRow("PT١S")]
    [DataRow("P99999999999999999999D")]
    [DataRow("P10675200D")]
    [DataRow("PT000000000000000000000000000000000000000000000000000000000000000005S")]
    [DataRow("PT922337203686S")]
    [DataRow("P10675199DT48H")]
    public void Read_BackoffNotADurationOrBeyondATimeSpan_Refused(string backoff)
    {
        string text = JsonSerializer.Serialize(new { nodes = new[] { new { id = "a", type = "t@1", retry = new { attempts = 2, backoff } } } });

        WorkflowGraphFormatException exception = ReadInvalid(text);

        exception.Errors.Should().Equal(new WorkflowGraphFormatError(
            "$.nodes[0].retry.backoff",
            "$.nodes[0].retry.backoff is an ISO 8601 duration of days, hours, minutes and seconds such as PT5S or P1DT12H, at most 10675199 days."));
    }

    [TestMethod]
    [DataRow("""{"backoff":"PT5S"}""", "$.nodes[0].retry.attempts is missing; it is a whole number from 2 to 2147483647.")]
    [DataRow("""{"attempts":"3"}""", "$.nodes[0].retry.attempts is a string, not a whole number from 2 to 2147483647.")]
    [DataRow("""{"attempts":1}""", "$.nodes[0].retry.attempts is a whole number from 2 to 2147483647.")]
    [DataRow("""{"attempts":-3}""", "$.nodes[0].retry.attempts is a whole number from 2 to 2147483647.")]
    [DataRow("""{"attempts":3.0}""", "$.nodes[0].retry.attempts is a whole number from 2 to 2147483647.")]
    [DataRow("""{"attempts":3e0}""", "$.nodes[0].retry.attempts is a whole number from 2 to 2147483647.")]
    [DataRow("""{"attempts":2147483648}""", "$.nodes[0].retry.attempts is a whole number from 2 to 2147483647.")]
    [DataRow("""{"attempts":1e400}""", "$.nodes[0].retry.attempts is a whole number from 2 to 2147483647.")]
    [DataRow("""{"attempts":99999999999999999999999999999999999999999}""", "$.nodes[0].retry.attempts is a whole number from 2 to 2147483647.")]
    [DataRow("""{"attempts":2,"backoff":5}""", "$.nodes[0].retry.backoff is a number, not an ISO 8601 duration of days, hours, minutes and seconds such as PT5S or P1DT12H, at most 10675199 days.")]
    public void Read_MalformedRetry_RefusedAtTheRetry(string retry, string message)
    {
        WorkflowGraphFormatException exception = ReadInvalid($$"""{"nodes":[{"id":"a","type":"t@1","retry":{{retry}}}]}""");

        exception.Errors.Should().ContainSingle().Which.Message.Should().Be(message);
    }

    [TestMethod]
    public void Read_LargestAttempts_IsRead()
    {
        Reader().Read("""{"nodes":[{"id":"a","type":"t@1","retry":{"attempts":2147483647}}]}""").Nodes[0].Retry?.Attempts.Should().Be(int.MaxValue);
    }

    [TestMethod]
    public void Read_VeryLargeNumbersInAConfig_AreKeptAsWritten()
    {
        WorkflowGraph graph = Reader().Read("""{"nodes":[{"id":"a","type":"t@1","config":{"big":1e400,"long":123456789012345678901234567890}}]}""");

        graph.Nodes[0].Config.GetRawText().Should().Be("""{"big":1e400,"long":123456789012345678901234567890}""");
    }

    [TestMethod]
    public void Read_TextNestedDeeperThan64Levels_IsNotJson()
    {
        string text = """{"nodes":[{"id":"a","type":"t@1","config":""" + new string('[', 62) + new string(']', 62) + "}]}";

        WorkflowGraphFormatException exception = ReadInvalid(text);

        exception.Errors.Should().ContainSingle().Which.Message.Should().StartWith("The text is not JSON");
    }

    [TestMethod]
    public void Read_TextNestedExactly64Levels_IsRead()
    {
        string text = """{"nodes":[{"id":"a","type":"t@1","config":""" + new string('[', 61) + new string(']', 61) + "}]}";

        Reader().Read(text).Nodes[0].Config.ValueKind.Should().Be(JsonValueKind.Array);
    }

    [TestMethod]
    public void Read_ElementNestedDeeperThan64Levels_RefusedAtTheDeepestReadPlace()
    {
        string text = """{"nodes":[{"id":"a","type":"t@1","config":""" + new string('[', 62) + new string(']', 62) + "}]}";
        using JsonDocument document = JsonDocument.Parse(text, new JsonDocumentOptions { MaxDepth = 200 });

        Action act = () => Reader().Read(document.RootElement);

        WorkflowGraphFormatError error = act.Should().Throw<WorkflowGraphFormatException>().Which.Errors.Should().ContainSingle().Subject;
        error.Path.Should().Be("$.nodes[0].config" + string.Concat(Enumerable.Repeat("[0]", 61)));
        error.Message.Should().Be(error.Path + " nests deeper than 64 levels.");
    }

    [TestMethod]
    [DataRow("{nodes:")]
    [DataRow("""{"nodes":[]""")]
    [DataRow("""{"nodes":[]} {}""")]
    [DataRow("""{"nodes":[],}""")]
    [DataRow("""{"nodes":[] /* note */}""")]
    [DataRow("")]
    [DataRow("   ")]
    [DataRow("\uFEFF{\"nodes\":[]}")]
    public void Read_NotOneJsonDocument_OneErrorNamingTheParser(string text)
    {
        WorkflowGraphFormatException exception = ReadInvalid(text);

        WorkflowGraphFormatError error = exception.Errors.Should().ContainSingle().Subject;
        error.Path.Should().StartWith("$");
        error.Message.Should().StartWith("The text is not JSON (line ");
        exception.InnerException.Should().BeAssignableTo<JsonException>();
    }

    [TestMethod]
    [DataRow("""{"nodes":[{"id":"\uD800","type":"t@1"}]}""", "$.nodes[0].id")]
    [DataRow("""{"nodes":[{"id":"a","type":"t@1","config":{"\uDC00":1}}]}""", "$.nodes[0].config")]
    [DataRow("""{"nodes":[{"id":"a","type":"t@1","config":["ok","\uD800x"]}]}""", "$.nodes[0].config[1]")]
    [DataRow("""{"nodes":[],"edges":[{"from":"a","to":"b","branch":"\uDBFF"}]}""", "$.edges[0].branch")]
    [DataRow("""{"nodes":[{"id":"a","type":"t@1","retry":{"attempts":2,"backoff":"\uD800"}}]}""", "$.nodes[0].retry.backoff")]
    public void Read_EscapedUnpairedSurrogate_RefusedAsInvalidText(string text, string path)
    {
        WorkflowGraphFormatException exception = ReadInvalid(text);

        exception.Errors.Should().Equal(new WorkflowGraphFormatError(path, $"{path} holds an escaped character that is not valid text."));
    }

    [TestMethod]
    public void Read_EscapedUnpairedSurrogateInAnIgnoredKeyName_IsIgnored()
    {
        Reader().Read("""{"nodes":[],"\uD800":1}""").Nodes.Should().BeEmpty();
    }

    [TestMethod]
    public void Read_NonAsciiText_RoundTrips()
    {
        WorkflowGraph graph = Reader().Read("""{"nodes":[{"id":"a","type":"t@1","config":{"text":"Grüße 😀 é"}}]}""");

        graph.Nodes[0].Config.GetProperty("text").GetString().Should().Be("Grüße 😀 é");
    }

    [TestMethod]
    public void Read_ManyNodes_ReadsThemInOrder()
    {
        var text = new StringBuilder("""{"nodes":[""");
        for (int index = 0; index < 5000; index++)
        {
            text.Append(index == 0 ? string.Empty : ",").Append(CultureInfo.InvariantCulture, $$"""{"id":"n{{index}}","type":"t@1"}""");
        }

        WorkflowGraph graph = Reader().Read(text.Append("]}").ToString());

        graph.Nodes.Should().HaveCount(5000);
        graph.Nodes[4999].Id.Should().Be("n4999");
    }

    [TestMethod]
    public void Read_ConcurrentCallers_EachGetTheirOwnGraph()
    {
        IWorkflowGraphReader reader = Reader();

        WorkflowGraph[] graphs = [.. Enumerable.Range(0, 64).AsParallel().Select(index => reader.Read($$"""{"nodes":[{"id":"n{{index}}","type":"t@1"}]}"""))];

        graphs.Select(graph => graph.Nodes[0].Id).Should().BeEquivalentTo(Enumerable.Range(0, 64).Select(index => $"n{index}"));
    }

    [TestMethod]
    public void Read_ReadGraph_PassesTheValidator()
    {
        IServiceProvider services = GetServices();
        const string text = """
            {"nodes":[{"id":"t","type":"start@1"},{"id":"c","type":"condition@1","config":{"all":[{"left":"{{trigger.n}}","op":"gt","right":1}]}},{"id":"yes","type":"step@1"}],
             "edges":[{"from":"t","to":"c"},{"from":"c","to":"yes","branch":"true"}]}
            """;
        WorkflowGraph graph = services.GetRequiredService<IWorkflowGraphReader>().Read(text);
        INodeType[] catalog = [new TestNode("start") { IsTrigger = true }, services.GetRequiredService<IConditionNodeType>(), new TestNode("step")];

        services.GetRequiredService<IWorkflowValidator>().Validate(graph, catalog, 10).Should().BeEmpty();
    }

    [TestMethod]
    public void Resolve_Twice_IsOneSingleton()
    {
        IServiceProvider services = GetServices();

        services.GetRequiredService<IWorkflowGraphReader>().Should().BeSameAs(services.GetRequiredService<IWorkflowGraphReader>());
    }

    // WHY: the test passes null where the contract forbids it, to prove the runtime guards; the `!` operator is banned.
#pragma warning disable CS8625
    [TestMethod]
    public void Read_NullOrUndefinedInput_Throws()
    {
        FluentActions.Invoking(() => Reader().Read(json: null)).Should().Throw<ArgumentNullException>().WithParameterName("json");
        FluentActions.Invoking(() => Reader().Read(default(JsonElement))).Should().Throw<ArgumentException>().WithParameterName("graph");
    }

    [TestMethod]
    public void Exception_InvalidErrors_Throw()
    {
        FluentActions.Invoking(() => new WorkflowGraphFormatException(null)).Should().Throw<ArgumentNullException>().WithParameterName("errors");
        FluentActions.Invoking(() => new WorkflowGraphFormatException([])).Should().Throw<ArgumentException>().WithParameterName("errors");
        FluentActions.Invoking(() => new WorkflowGraphFormatException([null])).Should().Throw<ArgumentException>().WithParameterName("errors");
        FluentActions.Invoking(() => new WorkflowGraphFormatError(null, "m")).Should().Throw<ArgumentNullException>().WithParameterName("path");
        FluentActions.Invoking(() => new WorkflowGraphFormatError("$", null)).Should().Throw<ArgumentNullException>().WithParameterName("message");
    }
#pragma warning restore CS8625

    [TestMethod]
    public void Exception_Errors_AreACopy()
    {
        List<WorkflowGraphFormatError> errors = [new("$", "first.")];
        var exception = new WorkflowGraphFormatException(errors);

        errors.Add(new WorkflowGraphFormatError("$", "second."));

        exception.Errors.Should().ContainSingle();
        exception.Message.Should().Be("The text is not a workflow graph: first.");
    }

    private IWorkflowGraphReader Reader() => GetServices().GetRequiredService<IWorkflowGraphReader>();

    private WorkflowGraphFormatException ReadInvalid(string text)
    {
        Action act = () => Reader().Read(text);
        return act.Should().Throw<WorkflowGraphFormatException>().Subject.Single();
    }

    private static JsonElement Json(string text)
    {
        using JsonDocument document = JsonDocument.Parse(text);
        return document.RootElement.Clone();
    }
}
