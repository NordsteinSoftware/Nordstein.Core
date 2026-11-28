using System.Net;
using System.Text.Json;
using Autofac;
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Nordstein.Core.AI.Clients;
using Nordstein.Core.AI.Embeddings;
using Nordstein.Core.AI.OpenAiCompatible.Tests.Internal;
using Nordstein.Core.Testing;
using Nordstein.Core.Testing.Http;
using Nordstein.Core.Testing.Time;

namespace Nordstein.Core.AI.OpenAiCompatible.Tests;

[TestClass]
public sealed class OpenAiCompatibleEmbeddingClientTests : BaseTest<Module>
{
    private const string Key = "secret-key-value";
    private const string TwoVectors = """{"data":[{"index":1,"embedding":[0,1]},{"index":0,"embedding":[1,0]}]}""";

    private static readonly OpenAiCompatibleEmbeddingOptions Options = new()
    {
        BaseUrl = new Uri("https://api.example.com/v1/"),
        ApiKey = Key,
        Model = "example-embed-1",
        Dimension = 2,
    };

    [TestMethod]
    public async Task Embed_TwoInputs_PostsTheWireFormatAndOrdersByIndex()
    {
        using var handler = new ScriptedHttpMessageHandler().Respond(HttpStatusCode.OK, TwoVectors);
        IEmbeddingClient client = Client(handler, Options);

        IReadOnlyList<ReadOnlyMemory<float>> vectors = await client.EmbedAsync(["first", "second"], CancellationToken);

        vectors.Select(vector => vector.ToArray()).Should().BeEquivalentTo([new[] { 1f, 0f }, new[] { 0f, 1f }], options => options.WithStrictOrdering());
        RecordedHttpRequest request = handler.Requests.Should().ContainSingle().Which;
        request.Method.Should().Be(HttpMethod.Post);
        request.RequestUri.Should().Be(new Uri("https://api.example.com/v1/embeddings"));
        request.Headers["Authorization"].Should().Equal($"Bearer {Key}");
        using JsonDocument body = JsonDocument.Parse(request.ContentText);
        body.RootElement.GetProperty("model").GetString().Should().Be("example-embed-1");
        body.RootElement.GetProperty("encoding_format").GetString().Should().Be("float");
        body.RootElement.GetProperty("input").EnumerateArray().Select(input => input.GetString()).Should().Equal("first", "second");
        body.RootElement.TryGetProperty("dimensions", out _).Should().BeFalse();
        client.ModelId.Should().Be("example-embed-1");
        client.Dimension.Should().Be(2);
    }

    [TestMethod]
    public async Task Embed_SendDimensionAndNoKey_SendsDimensionsAndNoAuthorization()
    {
        using var handler = new ScriptedHttpMessageHandler().Respond(HttpStatusCode.OK, """{"data":[{"index":0,"embedding":[1,0]}]}""");
        IEmbeddingClient client = Client(handler, Options with { SendDimension = true, ApiKey = "" });

        await client.EmbedAsync(["only"], CancellationToken);

        RecordedHttpRequest request = handler.Requests.Should().ContainSingle().Which;
        request.Headers.Should().NotContainKey("Authorization");
        using JsonDocument body = JsonDocument.Parse(request.ContentText);
        body.RootElement.GetProperty("dimensions").GetInt32().Should().Be(2);
    }

    [TestMethod]
    public async Task Embed_NoInputs_ReturnsEmptyWithoutACall()
    {
        using var handler = new ScriptedHttpMessageHandler();
        IEmbeddingClient client = Client(handler, Options);

        (await client.EmbedAsync([], CancellationToken)).Should().BeEmpty();
        handler.Requests.Should().BeEmpty();
    }

    [TestMethod]
    public async Task Embed_MoreInputsThanABatch_SendsBatchesInOrder()
    {
        using var handler = new ScriptedHttpMessageHandler()
            .Respond(HttpStatusCode.OK, TwoVectors)
            .Respond(HttpStatusCode.OK, """{"data":[{"index":0,"embedding":[0.5,0.5]}]}""");
        IEmbeddingClient client = Client(handler, Options with { BatchSize = 2 });

        IReadOnlyList<ReadOnlyMemory<float>> vectors = await client.EmbedAsync(["a", "b", "c"], CancellationToken);

        vectors.Should().HaveCount(3);
        vectors[2].ToArray().Should().Equal(0.5f, 0.5f);
        handler.Requests.Should().HaveCount(2);
        handler.Requests[1].ContentText.Should().Contain("\"c\"").And.NotContain("\"a\"");
    }

