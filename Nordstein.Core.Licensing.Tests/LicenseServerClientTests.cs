using System.Net;
using AwesomeAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Nordstein.Core.Licensing.Internal;
using Nordstein.Core.Testing.Http;
using Nordstein.Core.Testing.Time;

namespace Nordstein.Core.Licensing.Tests;

[TestClass]
public sealed class LicenseServerClientTests
{
    public required TestContext TestContext { get; init; }

    private readonly ManualClock clock = new(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));

    private LicenseServerClient Create(HttpMessageHandler handler)
    {
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://license.example.com/") };
        return new LicenseServerClient(httpClient, clock, NullLogger<LicenseServerClient>.Instance);
    }

    [TestMethod]
    public async Task CheckAsync_ValidResponse_ParsesStatusAndRawNames()
    {
        var handler = new ScriptedHttpMessageHandler().Respond(
            HttpStatusCode.OK,
            "{\"status\":\"valid\",\"updatedTier\":\"Premium\",\"updatedLimits\":{\"MaxUsers\":42}}");

        var result = await Create(handler).CheckAsync("jti-1", "1.0.0", TestContext.CancellationToken);

        result.Status.Should().Be(LicenseCheckResult.Valid);
        result.UpdatedTier.Should().Be("Premium");
        result.UpdatedLimits.Should().ContainKey("MaxUsers");
        result.UpdatedLimits["MaxUsers"].Should().Be(42);
        handler.Requests.Should().ContainSingle().Which.RequestUri?.AbsolutePath.Should().Be("/licenses/check");
    }

    [TestMethod]
    public async Task CheckAsync_RevokedResponse_ParsesRevoked()
    {
        var handler = new ScriptedHttpMessageHandler().Respond(HttpStatusCode.OK, "{\"status\":\"revoked\"}");

        var result = await Create(handler).CheckAsync("jti-1", "1.0.0", TestContext.CancellationToken);

        result.Status.Should().Be(LicenseCheckResult.Revoked);
    }

    [TestMethod]
    public async Task CheckAsync_ServerError_ReturnsUnknownTransient()
    {
        var handler = new ScriptedHttpMessageHandler().Respond(HttpStatusCode.InternalServerError, "{}");

        var result = await Create(handler).CheckAsync("jti-1", "1.0.0", TestContext.CancellationToken);

        result.Status.Should().Be(LicenseCheckResult.Unknown);
    }

    [TestMethod]
    public async Task CheckAsync_TransportFailure_ReturnsUnknownTransient()
    {
        var result = await Create(new ScriptedHttpMessageHandler().Throw(new HttpRequestException("simulated transport failure")))
            .CheckAsync("jti-1", "1.0.0", TestContext.CancellationToken);

        result.Status.Should().Be(LicenseCheckResult.Unknown);
    }

    [TestMethod]
    public async Task CheckAsync_ClientTimeout_ReturnsUnknownTransient()
    {
        // HttpClient.Timeout surfaces as TaskCanceledException without anyone cancelling. A hung
        // license server must read as transient — an escaping exception here would bubble out of
        // the background loop and stop the consuming host.
        var result = await Create(new ScriptedHttpMessageHandler().TimeOut())
            .CheckAsync("jti-1", "1.0.0", TestContext.CancellationToken);

        result.Status.Should().Be(LicenseCheckResult.Unknown);
    }

    [TestMethod]
    public async Task CheckAsync_CallerCancellation_Propagates()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await FluentActions
            .Invoking(() => Create(new ScriptedHttpMessageHandler().Respond(HttpStatusCode.OK, "{}"))
                .CheckAsync("jti-1", "1.0.0", cts.Token))
            .Should().ThrowAsync<OperationCanceledException>();
    }
}
