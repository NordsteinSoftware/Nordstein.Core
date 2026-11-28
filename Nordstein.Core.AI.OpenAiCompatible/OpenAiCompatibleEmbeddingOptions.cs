using System.Net;

namespace Nordstein.Core.AI.OpenAiCompatible;

/// <summary>
/// One embedding model behind an endpoint that speaks the OpenAI embeddings wire format.
/// </summary>
/// <remarks>
/// <see cref="ToString"/> never prints <see cref="ApiKey"/>, so an options object is safe in a log
/// line or an exception message.
/// </remarks>
public sealed record OpenAiCompatibleEmbeddingOptions
{
    /// <summary>
    /// The largest <see cref="BatchSize"/>.
    /// </summary>
    public const int MaxBatchSize = 2048;

    /// <summary>
    /// The API base URL the path <c>/embeddings</c> is appended to, for example
    /// <c>https://api.example.com/v1</c>: absolute, <c>https</c> or <c>http</c>, without user
    /// information, query or fragment.
    /// </summary>
    public required Uri BaseUrl { get; init; }

    /// <summary>
    /// The bearer token, or null or empty for an endpoint without authentication. Sent only over
    /// <c>https</c> or to a loopback host.
    /// </summary>
    public string? ApiKey { get; init; }

    /// <summary>
    /// The model name sent in the request. It is also the client's
    /// <see cref="Embeddings.IEmbeddingClient.ModelId"/>.
    /// </summary>
    public required string Model { get; init; }

    /// <summary>
    /// The number of floats in every vector the model returns; a reply of another length is refused.
    /// </summary>
    public required int Dimension { get; init; }

    /// <summary>
    /// Whether the request carries <c>dimensions</c>, for a model that can shorten its vectors on
    /// request. False for a model with one fixed dimension: some endpoints refuse the parameter.
    /// </summary>
    public bool SendDimension { get; init; }

    /// <summary>
    /// How many inputs one request carries, 1 to <see cref="MaxBatchSize"/>. The default is 32.
    /// </summary>
    public int BatchSize { get; init; } = 32;

    /// <summary>
    /// How long one request may take, response body included. The default is 60 seconds.
    /// </summary>
    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(60);

    /// <summary>
    /// Throws when a value is unusable.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// A value is missing or out of range, or an API key would travel over plain <c>http</c> to a
    /// host that is not loopback. The message never contains the key or the URL.
    /// </exception>
    public void Validate()
    {
        // WHY the null checks: a required member set from configuration binding can still be null.
        if (BaseUrl is null
            || !BaseUrl.IsAbsoluteUri
            || (BaseUrl.Scheme != Uri.UriSchemeHttps && BaseUrl.Scheme != Uri.UriSchemeHttp)
            || BaseUrl.UserInfo.Length != 0
            || BaseUrl.Query.Length != 0
            || BaseUrl.Fragment.Length != 0)
        {
            throw new InvalidOperationException(
                $"{nameof(BaseUrl)} must be an absolute http or https URL without user information, query or fragment.");
        }

        if (!string.IsNullOrEmpty(ApiKey) && BaseUrl.Scheme != Uri.UriSchemeHttps && !IsLoopback(BaseUrl))
        {
            throw new InvalidOperationException(
                $"{nameof(ApiKey)} is sent only over https or to a loopback host; {nameof(BaseUrl)} is plain http to another host.");
        }

        if (string.IsNullOrWhiteSpace(Model))
        {
            throw new InvalidOperationException($"{nameof(Model)} must name the embedding model.");
        }

        if (Dimension <= 0)
        {
            throw new InvalidOperationException($"{nameof(Dimension)} must be positive.");
        }

        if (BatchSize is <= 0 or > MaxBatchSize)
        {
            throw new InvalidOperationException($"{nameof(BatchSize)} must be 1 to {MaxBatchSize}.");
        }

        if (Timeout <= TimeSpan.Zero)
        {
            throw new InvalidOperationException($"{nameof(Timeout)} must be positive.");
        }
    }

    /// <summary>
    /// The options without the API key.
    /// </summary>
    public override string ToString()
        => $"{nameof(OpenAiCompatibleEmbeddingOptions)} {{ {nameof(Model)} = {Model}, {nameof(Dimension)} = {Dimension}, "
            + $"{nameof(SendDimension)} = {SendDimension}, {nameof(BatchSize)} = {BatchSize}, {nameof(Timeout)} = {Timeout}, "
            + $"{nameof(ApiKey)} = {(string.IsNullOrEmpty(ApiKey) ? "none" : "set")} }}";

    private static bool IsLoopback(Uri url)
        => url.IsLoopback || (IPAddress.TryParse(url.Host, out IPAddress? address) && IPAddress.IsLoopback(address));
}