    [TestMethod]
    public async Task Embed_NullInputs_Throw()
    {
        using var handler = new ScriptedHttpMessageHandler();
        IEmbeddingClient client = Client(handler, Options);

        await FluentActions.Awaiting(() => client.EmbedAsync(Missing.Reference<IReadOnlyList<string>>(), CancellationToken))
            .Should().ThrowAsync<ArgumentNullException>();
        await FluentActions.Awaiting(() => client.EmbedAsync([Missing.Reference<string>()], CancellationToken))
            .Should().ThrowAsync<ArgumentException>();
        handler.Requests.Should().BeEmpty();
    }

    [TestMethod]
    [DataRow(HttpStatusCode.InternalServerError)]
    [DataRow(HttpStatusCode.TooManyRequests)]
    [DataRow(HttpStatusCode.Unauthorized)]
    [DataRow(HttpStatusCode.TemporaryRedirect)]
    public async Task Embed_ErrorStatus_IsUnavailableAndNamesOnlyEndpointAndStatus(HttpStatusCode status)
    {
        using var handler = new ScriptedHttpMessageHandler().Respond(status, """{"error":"reply-detail"}""");
        IEmbeddingClient client = Client(handler, Options);

        Func<Task> act = () => client.EmbedAsync(["input-text"], CancellationToken);

        string message = (await act.Should().ThrowAsync<InferenceUnavailableException>()).Which.Message;
        message.Should().Contain("https://api.example.com/v1/embeddings").And.Contain(((int)status).ToString(System.Globalization.CultureInfo.InvariantCulture));
        message.Should().NotContain(Key).And.NotContain("input-text").And.NotContain("reply-detail");
    }

    [TestMethod]
    [DataRow(HttpStatusCode.BadRequest)]
    [DataRow(HttpStatusCode.RequestEntityTooLarge)]
    [DataRow(HttpStatusCode.UnprocessableEntity)]
    public async Task Embed_RequestRejected_IsContractFailure(HttpStatusCode status)
    {
        using var handler = new ScriptedHttpMessageHandler().Respond(status, """{"error":"reply-detail"}""");
        IEmbeddingClient client = Client(handler, Options);

        Func<Task> act = () => client.EmbedAsync(["input-text"], CancellationToken);

        (await act.Should().ThrowAsync<InferenceContractException>()).Which.Message.Should().Contain("rejected").And.NotContain("reply-detail");
    }

    [TestMethod]
    public async Task Embed_TransportFailure_IsUnavailableWithoutTheTransportMessage()
    {
        using var handler = new ScriptedHttpMessageHandler().Throw(new HttpRequestException("dns-detail"));
        IEmbeddingClient client = Client(handler, Options);

        Func<Task> act = () => client.EmbedAsync(["a"], CancellationToken);

        InferenceUnavailableException exception = (await act.Should().ThrowAsync<InferenceUnavailableException>()).Which;
        exception.Message.Should().NotContain("dns-detail");
        exception.InnerException.Should().BeOfType<HttpRequestException>();
    }

    [TestMethod]
    public async Task Embed_EndpointNeverAnswers_TimesOutOnTheContainersClock()
    {
        using var handler = new ScriptedHttpMessageHandler().Hang();
        var clock = new ManualClock();
        IEmbeddingClient client = Client(handler, Options with { Timeout = TimeSpan.FromSeconds(5) }, clock);

        Task<IReadOnlyList<ReadOnlyMemory<float>>> pending = client.EmbedAsync(["a"], CancellationToken);
        await clock.WaitForPendingTimerAsync(CancellationToken);
        clock.Advance(TimeSpan.FromSeconds(5));

        await FluentActions.Awaiting(() => pending).Should().ThrowAsync<InferenceUnavailableException>();
    }

    [TestMethod]
    public async Task Embed_CallerCancels_PropagatesCancellation()
    {
        using var handler = new ScriptedHttpMessageHandler().Hang();
        IEmbeddingClient client = Client(handler, Options);
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(CancellationToken);

        Task<IReadOnlyList<ReadOnlyMemory<float>>> pending = client.EmbedAsync(["a"], cancellation.Token);
        await cancellation.CancelAsync();

        await FluentActions.Awaiting(() => pending).Should().ThrowAsync<OperationCanceledException>();
    }

