using System.Collections.ObjectModel;

namespace Nordstein.Core.Testing.Http.Internal;

/// <summary>
/// Copies a request into a <see cref="RecordedHttpRequest"/> for the test handlers.
/// </summary>
internal static class HttpRequestRecording
{
    /// <summary>
    /// Reads the whole body (buffering the content, so the request stays sendable) and copies the
    /// method, URI, version and headers.
    /// </summary>
    /// <param name="request">The request a handler received.</param>
    /// <param name="cancellationToken">Cancels reading the body.</param>
    /// <returns>The copy.</returns>
    public static async Task<RecordedHttpRequest> RecordAsync(this HttpRequestMessage request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var headers = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (KeyValuePair<string, IEnumerable<string>> header in request.Headers)
        {
            headers[header.Key] = [.. header.Value];
        }

        byte[] content = [];
        string contentText = string.Empty;
        if (request.Content is { } body)
        {
            content = await body.ReadAsByteArrayAsync(cancellationToken);
            contentText = await body.ReadAsStringAsync(cancellationToken);
            foreach (KeyValuePair<string, IEnumerable<string>> header in body.Headers)
            {
                headers[header.Key] = [.. header.Value];
            }
        }

        return new RecordedHttpRequest(
            request.Method, request.RequestUri, request.Version, new ReadOnlyDictionary<string, IReadOnlyList<string>>(headers), content, contentText);
    }
}
