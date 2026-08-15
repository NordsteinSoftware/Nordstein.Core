using System.Net;
using System.Text;
using AwesomeAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Nordstein.Core.Licensing.Internal;

namespace Nordstein.Core.Licensing.Tests;

/// <summary>
/// Remaining <see cref="LicenseServerClient"/> branches: a 2xx response whose JSON body is the
/// literal <c>null</c> (empty payload) folds into a transient result, and the request body is
/// serialized with both the <c>jti</c> and <c>version</c> fields.
/// </summary>
[TestClass]
public sealed class LicenseServerClientEdgeCaseTests
{
    public required TestContext TestContext { get; init; }

    private static LicenseServerClient Create(HttpMessageHandler handler)
    {
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://license.example.com/") };
        var clock = new MutableClock(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
        return new LicenseServerClient(httpClient, clock, NullLogger<LicenseServerClient>.Instance);
    }

    [TestMethod]
    public async Task CheckAsync_SuccessWithNullJsonBody_ReturnsUnknownTransient()
    {
        // A 200 whose body deserializes to null (e.g. an empty/`null` payload) must not throw — it
        // folds into the offline grace window like any other transient outcome.
        var handler = new StubHttpMessageHandler(HttpStatusCode.OK, "null");

        var result = await Create(handler).CheckAsync("jti-1", "1.0.0", TestContext.CancellationToken);

        result.Status.Should().Be(LicenseCheckResult.Unknown);
        result.UpdatedTier.Should().BeNull();
        result.UpdatedLimits.Should().BeNull();
    }

    [TestMethod]
    public async Task CheckAsync_SerializesRequestBody_WithJtiAndVersion()
    {
        // The request payload must carry the license jti and the app version so the server can
        // resolve the correct license — pinning the CheckRequest wire shape.
        var handler = new BodyCapturingHandler("{\"status\":\"valid\"}");

        var result = await Create(handler).CheckAsync("jti-42", "9.9.9", TestContext.CancellationToken);

        result.Status.Should().Be(LicenseCheckResult.Valid);
        handler.CapturedBody.Should().NotBeNull();
        handler.CapturedBody.Should().Contain("\"jti\":\"jti-42\"");
        handler.CapturedBody.Should().Contain("\"version\":\"9.9.9\"");
    }

    /// <summary>
    /// Reads the request body (forcing content serialization) before returning a fixed response.
    /// </summary>
    private sealed class BodyCapturingHandler : HttpMessageHandler
    {
        private readonly string responseBody;

        public BodyCapturingHandler(string responseBody) => this.responseBody = responseBody;

        public string? CapturedBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            if (request.Content is { } content)
                CapturedBody = await content.ReadAsStringAsync(cancellationToken);

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(responseBody, Encoding.UTF8, "application/json"),
            };
        }
    }
}
