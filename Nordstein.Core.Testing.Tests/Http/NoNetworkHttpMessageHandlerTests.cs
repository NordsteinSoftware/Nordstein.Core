using System.Net;
using Autofac;
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Nordstein.Core.Testing.Http;

namespace Nordstein.Core.Testing.Tests.Http;

/// <summary>
/// <see cref="NoNetworkHttpMessageHandler"/>: every send fails with an exception transport-failure
/// handling does not catch, and is recorded, so a test proves no call left even when the code under
/// test swallowed the failure.
/// </summary>
[TestClass]
public sealed class NoNetworkHttpMessageHandlerTests : BaseTest<Module>
{
    private static readonly Uri Endpoint = new("https://api.example.test/v1/things");

    [TestMethod]
    public async Task SendAsync_ThrowsInvalidOperationNamingTheRequest()
    {
        using var handler = new NoNetworkHttpMessageHandler();
        using var client = new HttpClient(handler);

        Exception thrown = (await FluentActions
            .Awaiting(() => client.PostAsync(Endpoint, new StringContent("secret"), CancellationToken))
            .Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*POST https://api.example.test/v1/things*")).Which;

        thrown.Should().NotBeAssignableTo<HttpRequestException>("transport-failure handling must not swallow the guard");
    }

    [TestMethod]
    public async Task Attempts_RecordEverySendWithItsBody()
    {
        using var handler = new NoNetworkHttpMessageHandler();
        using var client = new HttpClient(handler);

        await FluentActions.Awaiting(() => client.PostAsync(Endpoint, new StringContent("secret"), CancellationToken))
            .Should().ThrowAsync<InvalidOperationException>();
        await FluentActions.Awaiting(() => client.GetAsync(Endpoint, CancellationToken))
            .Should().ThrowAsync<InvalidOperationException>();

        handler.Attempts.Select(attempt => attempt.ToString()).Should().Equal(
            "POST https://api.example.test/v1/things",
            "GET https://api.example.test/v1/things");
        handler.Attempts[0].ContentText.Should().Be("secret");
    }

    [TestMethod]
    public async Task Attempts_WhenTheCodeUnderTestSwallowsTheFailure_StillShowTheCall()
    {
        using var handler = new NoNetworkHttpMessageHandler();
        using var client = new HttpClient(handler);

        await SwallowingCall(client);

        handler.Attempts.Should().ContainSingle();
    }

    [TestMethod]
    public void Attempts_WhenNothingWasSent_AreEmpty()
    {
        using var handler = new NoNetworkHttpMessageHandler();

        handler.Attempts.Should().BeEmpty();
    }

    [TestMethod]
    public async Task SendAsync_WithAnAlreadyCancelledToken_StillFailsAsAnAttempt()
    {
        // A cancelled send is still an attempt to reach the network: the guard must not read as
        // an ordinary cancellation.
        using var handler = new NoNetworkHttpMessageHandler();
        using var invoker = new HttpMessageInvoker(handler);
        using var request = new HttpRequestMessage(HttpMethod.Get, Endpoint);

        await FluentActions
            .Awaiting(() => invoker.SendAsync(request, new CancellationToken(canceled: true)))
            .Should().ThrowAsync<InvalidOperationException>();

        handler.Attempts.Should().ContainSingle();
    }

    [TestMethod]
    public void Send_Synchronously_ThrowsAndRecords()
    {
        using var handler = new NoNetworkHttpMessageHandler();
        using var client = new HttpClient(handler);
        using var request = new HttpRequestMessage(HttpMethod.Delete, Endpoint);

        Action act = () => client.Send(request, CancellationToken);

        act.Should().Throw<InvalidOperationException>().WithMessage("*DELETE https://api.example.test/v1/things*");
        handler.Attempts.Should().ContainSingle();
    }

    [TestMethod]
    public async Task RegisteredAsTheContainersHandler_GuardsEveryClientBuiltOnIt()
    {
        var handler = new NoNetworkHttpMessageHandler();
        IServiceProvider services = GetServices(builder =>
        {
            builder.RegisterInstance(handler).As<HttpMessageHandler>();
            builder.Register(context => new HttpClient(context.Resolve<HttpMessageHandler>(), disposeHandler: false));
        });

        using HttpClient client = services.GetRequiredService<HttpClient>();
        await FluentActions.Awaiting(() => client.GetAsync(Endpoint, CancellationToken))
            .Should().ThrowAsync<InvalidOperationException>();

        handler.Attempts.Should().ContainSingle();
    }

    [TestMethod]
    public async Task ConcurrentSends_AreAllRecorded()
    {
        using var handler = new NoNetworkHttpMessageHandler();
        using var client = new HttpClient(handler);

        await Task.WhenAll(Enumerable.Range(0, 100).Select(_ => Task.Run(async () =>
        {
            try
            {
                using HttpResponseMessage response = await client.GetAsync(Endpoint, CancellationToken);
            }
            catch (InvalidOperationException)
            {
                // expected: the guard
            }
        })));

        handler.Attempts.Should().HaveCount(100);
    }

    private static async Task<HttpStatusCode?> SwallowingCall(HttpClient client)
    {
        try
        {
            using HttpResponseMessage response = await client.GetAsync(Endpoint);
            return response.StatusCode;
        }
        catch (Exception)
        {
            return null;
        }
    }
}
