using System.Net;
using System.Text;
using AwesomeAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Nordstein.Core.Common.Time;
using Nordstein.Core.Licensing.Internal;

namespace Nordstein.Core.Licensing.Tests;

/// <summary>
/// How <see cref="LicenseServerClient"/> reads a 2xx check response with optional members left out: a missing status is
/// <c>unknown</c>, a blank tier is no tier change, and a missing check time is the clock's now.
/// </summary>
[TestClass]
public sealed class LicenseServerClientResponseTests
{
    private static readonly DateTimeOffset Now = new(2026, 3, 1, 12, 0, 0, TimeSpan.Zero);

    public required TestContext TestContext { get; init; }

    [TestMethod]
    public async Task CheckAsync_ResponseWithoutStatusOrCheckTime_IsUnknownCheckedNow()
    {
        LicenseCheckResult result = await CheckAsync("""{"updatedTier":"Premium"}""");

        result.Should().Be(new LicenseCheckResult(LicenseCheckResult.Unknown, "Premium", null, Now));
    }

    [TestMethod]
    [DataRow("""{"status":"valid","updatedTier":""}""")]
    [DataRow("""{"status":"valid","updatedTier":"   "}""")]
    [DataRow("""{"status":"valid","updatedTier":null}""")]
    public async Task CheckAsync_BlankTier_IsNoTierChange(string body)
    {
        LicenseCheckResult result = await CheckAsync(body);

        result.Status.Should().Be(LicenseCheckResult.Valid);
        result.UpdatedTier.Should().BeNull();
    }

    [TestMethod]
    public async Task CheckAsync_ResponseWithEveryMember_ReadsThemAll()
    {
        LicenseCheckResult result = await CheckAsync("""{"status":"revoked","updatedTier":"Basic","updatedLimits":{"MaxUsers":5},"checkedAt":"2026-02-01T08:00:00+00:00"}""");

        result.Status.Should().Be(LicenseCheckResult.Revoked);
        result.UpdatedTier.Should().Be("Basic");
        result.UpdatedLimits.Should().BeEquivalentTo(new Dictionary<string, long> { ["MaxUsers"] = 5 });
        result.CheckedAt.Should().Be(new DateTimeOffset(2026, 2, 1, 8, 0, 0, TimeSpan.Zero));
    }

    private async Task<LicenseCheckResult> CheckAsync(string body)
    {
        var clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(Now);
        using var handler = new FixedResponseHandler(body);
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://license.example.com/") };
        var client = new LicenseServerClient(httpClient, clock, NullLogger<LicenseServerClient>.Instance);
        return await client.CheckAsync("jti-1", "1.0.0", TestContext.CancellationToken);
    }

    /// <summary>
    /// Answers every request with 200 and a fixed JSON body.
    /// </summary>
    private sealed class FixedResponseHandler : HttpMessageHandler
    {
        private readonly string body;

        public FixedResponseHandler(string body)
        {
            this.body = body;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") });
    }
}
