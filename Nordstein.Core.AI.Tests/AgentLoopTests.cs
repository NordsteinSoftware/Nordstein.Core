using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Nordstein.Core.AI.Agents;
using Nordstein.Core.AI.Clients;
using Nordstein.Core.AI.Completions;
using Nordstein.Core.AI.Messages;
using Nordstein.Core.AI.Serialization;
using Nordstein.Core.AI.Tools;
using Nordstein.Core.Testing;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace Nordstein.Core.AI.Tests;

[TestClass]
public sealed class AgentLoopTests : BaseTest<Module>
{
    private const string SystemPrompt = "You check documents.";
    private const string LookUp = "look_up";
    private const string Record = "record";

    [TestMethod]
    public async Task RunAsync_FinalAnswerOnFirstStep_CompletesWithTranscript()
    {
        IServiceProvider services = GetServices();
        AssistantMessage answer = Answer("All fine.");
        IModelClient client = Client(services, answer);
        IToolInvoker tools = Substitute.For<IToolInvoker>();
        UserMessage question = Message.CreateUserMessage("Check it.");

        AgentLoopResult result = await Loop(services).RunAsync(
            Agent(), client, tools, Conversation.Create().With(question), Options(), CancellationToken);

        result.Outcome.Should().Be(AgentLoopOutcome.Completed);
        result.Answer.Should().Be(answer);
        result.Steps.Should().Be(1);
        result.Conversation.Messages.Should().Equal(new SystemMessage(SystemPrompt), question, answer);
        await tools.DidNotReceiveWithAnyArgs().InvokeAsync(Arg.Any<ToolRequest>(), Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public async Task RunAsync_ToolCallThenAnswer_InvokesToolAndShowsItsResultToTheModel()
    {
        IServiceProvider services = GetServices();
        var call = new ToolRequest("call-1", LookUp, """{"q":"x"}""");
        AssistantMessage answer = Answer("Done.");
        IModelClient client = Client(services, Calls(call), answer);
        IToolInvoker tools = Tools((LookUp, "found it"));

        AgentLoopResult result = await Loop(services).RunAsync(
            Agent(), client, tools, Start(), Options(), CancellationToken);

        result.Outcome.Should().Be(AgentLoopOutcome.Completed);
        result.Answer.Should().Be(answer);
        result.Steps.Should().Be(2);
        await tools.Received(1).InvokeAsync(call, Arg.Any<CancellationToken>());
        ToolMessage toolMessage = result.Conversation.Messages.OfType<ToolMessage>().Single();
        toolMessage.Id.Should().Be("call-1");
        toolMessage.GetText().Should().Be("found it");
        await client.Received(1).CompleteAsync(
            Arg.Is<Conversation>(c => c.Messages.Count == 4 && c.Messages[3] == toolMessage),
            Arg.Any<ModelOptions?>(),
            Arg.Any<IReadOnlyDictionary<string, string>?>(),
            Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public async Task RunAsync_SeveralToolCallsInOneReply_RunInTheOrderAsked()
    {
        IServiceProvider services = GetServices();
        var first = new ToolRequest("a", Record, "{}");
        var second = new ToolRequest("b", LookUp, "{}");
        IModelClient client = Client(services, Calls(first, second), Answer("Done."));
        List<string> order = [];
        IToolInvoker tools = Substitute.For<IToolInvoker>();
        tools.InvokeAsync(Arg.Any<ToolRequest>(), Arg.Any<CancellationToken>())
            .Returns(ci =>
            {
                order.Add(ci.Arg<ToolRequest>().Id);
                return Task.FromResult<IReadOnlyList<Content>>([Content.FromText("ok")]);
            });

        AgentLoopResult result = await Loop(services).RunAsync(
            Agent(), client, tools, Start(), Options(), CancellationToken);

        order.Should().Equal("a", "b");
        result.Conversation.Messages.OfType<ToolMessage>().Select(m => m.Id).Should().Equal("a", "b");
    }

    [TestMethod]
    public async Task RunAsync_StepLimitReached_IsIncompleteAndStillRunsTheLastStepsTools()
    {
        IServiceProvider services = GetServices();
        IModelClient client = Client(
            services,
            Calls(new ToolRequest("1", LookUp, "{}")),
            Calls(new ToolRequest("2", Record, "{}")),
            Answer("never asked"));
        IToolInvoker tools = Tools((LookUp, "x"), (Record, "saved"));

        AgentLoopResult result = await Loop(services).RunAsync(
            Agent(), client, tools, Start(), Options(maxSteps: 2), CancellationToken);

        result.Outcome.Should().Be(AgentLoopOutcome.StepLimitReached);
        result.Answer.Should().BeNull();
        result.Steps.Should().Be(2);
        await tools.Received(1).InvokeAsync(
            Arg.Is<ToolRequest>(r => r.Name == Record), Arg.Any<CancellationToken>());
        result.Conversation.Messages[^1].Should().BeOfType<ToolMessage>()
            .Which.Id.Should().Be("2");
        await client.ReceivedWithAnyArgs(2).CompleteAsync(Arg.Any<Conversation>(), Arg.Any<ModelOptions?>(), Arg.Any<IReadOnlyDictionary<string, string>?>(), Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public async Task RunAsync_MaxStepsOne_FinalAnswer_Completes()
    {
        IServiceProvider services = GetServices();
        IModelClient client = Client(services, Answer("ok"));

        AgentLoopResult result = await Loop(services).RunAsync(
            Agent(), client, Tools(), Start(), Options(maxSteps: 1), CancellationToken);

        result.Outcome.Should().Be(AgentLoopOutcome.Completed);
        result.Steps.Should().Be(1);
    }

    [TestMethod]
    public async Task RunAsync_ToolThrows_BecomesAFailedResultAndTheRunGoesOn()
    {
        IServiceProvider services = GetServices();
        IModelClient client = Client(services, Calls(new ToolRequest("1", LookUp, "{}")), Answer("Sorry."));
        IToolInvoker tools = Substitute.For<IToolInvoker>();
        tools.InvokeAsync(Arg.Any<ToolRequest>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("index offline"));

        AgentLoopResult result = await Loop(services).RunAsync(
            Agent(), client, tools, Start(), Options(), CancellationToken);

        result.Outcome.Should().Be(AgentLoopOutcome.Completed);
        string text = result.Conversation.Messages.OfType<ToolMessage>().Single().GetText();
        text.Should().Contain("Status: Failure").And.Contain("index offline");
    }

    [TestMethod]
    public async Task RunAsync_ToolThrowsSynchronously_BecomesAFailedResult()
    {
        IServiceProvider services = GetServices();
        IModelClient client = Client(services, Calls(new ToolRequest("1", LookUp, "{}")), Answer("Sorry."));
        IToolInvoker tools = Substitute.For<IToolInvoker>();
        tools.InvokeAsync(Arg.Any<ToolRequest>(), Arg.Any<CancellationToken>())
            .Throws(new FormatException("bad arguments"));

        AgentLoopResult result = await Loop(services).RunAsync(
            Agent(), client, tools, Start(), Options(), CancellationToken);

        result.Conversation.Messages.OfType<ToolMessage>().Single().GetText()
            .Should().Contain("bad arguments");
    }

    [TestMethod]
    public async Task RunAsync_ToolCancelledByItsOwnToken_BecomesAFailedResult()
    {
        IServiceProvider services = GetServices();
        IModelClient client = Client(services, Calls(new ToolRequest("1", LookUp, "{}")), Answer("Timed out."));
        IToolInvoker tools = Substitute.For<IToolInvoker>();
        tools.InvokeAsync(Arg.Any<ToolRequest>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new TaskCanceledException("tool timeout"));

        AgentLoopResult result = await Loop(services).RunAsync(
            Agent(), client, tools, Start(), Options(), CancellationToken);

        result.Outcome.Should().Be(AgentLoopOutcome.Completed);
        result.Conversation.Messages.OfType<ToolMessage>().Single().GetText()
            .Should().Contain("tool timeout");
    }

    [TestMethod]
    public async Task RunAsync_UnknownTool_BecomesAFailedResultWithoutCallingTheInvoker()
    {
        IServiceProvider services = GetServices();
        IModelClient client = Client(services, Calls(new ToolRequest("1", "delete_all", "{}")), Answer("ok"));
        IToolInvoker tools = Tools();

        AgentLoopResult result = await Loop(services).RunAsync(
            Agent(), client, tools, Start(), Options(), CancellationToken);

        await tools.DidNotReceiveWithAnyArgs().InvokeAsync(Arg.Any<ToolRequest>(), Arg.Any<CancellationToken>());
        result.Conversation.Messages.OfType<ToolMessage>().Single().GetText()
            .Should().Contain("Status: Failure").And.Contain("delete_all");
    }

    [TestMethod]
    public async Task RunAsync_ToolNameDiffersInCase_IsUnknown()
    {
        IServiceProvider services = GetServices();
        IModelClient client = Client(services, Calls(new ToolRequest("1", "LOOK_UP", "{}")), Answer("ok"));
        IToolInvoker tools = Tools();

        await Loop(services).RunAsync(Agent(), client, tools, Start(), Options(), CancellationToken);

        await tools.DidNotReceiveWithAnyArgs().InvokeAsync(Arg.Any<ToolRequest>(), Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public async Task RunAsync_AlreadyCancelled_ThrowsWithoutCallingTheModel()
    {
        IServiceProvider services = GetServices();
        IModelClient client = Client(services, Answer("ok"));
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        Func<Task> act = () => Loop(services).RunAsync(Agent(), client, Tools(), Start(), Options(), cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        await client.DidNotReceiveWithAnyArgs().CompleteAsync(Arg.Any<Conversation>(), Arg.Any<ModelOptions?>(), Arg.Any<IReadOnlyDictionary<string, string>?>(), Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public async Task RunAsync_CancelledInsideATool_ThrowsAndMakesNoFurtherCall()
    {
        IServiceProvider services = GetServices();
        using var cts = new CancellationTokenSource();
        IModelClient client = Client(
            services,
            Calls(new ToolRequest("1", LookUp, "{}"), new ToolRequest("2", Record, "{}")),
            Answer("never"));
        IToolInvoker tools = Substitute.For<IToolInvoker>();
        tools.InvokeAsync(Arg.Is<ToolRequest>(r => r.Id == "1"), Arg.Any<CancellationToken>())
            .Returns(async ci =>
            {
                await cts.CancelAsync();
                ci.Arg<CancellationToken>().ThrowIfCancellationRequested();
                return (IReadOnlyList<Content>)[];
            });

        Func<Task> act = () => Loop(services).RunAsync(Agent(), client, tools, Start(), Options(), cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        await tools.DidNotReceive().InvokeAsync(
            Arg.Is<ToolRequest>(r => r.Id == "2"), Arg.Any<CancellationToken>());
        await client.ReceivedWithAnyArgs(1).CompleteAsync(Arg.Any<Conversation>(), Arg.Any<ModelOptions?>(), Arg.Any<IReadOnlyDictionary<string, string>?>(), Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public async Task RunAsync_CancelledInsideTheLastStepsLastTool_Throws()
    {
        // Regression guard for the rethrow filter: no later check would catch this cancellation.
        IServiceProvider services = GetServices();
        using var cts = new CancellationTokenSource();
        IModelClient client = Client(services, Calls(new ToolRequest("1", LookUp, "{}")));
        IToolInvoker tools = Substitute.For<IToolInvoker>();
        tools.InvokeAsync(Arg.Any<ToolRequest>(), Arg.Any<CancellationToken>())
            .Returns(async ci =>
            {
                await cts.CancelAsync();
                ci.Arg<CancellationToken>().ThrowIfCancellationRequested();
                return (IReadOnlyList<Content>)[];
            });

        Func<Task> act = () => Loop(services).RunAsync(Agent(), client, tools, Start(), Options(maxSteps: 1), cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [TestMethod]
    public async Task RunAsync_CancelledByAToolThatReturns_ThrowsBeforeTheNextCall()
    {
        IServiceProvider services = GetServices();
        using var cts = new CancellationTokenSource();
        IModelClient client = Client(services, Calls(new ToolRequest("1", LookUp, "{}")), Answer("never"));
        IToolInvoker tools = Substitute.For<IToolInvoker>();
        tools.InvokeAsync(Arg.Any<ToolRequest>(), Arg.Any<CancellationToken>())
            .Returns(async _ =>
            {
                await cts.CancelAsync();
                return (IReadOnlyList<Content>)[Content.FromText("x")];
            });

        Func<Task> act = () => Loop(services).RunAsync(Agent(), client, tools, Start(), Options(), cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        await client.ReceivedWithAnyArgs(1).CompleteAsync(Arg.Any<Conversation>(), Arg.Any<ModelOptions?>(), Arg.Any<IReadOnlyDictionary<string, string>?>(), Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public async Task RunAsync_PassesTheRunTokenToModelAndTools()
    {
        IServiceProvider services = GetServices();
        using var cts = new CancellationTokenSource();
        IModelClient client = Client(services, Calls(new ToolRequest("1", LookUp, "{}")), Answer("ok"));
        IToolInvoker tools = Tools((LookUp, "x"));

        await Loop(services).RunAsync(Agent(), client, tools, Start(), Options(), cts.Token);

        await client.Received(2).CompleteAsync(
            Arg.Any<Conversation>(), Arg.Any<ModelOptions?>(),
            Arg.Any<IReadOnlyDictionary<string, string>?>(), cts.Token);
        await tools.Received(1).InvokeAsync(Arg.Any<ToolRequest>(), cts.Token);
    }

    [TestMethod]
    public async Task RunAsync_ModelThrows_Propagates()
    {
        IServiceProvider services = GetServices();
        IModelClient client = Substitute.For<IModelClient>();
        client.CompleteAsync(Arg.Any<Conversation>(), Arg.Any<ModelOptions?>(), Arg.Any<IReadOnlyDictionary<string, string>?>(), Arg.Any<CancellationToken>())
            .ReturnsForAnyArgs(Task.FromException<ICompletion>(new HttpRequestException("503")));

        Func<Task> act = () => Loop(services).RunAsync(Agent(), client, Tools(), Start(), Options(), CancellationToken);

        await act.Should().ThrowAsync<HttpRequestException>().WithMessage("503");
    }

    [TestMethod]
    public async Task RunAsync_SendsModelNameToolsSamplingAndVariables()
    {
        IServiceProvider services = GetServices();
        var parametersFactory = services.GetRequiredService<IModelParameters.Create>();
        IModelParameters parameters = parametersFactory(
            temperature: 0.2, topP: 0.9, reasoningEffort: "low", frequencyPenalty: 0.1,
            presencePenalty: 0.3, maxTokens: 500, seed: 7, stop: ["END"], n: 3);
        IAgent agent = Agent(parameters);
        IModelClient client = Client(services, Answer("ok"));
        var variables = new Dictionary<string, string> { ["today"] = "2026-10-26" };
        ModelOptions? sent = null;
        await client.CompleteAsync(
            Arg.Any<Conversation>(),
            Arg.Do<ModelOptions?>(o => sent = o),
            Arg.Any<IReadOnlyDictionary<string, string>?>(),
            Arg.Any<CancellationToken>());

        await Loop(services).RunAsync(
            agent, client, Tools(), Start(), new AgentLoopOptions("smart-model", 4, variables), CancellationToken);

        agent.Received(1).CreateSystemMessage(variables);
        await client.Received(1).CompleteAsync(
            Arg.Any<Conversation>(), Arg.Any<ModelOptions?>(), variables, Arg.Any<CancellationToken>());
        Assert.IsNotNull(sent);
        sent.ModelName.Should().Be("smart-model");
        sent.Tools.Should().Equal(agent.Tools);
        sent.ResponseFormat.Should().BeNull();
        Assert.IsNotNull(sent.Sampling);
        sent.Sampling.Should().BeEquivalentTo(
            new ModelSamplingParameters(0.2, 0.9, 0.1, 0.3, 500, 7, ["END"], "low"));
    }

    [TestMethod]
    public async Task RunAsync_ResponseFormat_SentOnEveryStep()
    {
        IServiceProvider services = GetServices();
        IModelClient client = Client(services, Calls(new ToolRequest("1", LookUp, "{}")), Answer("{\"ok\":true}"));
        List<ModelOptions?> sent = [];
        await client.CompleteAsync(
            Arg.Any<Conversation>(),
            Arg.Do<ModelOptions?>(sent.Add),
            Arg.Any<IReadOnlyDictionary<string, string>?>(),
            Arg.Any<CancellationToken>());
        var format = new JsonSchemaResponseFormat("answer", """{"type":"object","properties":{"ok":{"type":"boolean"}},"required":["ok"],"additionalProperties":false}""");

        AgentLoopResult result = await Loop(services).RunAsync(
            Agent(), client, Tools((LookUp, "x")), Start(), Options() with { ResponseFormat = format }, CancellationToken);

        result.Outcome.Should().Be(AgentLoopOutcome.Completed);
        sent.Should().HaveCount(2);
        sent.Should().AllSatisfy(options => options.Should().NotBeNull().And.Match<ModelOptions>(o => o.ResponseFormat == format));
    }

    [TestMethod]
    public void AgentLoopOptions_ResponseFormat_DefaultsToNoneAndTakesPartInEquality()
    {
        var format = new JsonSchemaResponseFormat("answer", """{"type":"object"}""");

        Options().ResponseFormat.Should().BeNull();
        (Options() with { ResponseFormat = format }).Should().Be(Options() with { ResponseFormat = format });
        (Options() with { ResponseFormat = format }).Should().NotBe(Options());
    }

    [TestMethod]
    public async Task RunAsync_EmptyModelParameters_SendsNoSampling()
    {
        IServiceProvider services = GetServices();
        IModelClient client = Client(services, Answer("ok"));
        ModelOptions? sent = null;
        await client.CompleteAsync(
            Arg.Any<Conversation>(),
            Arg.Do<ModelOptions?>(o => sent = o),
            Arg.Any<IReadOnlyDictionary<string, string>?>(),
            Arg.Any<CancellationToken>());

        await Loop(services).RunAsync(Agent(), client, Tools(), Start(), Options(), CancellationToken);

        Assert.IsNotNull(sent);
        sent.Sampling.Should().BeNull();
        sent.ResponseFormat.Should().BeNull();
    }

    [TestMethod]
    public async Task RunAsync_SumsTheReportedUsage()
    {
        IServiceProvider services = GetServices();
        var create = services.GetRequiredService<ICompletion.Create>();
        IModelClient client = Substitute.For<IModelClient>();
        client.CompleteAsync(Arg.Any<Conversation>(), Arg.Any<ModelOptions?>(), Arg.Any<IReadOnlyDictionary<string, string>?>(), Arg.Any<CancellationToken>()).ReturnsForAnyArgs(
            create(Calls(new ToolRequest("1", LookUp, "{}")), new TokenUsage(10, 2, 4), TimeSpan.Zero),
            create(Calls(new ToolRequest("2", LookUp, "{}")), null, TimeSpan.Zero),
            create(Answer("ok"), new TokenUsage(5, 1), TimeSpan.Zero));

        AgentLoopResult result = await Loop(services).RunAsync(
            Agent(), client, Tools((LookUp, "x")), Start(), Options(), CancellationToken);

        result.Usage.Should().Be(new TokenUsage(15, 3, 4));
    }

    [TestMethod]
    [DataRow("unavailable")]
    [DataRow("contract")]
    [DataRow("format")]
    public async Task RunAsync_ModelClientThrows_TheExceptionPropagatesUnchanged(string kind)
    {
        IServiceProvider services = GetServices();
        Exception thrown = kind switch
        {
            "unavailable" => new InferenceUnavailableException("The endpoint did not answer."),
            "contract" => new InferenceContractException("The answer has no choices."),
            _ => new OutputFormatException("$.total", OutputFormatViolation.WrongType),
        };
        IModelClient client = Substitute.For<IModelClient>();
        client.CompleteAsync(Arg.Any<Conversation>(), Arg.Any<ModelOptions?>(), Arg.Any<IReadOnlyDictionary<string, string>?>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(thrown);

        Func<Task> run = () => Loop(services).RunAsync(Agent(), client, Tools(), Start(), Options(), CancellationToken);

        (await run.Should().ThrowAsync<Exception>()).Which.Should().BeSameAs(thrown);
    }

    [TestMethod]
    public async Task RunAsync_NoUsageReported_UsageIsNull()
    {
        IServiceProvider services = GetServices();
        var create = services.GetRequiredService<ICompletion.Create>();
        IModelClient client = Substitute.For<IModelClient>();
        client.CompleteAsync(Arg.Any<Conversation>(), Arg.Any<ModelOptions?>(), Arg.Any<IReadOnlyDictionary<string, string>?>(), Arg.Any<CancellationToken>())
            .ReturnsForAnyArgs(create(Answer("ok"), null, TimeSpan.Zero));

        AgentLoopResult result = await Loop(services).RunAsync(
            Agent(), client, Tools(), Start(), Options(), CancellationToken);

        result.Usage.Should().BeNull();
    }

    [TestMethod]
    public async Task RunAsync_DoesNotDisposeTheClient()
    {
        IServiceProvider services = GetServices();
        IModelClient client = Client(services, Answer("ok"));

        await Loop(services).RunAsync(Agent(), client, Tools(), Start(), Options(), CancellationToken);

        client.DidNotReceive().Dispose();
    }

    [TestMethod]
    public async Task RunAsync_ConversationWithASystemMessage_Throws()
    {
        IServiceProvider services = GetServices();
        IModelClient client = Client(services, Answer("ok"));
        Conversation conversation = Start().WithSystemMessage(new SystemMessage("other"));

        Func<Task> act = () => Loop(services).RunAsync(Agent(), client, Tools(), conversation, Options(), CancellationToken);

        await act.Should().ThrowAsync<ArgumentException>().WithParameterName("conversation");
        await client.DidNotReceiveWithAnyArgs().CompleteAsync(Arg.Any<Conversation>(), Arg.Any<ModelOptions?>(), Arg.Any<IReadOnlyDictionary<string, string>?>(), Arg.Any<CancellationToken>());
    }

    [TestMethod]
    [DataRow(0)]
    [DataRow(-1)]
    public async Task RunAsync_MaxStepsBelowOne_Throws(int maxSteps)
    {
        IServiceProvider services = GetServices();

        Func<Task> act = () => Loop(services).RunAsync(
            Agent(), Client(services, Answer("ok")), Tools(), Start(), Options(maxSteps), CancellationToken);

        await act.Should().ThrowAsync<ArgumentOutOfRangeException>().WithParameterName("options");
    }

    [TestMethod]
    [DataRow("")]
    [DataRow("  ")]
    public async Task RunAsync_BlankModelName_Throws(string modelName)
    {
        IServiceProvider services = GetServices();

        Func<Task> act = () => Loop(services).RunAsync(
            Agent(), Client(services, Answer("ok")), Tools(), Start(), new AgentLoopOptions(modelName, 3), CancellationToken);

        await act.Should().ThrowAsync<ArgumentException>().WithParameterName("options");
    }

    [TestMethod]
    public async Task RunAsync_NullArguments_Throw()
    {
        IServiceProvider services = GetServices();
        IAgentLoop loop = Loop(services);
        IModelClient client = Client(services, Answer("ok"));

        await FluentActions.Awaiting(() => loop.RunAsync(null!, client, Tools(), Start(), Options(), CancellationToken))
            .Should().ThrowAsync<ArgumentNullException>().WithParameterName("agent");
        await FluentActions.Awaiting(() => loop.RunAsync(Agent(), null!, Tools(), Start(), Options(), CancellationToken))
            .Should().ThrowAsync<ArgumentNullException>().WithParameterName("client");
        await FluentActions.Awaiting(() => loop.RunAsync(Agent(), client, null!, Start(), Options(), CancellationToken))
            .Should().ThrowAsync<ArgumentNullException>().WithParameterName("tools");
        await FluentActions.Awaiting(() => loop.RunAsync(Agent(), client, Tools(), null!, Options(), CancellationToken))
            .Should().ThrowAsync<ArgumentNullException>().WithParameterName("conversation");
        await FluentActions.Awaiting(() => loop.RunAsync(Agent(), client, Tools(), Start(), null!, CancellationToken))
            .Should().ThrowAsync<ArgumentNullException>().WithParameterName("options");
    }

    [TestMethod]
    public void Module_RegistersTheLoopStateless()
    {
        IServiceProvider services = GetServices();

        IAgentLoop first = Loop(services);

        first.Should().BeSameAs(Loop(services));
    }

    private static IAgentLoop Loop(IServiceProvider services)
        => services.GetRequiredService<IAgentLoop>();

    private static AgentLoopOptions Options(int maxSteps = 8)
        => new("test-model", maxSteps);

    private static Conversation Start()
        => Conversation.Create().With(Message.CreateUserMessage("Check it."));

    private static AssistantMessage Answer(string text)
        => new([Content.FromText(text)], []);

    private static AssistantMessage Calls(params ToolRequest[] requests)
        => new([], requests);

    private static IAgent Agent(IModelParameters? parameters = null)
    {
        IAgent agent = Substitute.For<IAgent>();
        agent.Name.Returns("checker");
        agent.Tools.Returns(
        [
            new ToolSpecification(LookUp, "Looks something up", ToolArguments.None),
            new ToolSpecification(Record, "Records a result", ToolArguments.None),
        ]);
        agent.ModelParameters.Returns(parameters ?? IModelParameters.Empty);
        agent.CreateSystemMessage(Arg.Any<IReadOnlyDictionary<string, string>?>())
            .Returns(new SystemMessage(SystemPrompt));
        return agent;
    }

    private static IModelClient Client(IServiceProvider services, params AssistantMessage[] replies)
    {
        var create = services.GetRequiredService<ICompletion.Create>();
        ICompletion[] completions = [.. replies.Select(reply => create(reply, null, TimeSpan.Zero))];
        IModelClient client = Substitute.For<IModelClient>();
        client.CompleteAsync(Arg.Any<Conversation>(), Arg.Any<ModelOptions?>(), Arg.Any<IReadOnlyDictionary<string, string>?>(), Arg.Any<CancellationToken>())
            .ReturnsForAnyArgs(completions[0], completions[1..]);
        return client;
    }

    private static IToolInvoker Tools(params (string Name, string Result)[] results)
    {
        IToolInvoker tools = Substitute.For<IToolInvoker>();
        foreach ((string name, string result) in results)
        {
            tools.InvokeAsync(Arg.Is<ToolRequest>(r => r.Name == name), Arg.Any<CancellationToken>())
                .Returns(Task.FromResult<IReadOnlyList<Content>>([Content.FromText(result)]));
        }

        return tools;
    }
}
