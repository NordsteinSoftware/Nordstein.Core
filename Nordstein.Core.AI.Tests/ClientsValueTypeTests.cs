using AwesomeAssertions;
using Nordstein.Core.AI.Clients;
using Nordstein.Core.AI.Completions;
using Nordstein.Core.AI.Messages;
using Nordstein.Core.AI.Tools;

namespace Nordstein.Core.AI.Tests;

/// <summary>
/// Exercises the public value types in <c>Nordstein.Core.AI.Clients</c> — <see cref="ModelOptions"/>,
/// the <see cref="ModelStreamUpdate"/> variants, and the request-preview records — for member
/// exposure, the null branches, and value equality. These carry no provider or network behaviour.
/// </summary>
[TestClass]
public sealed class ClientsValueTypeTests
{
    [TestMethod]
    public void ModelOptions_WithoutSampling_ExposesNameAndTools()
    {
        var options = new ModelOptions("gpt-4o", []);

        options.ModelName.Should().Be("gpt-4o");
        options.Tools.Should().BeEmpty();
        options.Sampling.Should().BeNull();
    }

    [TestMethod]
    public void ModelOptions_WithSampling_ExposesSampling()
    {
        var sampling = new ModelSamplingParameters(Temperature: 0.5);
        IReadOnlyList<ToolSpecification> tools = [];

        var options = new ModelOptions("gpt-4o", tools, sampling);

        options.Sampling.Should().BeSameAs(sampling);
    }

    [TestMethod]
    public void ModelOptions_Equality_ComparesByValue()
    {
        IReadOnlyList<ToolSpecification> tools = [];

        new ModelOptions("gpt-4o", tools).Should().Be(new ModelOptions("gpt-4o", tools));
        new ModelOptions("gpt-4o", tools).Should().NotBe(new ModelOptions("o1", tools));
    }

    [TestMethod]
    public void TextDelta_ExposesTextAndIsAStreamUpdate()
    {
        var delta = new TextDelta("chunk");

        delta.Text.Should().Be("chunk");
        delta.Should().BeAssignableTo<ModelStreamUpdate>();
        delta.Should().Be(new TextDelta("chunk"));
        delta.Should().NotBe(new TextDelta("other"));
    }

    [TestMethod]
    public void ToolRequested_ExposesRequest()
    {
        var request = new ToolRequest("id-1", "search", "{}");

        var update = new ToolRequested(request);

        update.Request.Should().BeSameAs(request);
        update.Should().BeAssignableTo<ModelStreamUpdate>();
    }

    [TestMethod]
    public void Completed_ExposesUsageLatencyAndFinishReason()
    {
        var usage = new TokenUsage(1, 2);

        var completed = new Completed(usage, TimeSpan.FromSeconds(1), "stop");

        completed.Usage.Should().Be(usage);
        completed.Latency.Should().Be(TimeSpan.FromSeconds(1));
        completed.FinishReason.Should().Be("stop");
        completed.Should().BeAssignableTo<ModelStreamUpdate>();
    }

    [TestMethod]
    public void Completed_WithNullUsageAndReason_AllowsNulls()
    {
        var completed = new Completed(Usage: null, TimeSpan.Zero, FinishReason: null);

        completed.Usage.Should().BeNull();
        completed.FinishReason.Should().BeNull();
    }

    [TestMethod]
    public void RequestToolCallPreview_ExposesMembers()
    {
        var toolCall = new RequestToolCallPreview("call-1", "search", """{"q":"x"}""");

        toolCall.Id.Should().Be("call-1");
        toolCall.Name.Should().Be("search");
        toolCall.Arguments.Should().Be("""{"q":"x"}""");
        toolCall.Should().Be(new RequestToolCallPreview("call-1", "search", """{"q":"x"}"""));
    }

    [TestMethod]
    public void RequestMessagePreview_ExposesMembers()
    {
        var toolCall = new RequestToolCallPreview("call-1", "search", "{}");

        var message = new RequestMessagePreview("assistant", "hi", [toolCall], "call-1");

        message.Role.Should().Be("assistant");
        message.Content.Should().Be("hi");
        message.ToolCalls.Should().ContainSingle().Which.Should().BeSameAs(toolCall);
        message.ToolCallId.Should().Be("call-1");
    }

    [TestMethod]
    public void RequestMessagePreview_WithNullContentAndToolCallId_AllowsNulls()
    {
        var message = new RequestMessagePreview("user", Content: null, [], ToolCallId: null);

        message.Content.Should().BeNull();
        message.ToolCalls.Should().BeEmpty();
        message.ToolCallId.Should().BeNull();
    }

    [TestMethod]
    public void RequestToolPreview_ExposesMembers()
    {
        var tool = new RequestToolPreview("search", "find things", "{}");

        tool.Name.Should().Be("search");
        tool.Description.Should().Be("find things");
        tool.JsonSchema.Should().Be("{}");
    }

    [TestMethod]
    public void ModelRequestPreview_ExposesModelMessagesAndTools()
    {
        var message = new RequestMessagePreview("user", "hi", [], null);
        var tool = new RequestToolPreview("search", "find things", "{}");
        IReadOnlyList<RequestMessagePreview> messages = [message];
        IReadOnlyList<RequestToolPreview> tools = [tool];

        var preview = new ModelRequestPreview("gpt-4o", messages, tools);

        preview.Model.Should().Be("gpt-4o");
        preview.Messages.Should().ContainSingle().Which.Should().BeSameAs(message);
        preview.Tools.Should().ContainSingle().Which.Should().BeSameAs(tool);
    }

    [TestMethod]
    public void ModelRequestPreview_Equality_ComparesByValue()
    {
        IReadOnlyList<RequestMessagePreview> messages = [new RequestMessagePreview("user", "hi", [], null)];
        IReadOnlyList<RequestToolPreview> tools = [new RequestToolPreview("search", "desc", "{}")];

        var a = new ModelRequestPreview("gpt-4o", messages, tools);
        var b = new ModelRequestPreview("gpt-4o", messages, tools);

        a.Should().Be(b);
        a.Should().NotBe(new ModelRequestPreview("o1", messages, tools));
    }
}
