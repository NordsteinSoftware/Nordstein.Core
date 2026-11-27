using System.Net;
using System.Net.Http.Headers;
using System.Text;
using AwesomeAssertions;
using Nordstein.Core.Testing.Http;

namespace Nordstein.Core.Testing.Tests.Http;

/// <summary>
/// <see cref="ScriptedHttpMessageHandler"/>: each send takes the next scripted step and is recorded
/// first, in send order; an empty script fails loudly; nothing ever leaves the process.
/// </summary>
[TestClass]
public sealed class ScriptedHttpMessageHandlerTests
{
    private static readonly Uri Endpoint = new("https://api.example.test/v1/");

    public required TestContext TestContext { get; init; }

    private CancellationToken CancellationToken => TestContext.CancellationToken;

    [TestMethod]
    public async Task Respond_WithStatusAndContent_AnswersOnceWithAFreshResponse()
    {
        using var handler = new ScriptedHttpMessageHandler().Respond(HttpStatusCode.Created, "{\"id\":7}");
        using var client = new HttpClient(handler) { BaseAddress = Endpoint };

        using HttpResponseMessage response = await client.GetAsync("things/7", CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        (await response.Content.ReadAsStringAsync(CancellationToken)).Should().Be("{\"id\":7}");
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/json");
        response.Content.Headers.ContentType?.CharSet.Should().Be("utf-8");
        response.RequestMessage?.RequestUri.Should().Be(new Uri(Endpoint, "things/7"));
        handler.RemainingSteps.Should().Be(0);
    }

    [TestMethod]
    public async Task Respond_WithAMediaType_UsesIt()
    {
        using var handler = new ScriptedHttpMessageHandler().Respond(HttpStatusCode.OK, "plain", "text/plain");
        using var client = new HttpClient(handler);

        using HttpResponseMessage response = await client.GetAsync(Endpoint, CancellationToken);

        response.Content.Headers.ContentType?.MediaType.Should().Be("text/plain");
    }

    [TestMethod]
    public async Task Respond_WithStatusOnly_AnswersWithAnEmptyBody()
    {
        using var handler = new ScriptedHttpMessageHandler().Respond(HttpStatusCode.NoContent);
        using var client = new HttpClient(handler);

        using HttpResponseMessage response = await client.DeleteAsync(Endpoint, CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await response.Content.ReadAsByteArrayAsync(CancellationToken)).Should().BeEmpty();
    }

    [TestMethod]
    public async Task Steps_AreTakenInOrder_AndAnEmptyScriptFailsLoudly()
    {
        using var handler = new ScriptedHttpMessageHandler()
            .Respond(HttpStatusCode.OK, "first")
            .Respond(HttpStatusCode.Accepted, "second");
        using var client = new HttpClient(handler);

        handler.RemainingSteps.Should().Be(2);
        using HttpResponseMessage first = await client.GetAsync(Endpoint, CancellationToken);
        using HttpResponseMessage second = await client.GetAsync(Endpoint, CancellationToken);

        (await first.Content.ReadAsStringAsync(CancellationToken)).Should().Be("first");
        second.StatusCode.Should().Be(HttpStatusCode.Accepted);
        await FluentActions
            .Awaiting(() => client.PostAsync(new Uri(Endpoint, "late"), new StringContent("x"), CancellationToken))
            .Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*no scripted step is left for request 3: POST https://api.example.test/v1/late*");
        handler.Requests.Should().HaveCount(3, "the unanswered request is recorded too");
    }

    [TestMethod]
    public async Task Respond_WithASynchronousFactory_SeesTheRequest()
    {
        using var handler = new ScriptedHttpMessageHandler()
            .Respond(request => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(request.Method.Method),
            });
        using var client = new HttpClient(handler);

        using HttpResponseMessage response = await client.PutAsync(Endpoint, new StringContent("body"), CancellationToken);

        (await response.Content.ReadAsStringAsync(CancellationToken)).Should().Be("PUT");
    }

    [TestMethod]
    public async Task Respond_WithAnAsynchronousFactory_ReceivesTheSendsToken()
    {
        CancellationToken seen = default;
        using var handler = new ScriptedHttpMessageHandler()
            .Respond((_, token) =>
            {
                seen = token;
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
            });
        using var invoker = new HttpMessageInvoker(handler);
        using var cancel = new CancellationTokenSource();
        using var request = new HttpRequestMessage(HttpMethod.Get, Endpoint);

        using HttpResponseMessage response = await invoker.SendAsync(request, cancel.Token);

        seen.Should().Be(cancel.Token);
    }