    [TestMethod]
    [DataRow("<html>login</html>", DisplayName = "not JSON")]
    [DataRow("""{"data":null}""", DisplayName = "no data")]
    [DataRow("null", DisplayName = "null document")]
    [DataRow("""{"data":[{"index":0,"embedding":[1,0]}]}""", DisplayName = "too few embeddings")]
    [DataRow("""{"data":[{"index":0,"embedding":[1,0]},{"index":0,"embedding":[0,1]}]}""", DisplayName = "repeated index")]
    [DataRow("""{"data":[{"index":0,"embedding":[1,0]},{"index":2,"embedding":[0,1]}]}""", DisplayName = "index too large")]
    [DataRow("""{"data":[{"index":-1,"embedding":[1,0]},{"index":1,"embedding":[0,1]}]}""", DisplayName = "negative index")]
    [DataRow("""{"data":[{"index":0,"embedding":[1,0]},null]}""", DisplayName = "null item")]
    [DataRow("""{"data":[{"index":0,"embedding":[1,0]},{"index":1,"embedding":[0,1,0]}]}""", DisplayName = "wrong dimension")]
    [DataRow("""{"data":[{"index":0,"embedding":[1,0]},{"index":1}]}""", DisplayName = "missing embedding")]
    [DataRow("""{"data":[{"index":0,"embedding":[1,0]},{"index":1,"embedding":[1e39,0]}]}""", DisplayName = "infinite component")]
    public async Task Embed_ReplyOfWrongShape_IsContractFailureWithoutTheReply(string reply)
    {
        using var handler = new ScriptedHttpMessageHandler().Respond(HttpStatusCode.OK, reply);
        IEmbeddingClient client = Client(handler, Options);

        Func<Task> act = () => client.EmbedAsync(["first", "second"], CancellationToken);

        string message = (await act.Should().ThrowAsync<InferenceContractException>()).Which.Message;
        message.Should().Contain("https://api.example.com/v1/embeddings").And.NotContain("login");
    }

    [TestMethod]
    public void Factory_NullOrInvalidOptions_Throws()
    {
        OpenAiCompatibleEmbeddingClientFactory factory = GetServices().GetRequiredService<OpenAiCompatibleEmbeddingClientFactory>();

        ((Action)(() => factory(Missing.Reference<OpenAiCompatibleEmbeddingOptions>()))).Should().Throw<ArgumentNullException>();
        ((Action)(() => factory(Options with { Dimension = 0 }))).Should().Throw<InvalidOperationException>();
    }

    [TestMethod]
    public void Module_NoHostHandler_RegistersTheDirectHandlerOnce()
    {
        IServiceProvider services = GetServices();
        ILifetimeScope scope = services.GetRequiredService<ILifetimeScope>();

        HttpMessageHandler first = scope.ResolveNamed<HttpMessageHandler>(Module.HandlerName);
        HttpMessageHandler second = scope.ResolveNamed<HttpMessageHandler>(Module.HandlerName);

        SocketsHttpHandler direct = first.Should().BeOfType<SocketsHttpHandler>().Which;
        direct.UseProxy.Should().BeFalse();
        direct.AllowAutoRedirect.Should().BeFalse();
        second.Should().BeSameAs(first);
        services.GetRequiredService<OpenAiCompatibleEmbeddingClientFactory>()(Options).Should().NotBeNull();
    }

    protected override void ConfigureContainer(ContainerBuilder builder)
    {
        base.ConfigureContainer(builder);
        builder.RegisterModule<Common.Module>();
    }

    private IEmbeddingClient Client(HttpMessageHandler handler, OpenAiCompatibleEmbeddingOptions options, TimeProvider? timeProvider = null)
        => GetServices(builder =>
        {
            builder.RegisterInstance(handler).Named<HttpMessageHandler>(Module.HandlerName).ExternallyOwned();
            if (timeProvider is not null)
            {
                builder.RegisterInstance(timeProvider).As<TimeProvider>();
            }
        }).GetRequiredService<OpenAiCompatibleEmbeddingClientFactory>()(options);
}
