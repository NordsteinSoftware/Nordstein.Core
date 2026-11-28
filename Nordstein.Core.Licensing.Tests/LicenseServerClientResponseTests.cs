using System.Net;
using AwesomeAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Nordstein.Core.Licensing.Internal;
using Nordstein.Core.Testing.Http;
using Nordstein.Core.Testing.Time;

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
        var clock = new ManualClock(Now);
        using var handler = new ScriptedHttpMessageHandler().Respond(HttpStatusCode.OK, body);
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://license.example.com/") };
        var client = new LicenseServerClient(httpClient, clock, NullLogger<LicenseServerClient>.Instance);
        return await client.CheckAsync("jti-1", "1.0.0", TestContext.CancellationToken);
    }
}