    [TestMethod]
    public async Task Respond_WithAFactoryReturningNull_FailsLoudly()
    {
#pragma warning disable CS8603 // WHY: the guard under test is reachable only with the null the annotations forbid.
        using var handler = new ScriptedHttpMessageHandler()
            .Respond(_ => null)
            .Respond((_, _) => null);
#pragma warning restore CS8603
        using var client = new HttpClient(handler);

        await FluentActions
            .Awaiting(() => client.GetAsync(Endpoint, CancellationToken))
            .Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*request 1 (GET https://api.example.test/v1/) returned no response*");
        await FluentActions
            .Awaiting(() => client.GetAsync(Endpoint, CancellationToken))
            .Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*request 2 (GET https://api.example.test/v1/) returned no response*");
    }

    [TestMethod]
    public async Task Respond_WithAResponseThatNamesItsRequest_KeepsIt()
    {
        using var named = new HttpRequestMessage(HttpMethod.Head, "https://other.example.test/");
        using var handler = new ScriptedHttpMessageHandler()
            .Respond(_ => new HttpResponseMessage(HttpStatusCode.OK) { RequestMessage = named });
        using var client = new HttpClient(handler);

        using HttpResponseMessage response = await client.GetAsync(Endpoint, CancellationToken);

        response.RequestMessage.Should().BeSameAs(named);
    }

    [TestMethod]
    public async Task Throw_ThrowsTheGivenException()
    {
        var failure = new HttpRequestException("connection refused");
        using var handler = new ScriptedHttpMessageHandler().Throw(failure);
        using var client = new HttpClient(handler);

        Exception thrown = (await FluentActions
            .Awaiting(() => client.GetAsync(Endpoint, CancellationToken))
            .Should().ThrowAsync<HttpRequestException>()).Which;

        thrown.Should().BeSameAs(failure);
    }

    [TestMethod]
    public async Task TimeOut_SurfacesAsTheClientTimeoutWithoutCallerCancellation()
    {
        using var handler = new ScriptedHttpMessageHandler().TimeOut();
        using var client = new HttpClient(handler);

        TaskCanceledException thrown = (await FluentActions
            .Awaiting(() => client.GetAsync(Endpoint, CancellationToken))
            .Should().ThrowAsync<TaskCanceledException>()).Which;

        thrown.InnerException.Should().BeOfType<TimeoutException>();
        CancellationToken.IsCancellationRequested.Should().BeFalse();
    }

    [TestMethod]
    public async Task Hang_WaitsUntilTheClientTimeoutCancelsTheSend()
    {
        using var handler = new ScriptedHttpMessageHandler().Hang();
        using var client = new HttpClient(handler) { Timeout = TimeSpan.FromMilliseconds(50) };

        TaskCanceledException thrown = (await FluentActions
            .Awaiting(() => client.GetAsync(Endpoint, CancellationToken))
            .Should().ThrowAsync<TaskCanceledException>()).Which;

        thrown.InnerException.Should().BeOfType<TimeoutException>("HttpClient reports its own timeout");
    }

    [TestMethod]
    public async Task Hang_EndsWithTheCallersCancellation()
    {
        using var handler = new ScriptedHttpMessageHandler().Hang();
        using var client = new HttpClient(handler);
        using var cancel = new CancellationTokenSource();

        Task<HttpResponseMessage> send = client.GetAsync(Endpoint, cancel.Token);
        await cancel.CancelAsync();

        await FluentActions.Awaiting(() => send).Should().ThrowAsync<OperationCanceledException>();
    }

    [TestMethod]
    public async Task RepeatLast_AnswersEveryLaterRequestWithAFreshResponse()
    {
        using var handler = new ScriptedHttpMessageHandler()
            .Respond(HttpStatusCode.Accepted)
            .Respond(HttpStatusCode.OK, "again")
            .RepeatLast();
        using var client = new HttpClient(handler);

        using HttpResponseMessage first = await client.GetAsync(Endpoint, CancellationToken);
        var later = new List<HttpResponseMessage>();
        for (int i = 0; i < 3; i++)
        {
            later.Add(await client.GetAsync(Endpoint, CancellationToken));
        }

        first.StatusCode.Should().Be(HttpStatusCode.Accepted);
        later.Should().OnlyContain(response => response.StatusCode == HttpStatusCode.OK);
        later.Select(response => response.Content).Distinct().Should().HaveCount(3, "each send gets its own response");
        foreach (HttpResponseMessage response in later)
        {
            (await response.Content.ReadAsStringAsync(CancellationToken)).Should().Be("again");
            response.Dispose();
        }

        handler.RemainingSteps.Should().Be(0);
    }

    [TestMethod]
    public void RepeatLast_OnAnEmptyScript_Throws()
    {
        using var handler = new ScriptedHttpMessageHandler();

        Action act = () => handler.RepeatLast();

        act.Should().Throw<InvalidOperationException>().WithMessage("*no step to repeat*");
    }

    [TestMethod]
    public void AddingAStepAfterRepeatLast_Throws()
    {
        using var handler = new ScriptedHttpMessageHandler().Respond(HttpStatusCode.OK).RepeatLast();

        Action respond = () => handler.Respond(HttpStatusCode.OK);
        Action repeat = () => handler.RepeatLast();

        respond.Should().Throw<InvalidOperationException>().WithMessage("*ends with a repeating step*");
        repeat.Should().Throw<InvalidOperationException>().WithMessage("*ends with a repeating step*");
    }

    [TestMethod]
    public async Task RepeatLast_AfterEarlierStepsWereConsumed_RepeatsTheLastScriptedStep()
    {
        using var handler = new ScriptedHttpMessageHandler().Respond(HttpStatusCode.Accepted);
        using var client = new HttpClient(handler);
        using HttpResponseMessage first = await client.GetAsync(Endpoint, CancellationToken);

        handler.Respond(HttpStatusCode.Gone).RepeatLast();
        using HttpResponseMessage second = await client.GetAsync(Endpoint, CancellationToken);
        using HttpResponseMessage third = await client.GetAsync(Endpoint, CancellationToken);

        (second.StatusCode, third.StatusCode).Should().Be((HttpStatusCode.Gone, HttpStatusCode.Gone));
    }

    [TestMethod]
    public async Task Requests_RecordMethodUriVersionHeadersAndBody()
    {
        using var handler = new ScriptedHttpMessageHandler().Respond(HttpStatusCode.OK);
        using var client = new HttpClient(handler);
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(Endpoint, "upload?x=1"))
        {
            Version = HttpVersion.Version20,
            Content = new StringContent("héllo", Encoding.UTF8, "text/plain"),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "token-1");
        request.Headers.Add("X-Trace", ["a", "b"]);

        using HttpResponseMessage response = await client.SendAsync(request, CancellationToken);

        RecordedHttpRequest recorded = handler.Requests.Should().ContainSingle().Subject;
        recorded.Method.Should().Be(HttpMethod.Post);
        recorded.RequestUri.Should().Be(new Uri("https://api.example.test/v1/upload?x=1"));
        recorded.Version.Should().Be(HttpVersion.Version20);
        recorded.Headers["authorization"].Should().Equal("Bearer token-1");
        recorded.Headers["X-Trace"].Should().Equal("a", "b");
        recorded.Headers["Content-Type"].Should().Equal("text/plain; charset=utf-8");
        recorded.ContentText.Should().Be("héllo");
        recorded.Content.ToArray().Should().Equal(Encoding.UTF8.GetBytes("héllo"));
        recorded.ToString().Should().Be("POST https://api.example.test/v1/upload?x=1");
    }

    [TestMethod]
    public async Task Requests_DecodeTheBodyWithItsDeclaredCharset()
    {
        using var handler = new ScriptedHttpMessageHandler().Respond(HttpStatusCode.OK);
        using var client = new HttpClient(handler);
        using var content = new ByteArrayContent(Encoding.Latin1.GetBytes("Grüße"));
        content.Headers.ContentType = new MediaTypeHeaderValue("text/plain") { CharSet = "iso-8859-1" };

        using HttpResponseMessage response = await client.PostAsync(Endpoint, content, CancellationToken);

        handler.Requests[0].ContentText.Should().Be("Grüße");
        handler.Requests[0].Content.Length.Should().Be(5);
    }

    [TestMethod]
    public async Task Requests_WithoutContent_RecordAnEmptyBody()
    {
        using var handler = new ScriptedHttpMessageHandler().Respond(HttpStatusCode.OK);
        using var client = new HttpClient(handler);

        using HttpResponseMessage response = await client.GetAsync(Endpoint, CancellationToken);

        handler.Requests[0].ContentText.Should().BeEmpty();
        handler.Requests[0].Content.IsEmpty.Should().BeTrue();
        handler.Requests[0].Headers.Should().NotContainKey("Content-Type");
    }

    [TestMethod]
    public async Task Requests_StayReadableAfterTheRequestIsDisposed()
    {
        using var handler = new ScriptedHttpMessageHandler().Respond(HttpStatusCode.OK);
        using var invoker = new HttpMessageInvoker(handler);
        var request = new HttpRequestMessage(HttpMethod.Post, Endpoint) { Content = new StringContent("kept") };

        using (HttpResponseMessage response = await invoker.SendAsync(request, CancellationToken))
        {
            request.Dispose();
        }

        handler.Requests[0].ContentText.Should().Be("kept");
    }

    [TestMethod]
    public async Task Send_WithAnAlreadyCancelledToken_NeitherRecordsNorConsumesAStep()
    {
        using var handler = new ScriptedHttpMessageHandler().Respond(HttpStatusCode.OK);
        using var invoker = new HttpMessageInvoker(handler);
        using var request = new HttpRequestMessage(HttpMethod.Get, Endpoint);

        await FluentActions
            .Awaiting(() => invoker.SendAsync(request, new CancellationToken(canceled: true)))
            .Should().ThrowAsync<TaskCanceledException>();

        handler.Requests.Should().BeEmpty();
        handler.RemainingSteps.Should().Be(1);
    }

    [TestMethod]
    public void Send_Synchronously_TakesTheNextStepToo()
    {
        using var handler = new ScriptedHttpMessageHandler().Respond(HttpStatusCode.OK, "sync");
        using var client = new HttpClient(handler);
        using var request = new HttpRequestMessage(HttpMethod.Get, Endpoint);

        using HttpResponseMessage response = client.Send(request, CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        handler.Requests.Should().ContainSingle();
    }

    [TestMethod]
    public async Task ConcurrentSends_EachGetExactlyOneStepAndAreAllRecorded()
    {
        const int Sends = 200;
        using var handler = new ScriptedHttpMessageHandler();
        for (int i = 0; i < Sends; i++)
        {
            handler.Respond(HttpStatusCode.OK, i.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }

        using var client = new HttpClient(handler);

        string[] bodies = await Task.WhenAll(Enumerable.Range(0, Sends).Select(i => Task.Run(async () =>
        {
            using HttpResponseMessage response = await client.PostAsync(
                Endpoint, new StringContent(i.ToString(System.Globalization.CultureInfo.InvariantCulture)), CancellationToken);
            return await response.Content.ReadAsStringAsync(CancellationToken);
        })));

        bodies.Should().OnlyHaveUniqueItems().And.HaveCount(Sends);
        handler.Requests.Should().HaveCount(Sends);
        handler.Requests.Select(request => request.ContentText).Should().OnlyHaveUniqueItems();
        handler.RemainingSteps.Should().Be(0);
    }

    [TestMethod]
    public void Respond_WithNullArguments_Throws()
    {
        using var handler = new ScriptedHttpMessageHandler();

#pragma warning disable CS8600, CS8625 // WHY: the guards under test are reachable only with the null the annotations forbid.
        Action content = () => handler.Respond(HttpStatusCode.OK, null);
        Action mediaType = () => handler.Respond(HttpStatusCode.OK, "x", null);
        Action factory = () => handler.Respond((Func<HttpRequestMessage, HttpResponseMessage>)null);
        Action asyncFactory = () => handler.Respond((Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>>)null);
        Action exception = () => handler.Throw(null);
#pragma warning restore CS8600, CS8625

        content.Should().Throw<ArgumentNullException>().WithParameterName("content");
        mediaType.Should().Throw<ArgumentNullException>().WithParameterName("mediaType");
        factory.Should().Throw<ArgumentNullException>().WithParameterName("respond");
        asyncFactory.Should().Throw<ArgumentNullException>().WithParameterName("respond");
        exception.Should().Throw<ArgumentNullException>().WithParameterName("exception");
        handler.RemainingSteps.Should().Be(0);
    }
}
