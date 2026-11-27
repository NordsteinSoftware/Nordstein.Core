namespace Nordstein.Core.Testing.Http;

/// <summary>
/// A copy of one request a test handler received, taken before the handler answered: method, URI,
/// version, every header and the whole body. It holds no reference to the
/// <see cref="HttpRequestMessage"/>, so it stays readable after the caller disposed the request.
/// </summary>
/// <remarks>Immutable and safe to read from any thread.</remarks>
public sealed class RecordedHttpRequest
{
    internal RecordedHttpRequest(
        HttpMethod method,
        Uri? requestUri,
        Version version,
        IReadOnlyDictionary<string, IReadOnlyList<string>> headers,
        ReadOnlyMemory<byte> content,
        string contentText)
    {
        Method = method;
        RequestUri = requestUri;
        Version = version;
        Headers = headers;
        Content = content;
        ContentText = contentText;
    }

    /// <summary>The request's method.</summary>
    public HttpMethod Method { get; }

    /// <summary>The request's absolute URI as the handler received it (an <see cref="HttpClient"/> base address applied); null when the request had none.</summary>
    public Uri? RequestUri { get; }

    /// <summary>The request's HTTP version.</summary>
    public Version Version { get; }

    /// <summary>
    /// The request headers and the content headers (<c>Content-Type</c>, <c>Content-Length</c>, …)
    /// in one map, looked up case-insensitively; each header keeps its values in order.
    /// </summary>
    public IReadOnlyDictionary<string, IReadOnlyList<string>> Headers { get; }

    /// <summary>The body's bytes; empty when the request had no content.</summary>
    public ReadOnlyMemory<byte> Content { get; }

    /// <summary>
    /// The body as text, decoded with the content's declared charset (UTF-8 when it declares
    /// none); empty when the request had no content.
    /// </summary>
    public string ContentText { get; }

    /// <summary>The method and URI, as in <c>POST https://host/path</c>.</summary>
    public override string ToString() => $"{Method} {RequestUri}";
}
