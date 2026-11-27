using System.Net;
using System.Text;
using AwesomeAssertions;
using Nordstein.Core.Common.Http;
using Nordstein.Core.Common.Tests.Http.Internal;
using Nordstein.Core.Testing;

namespace Nordstein.Core.Common.Tests.Http;

/// <summary>
/// The handler's contract, proved on the wire against loopback servers: no proxy, no redirect,
/// connections recycled every two minutes. Nothing leaves <c>127.0.0.1</c>.
/// </summary>
[TestClass]
public sealed class DirectHttpHandlerFactoryTests : BaseTest<Module>
{
    [TestMethod]
    public void Handler_Settings_ProxyAndRedirectOff()
    {
        using SocketsHttpHandler handler = DirectHttpHandlerFactory.Create();

        handler.UseProxy.Should().BeFalse();
        handler.AllowAutoRedirect.Should().BeFalse();
    }

    [TestMethod]
    [DoNotParallelize] // WHY: swaps the process-wide HttpClient.DefaultProxy for the test's duration.
    public async Task Handler_ProxyEnvironmentSet_ConnectsDirectly()
    {
        await using var target = new LoopbackHttpServer(Response(200, "direct"));
        await using var proxy = new LoopbackHttpServer(Response(200, "proxy"));
        using SocketsHttpHandler handler = DirectHttpHandlerFactory.Create();

        string body = await GetThroughDefaultProxyAsync(handler, target.BaseAddress, proxy.BaseAddress);

        body.Should().Be("direct", "HTTP(S)_PROXY, here the process-wide default proxy, is ignored");
        target.RequestLines.Should().Equal("GET / HTTP/1.1");
        proxy.RequestLines.Should().BeEmpty();
    }

    [TestMethod]
    [DoNotParallelize] // WHY: swaps the process-wide HttpClient.DefaultProxy for the test's duration.
    public async Task DefaultSocketsHandler_ProxyEnvironmentSet_GoesThroughProxy()
    {
        // Positive control: the same setup routes a default handler through the proxy, so the test
        // above cannot pass because the swapped default proxy had no effect.
        await using var target = new LoopbackHttpServer(Response(200, "direct"));
        await using var proxy = new LoopbackHttpServer(Response(200, "proxy"));
        using var handler = new SocketsHttpHandler();

        string body = await GetThroughDefaultProxyAsync(handler, target.BaseAddress, proxy.BaseAddress);

        body.Should().Be("proxy");
        proxy.RequestLines.Should().Equal($"GET {target.BaseAddress} HTTP/1.1");
        target.RequestLines.Should().BeEmpty();
    }

    [TestMethod]
    [DataRow(301)]
    [DataRow(302)]
    [DataRow(303)]
    [DataRow(307)]
    [DataRow(308)]
    public async Task Handler_RedirectResponse_ReturnedNotFollowed(int status)
    {
        await using var elsewhere = new LoopbackHttpServer(Response(200, "elsewhere"));
        await using var endpoint = new LoopbackHttpServer(
            Response(status, string.Empty, $"Location: {elsewhere.BaseAddress}moved"));
        using SocketsHttpHandler handler = DirectHttpHandlerFactory.Create();
        using var client = new HttpClient(handler, disposeHandler: false) { Timeout = TimeSpan.FromSeconds(30) };
        using var content = new StringContent("request body");

        using HttpResponseMessage response = await client.PostAsync(endpoint.BaseAddress, content, CancellationToken);

        ((int)response.StatusCode).Should().Be(status, "a 3xx reaches the caller as a non-success status");
        response.IsSuccessStatusCode.Should().BeFalse();
        endpoint.RequestLines.Should().Equal("POST / HTTP/1.1");
        elsewhere.RequestLines.Should().BeEmpty("the body is never re-sent to the Location host");
    }

    [TestMethod]
    public void Handler_PooledConnectionLifetime_IsTwoMinutes()
    {
        using SocketsHttpHandler handler = DirectHttpHandlerFactory.Create();

        handler.PooledConnectionLifetime.Should().Be(TimeSpan.FromMinutes(2));
    }

    [TestMethod]
    public void Create_CalledTwice_ReturnsSeparateHandlers()
    {
        using SocketsHttpHandler first = DirectHttpHandlerFactory.Create();
        using SocketsHttpHandler second = DirectHttpHandlerFactory.Create();

        second.Should().NotBeSameAs(first, "each caller owns and disposes its own handler");
    }

    /// <summary>
    /// Sends <c>GET</c> to <paramref name="target"/> through <paramref name="handler"/> while the
    /// process-wide default proxy, which a handler with <c>UseProxy</c> on reads from
    /// <c>HTTP(S)_PROXY</c>, points at <paramref name="proxy"/>; restores it afterwards.
    /// </summary>
    private async Task<string> GetThroughDefaultProxyAsync(SocketsHttpHandler handler, Uri target, Uri proxy)
    {
        IWebProxy original = HttpClient.DefaultProxy;
        HttpClient.DefaultProxy = new FixedWebProxy(proxy);
        try
        {
            using var client = new HttpClient(handler, disposeHandler: false) { Timeout = TimeSpan.FromSeconds(30) };
            return await client.GetStringAsync(target, CancellationToken);
        }
        finally
        {
            HttpClient.DefaultProxy = original;
        }
    }

    private static string Response(int status, string body, params string[] headers)
        => $"HTTP/1.1 {status} Test\r\n"
            + string.Concat(headers.Select(header => header + "\r\n"))
            + $"Content-Length: {Encoding.ASCII.GetByteCount(body)}\r\nConnection: close\r\n\r\n{body}";
}
