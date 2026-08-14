using System.Net;
using System.Text;

namespace Nordstein.Core.Licensing.Tests;

/// <summary>
/// Returns a preconfigured response (or throws) for any request, capturing the last request URI.
/// </summary>
internal sealed class StubHttpMessageHandler : HttpMessageHandler
{
    private readonly HttpStatusCode statusCode;
    private readonly string body;
    private readonly bool throwTransport;
    private readonly bool throwTimeout;

    public StubHttpMessageHandler(HttpStatusCode statusCode, string body)
    {
        this.statusCode = statusCode;
        this.body = body;
    }

    private StubHttpMessageHandler(bool throwTransport, bool throwTimeout)
    {
        this.throwTransport = throwTransport;
        this.throwTimeout = throwTimeout;
        this.body = string.Empty;
    }

    public static StubHttpMessageHandler Faulting() => new(throwTransport: true, throwTimeout: false);

    /// <summary>
    /// Simulates HttpClient's client-side timeout, which surfaces as a
    /// <see cref="TaskCanceledException"/> even though no caller cancelled.
    /// </summary>
    public static StubHttpMessageHandler TimingOut() => new(throwTransport: false, throwTimeout: true);

    public Uri? LastRequestUri { get; private set; }

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        LastRequestUri = request.RequestUri;

        if (throwTransport)
            throw new HttpRequestException("simulated transport failure");

        if (throwTimeout)
            throw new TaskCanceledException("simulated client timeout", new TimeoutException());

        return Task.FromResult(new HttpResponseMessage(statusCode)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        });
    }
}
