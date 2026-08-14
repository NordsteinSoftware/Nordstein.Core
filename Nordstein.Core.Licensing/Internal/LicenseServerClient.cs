using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Nordstein.Core.Common.Time;

namespace Nordstein.Core.Licensing.Internal;

/// <summary>
/// Typed HTTP client for the license server's <c>/licenses/check</c> endpoint. Any non-success
/// response or transport failure is mapped to a transient "unknown" result so the caller can
/// fold it into the offline grace window rather than crashing.
/// </summary>
internal sealed class LicenseServerClient : ILicenseServerClient
{
    private readonly HttpClient httpClient;
    private readonly IClock clock;
    private readonly ILogger<LicenseServerClient> logger;

    public LicenseServerClient(HttpClient httpClient, IClock clock, ILogger<LicenseServerClient> logger)
    {
        this.httpClient = httpClient;
        this.clock = clock;
        this.logger = logger;
    }

    public async Task<LicenseCheckResult> CheckAsync(string jti, string version, CancellationToken cancellationToken)
    {
        try
        {
            using var response = await httpClient.PostAsJsonAsync(
                "/licenses/check",
                new CheckRequest(jti, version),
                cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning(
                    "License server returned {StatusCode} for check; treating as transient",
                    (int)response.StatusCode);
                return Transient();
            }

            var body = await response.Content.ReadFromJsonAsync<CheckResponse>(cancellationToken);
            if (body is null)
                return Transient();

            return new LicenseCheckResult(
                body.Status ?? LicenseCheckResult.Unknown,
                string.IsNullOrWhiteSpace(body.UpdatedTier) ? null : body.UpdatedTier,
                body.UpdatedLimits,
                body.CheckedAt ?? clock.UtcNow);
        }
        catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            // HttpClient.Timeout surfaces as TaskCanceledException even though nobody cancelled.
            // A hung license server must fold into the offline grace window like any other
            // transport failure — never escape into the background loop (where an unhandled
            // exception would stop the host). Caller cancellation still propagates.
            logger.LogWarning(ex, "License server check timed out; treating as transient");
            return Transient();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "License server check failed; treating as transient");
            return Transient();
        }
    }

    private LicenseCheckResult Transient()
        => new(LicenseCheckResult.Unknown, UpdatedTier: null, UpdatedLimits: null, clock.UtcNow);

    private sealed record CheckRequest(
        [property: JsonPropertyName("jti")] string Jti,
        [property: JsonPropertyName("version")] string Version);

    private sealed record CheckResponse
    {
        [JsonPropertyName("status")]
        public string? Status { get; init; }

        [JsonPropertyName("updatedTier")]
        public string? UpdatedTier { get; init; }

        [JsonPropertyName("updatedLimits")]
        public Dictionary<string, long>? UpdatedLimits { get; init; }

        [JsonPropertyName("checkedAt")]
        public DateTimeOffset? CheckedAt { get; init; }
    }
}
