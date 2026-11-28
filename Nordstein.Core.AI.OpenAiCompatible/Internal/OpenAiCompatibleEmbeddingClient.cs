using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Nordstein.Core.AI.Clients;
using Nordstein.Core.AI.Embeddings;
using Nordstein.Core.Common.Net;

namespace Nordstein.Core.AI.OpenAiCompatible.Internal;

/// <summary>
/// <c>POST {BaseUrl}/embeddings</c> with <c>{model, input[], encoding_format:"float"}</c>, plus
/// <c>dimensions</c> when the options ask for it. Batches are sent one after the other: bounding
/// concurrency is the endpoint's job.
/// </summary>
/// <remarks>
/// Transport failures, time-outs and error statuses are <see cref="InferenceUnavailableException"/>;
/// a request the endpoint rejects as such and a reply of the wrong shape are
/// <see cref="InferenceContractException"/>. Messages carry the endpoint's display form and the
/// status only: never user information, the key, a transport or parser message, or the inputs.
/// Stateless: the handler is shared and owned by the container.
/// </remarks>
internal sealed class OpenAiCompatibleEmbeddingClient : IEmbeddingClient
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly OpenAiCompatibleEmbeddingOptions options;
    private readonly HttpMessageInvoker http;
    private readonly TimeProvider timeProvider;
    private readonly Uri embeddingsUri;

    // The embeddings URI without user information, query or fragment: the only form a message carries.
    private readonly string endpoint;

    public OpenAiCompatibleEmbeddingClient(OpenAiCompatibleEmbeddingOptions options, HttpMessageHandler handler, TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(handler);
        ArgumentNullException.ThrowIfNull(timeProvider);
        options.Validate();
        this.options = options;
        this.timeProvider = timeProvider;

        // The invoker does not own the handler, so there is nothing of its own to dispose.
        http = new HttpMessageInvoker(handler, disposeHandler: false);
        embeddingsUri = new Uri(options.BaseUrl.ToString().TrimEnd('/') + "/embeddings");
        endpoint = embeddingsUri.ToDisplayString();
    }

    public string ModelId => options.Model;

    public int Dimension => options.Dimension;

    public async Task<IReadOnlyList<ReadOnlyMemory<float>>> EmbedAsync(IReadOnlyList<string> inputs, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(inputs);
        if (inputs.Any(input => input is null))
        {
            throw new ArgumentException("An input is null.", nameof(inputs));
        }

        var vectors = new List<ReadOnlyMemory<float>>(inputs.Count);
        foreach (string[] batch in inputs.Chunk(options.BatchSize))
        {
            vectors.AddRange(await EmbedBatchAsync(batch, cancellationToken).ConfigureAwait(false));
        }

        return vectors;
    }

    private async Task<ReadOnlyMemory<float>[]> EmbedBatchAsync(string[] batch, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, embeddingsUri);
        request.Content = JsonContent.Create(
            new EmbeddingRequest(options.Model, batch, "float", options.SendDimension ? options.Dimension : null), options: Json);
        if (!string.IsNullOrEmpty(options.ApiKey))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", options.ApiKey);
        }

        // One deadline for the request and its body, on the container's clock.
        using var timeout = new CancellationTokenSource(options.Timeout, timeProvider);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);
        try
        {
            using HttpResponseMessage response = await http.SendAsync(request, linked.Token).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                throw StatusFailure(response);
            }

            EmbeddingResponse? parsed = await response.Content.ReadFromJsonAsync<EmbeddingResponse>(Json, linked.Token).ConfigureAwait(false);
            return Order(parsed, batch.Length);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (exception is HttpRequestException or OperationCanceledException or IOException)
        {
            // The exception message is not forwarded; the inner exception carries transport detail only.
            throw new InferenceUnavailableException($"The embedding endpoint {endpoint} could not be reached or did not answer in time.", exception);
        }
        // WHY: ReadFromJsonAsync does not check the media type, so a proxy's login page fails as
        // malformed JSON; an unusable charset throws InvalidOperationException or ArgumentException,
        // and NotSupportedException covers a content it cannot read. Each is a reply of the wrong
        // shape, the same permanent failure as malformed JSON.
        catch (Exception exception) when (exception is JsonException or NotSupportedException or InvalidOperationException or ArgumentException)
        {
            // The exception message is not forwarded: it can echo the reply.
            throw new InferenceContractException($"The embedding endpoint {endpoint} answered with something that is not an embeddings response.");
        }
    }

    private Exception StatusFailure(HttpResponseMessage response)
    {
        string status = $"{(int)response.StatusCode} {response.ReasonPhrase}".TrimEnd();

        // The request itself was rejected (too long, malformed for this model): the same input
        // fails the same way every time, so this is not an outage.
        return response.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.RequestEntityTooLarge or HttpStatusCode.UnprocessableEntity
            ? new InferenceContractException($"The embedding endpoint {endpoint} rejected the request: {status}.")
            : new InferenceUnavailableException($"The embedding endpoint {endpoint} answered {status}.");
    }

    private ReadOnlyMemory<float>[] Order(EmbeddingResponse? parsed, int expected)
    {
        if (parsed?.Data is null || parsed.Data.Count != expected)
        {
            throw new InferenceContractException(
                $"The embedding endpoint {endpoint} answered {parsed?.Data?.Count ?? 0} embeddings for {expected} inputs.");
        }

        var ordered = new ReadOnlyMemory<float>[expected];
        bool[] seen = new bool[expected];
        foreach (EmbeddingItem? item in parsed.Data)
        {
            if (item is null || item.Index < 0 || item.Index >= expected || seen[item.Index])
            {
                throw new InferenceContractException(
                    $"The embedding endpoint {endpoint} answered an embedding whose index is missing, out of range or repeated.");
            }

            if (item.Embedding is null || item.Embedding.Length != options.Dimension)
            {
                throw new InferenceContractException(
                    $"The embedding endpoint {endpoint} answered embedding {item.Index} with {item.Embedding?.Length ?? 0} dimensions; the configured model has {options.Dimension}.");
            }

            if (item.Embedding.Any(value => !float.IsFinite(value)))
            {
                throw new InferenceContractException(
                    $"The embedding endpoint {endpoint} answered embedding {item.Index} with a component that is not finite.");
            }

            ordered[item.Index] = item.Embedding;
            seen[item.Index] = true;
        }

        return ordered;
    }

    private sealed record EmbeddingRequest(
        [property: JsonPropertyName("model")] string Model,
        [property: JsonPropertyName("input")] string[] Input,
        [property: JsonPropertyName("encoding_format")] string EncodingFormat,
        [property: JsonPropertyName("dimensions"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? Dimensions);

    private sealed record EmbeddingResponse([property: JsonPropertyName("data")] List<EmbeddingItem?>? Data);

    private sealed record EmbeddingItem(
        [property: JsonPropertyName("index")] int Index,
        [property: JsonPropertyName("embedding")] float[]? Embedding);
}
