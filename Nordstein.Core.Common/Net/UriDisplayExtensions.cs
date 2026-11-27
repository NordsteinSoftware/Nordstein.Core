namespace Nordstein.Core.Common.Net;

/// <summary>
/// Display text for URIs that may carry credentials.
/// </summary>
public static class UriDisplayExtensions
{
    /// <summary>
    /// The URI as scheme, host, port and path only, for a message, a log line or an audit record: a
    /// credential embedded in the user information (<c>https://user:key@host/v1</c>), the query
    /// (<c>?api_key=…</c>) or the fragment never appears in the result.
    /// </summary>
    /// <remarks>
    /// An absolute URI gives <see cref="UriComponents.SchemeAndServer"/> plus
    /// <see cref="UriComponents.Path"/> in <see cref="UriFormat.UriEscaped"/> form: a scheme's
    /// default port is omitted and an empty path is written as <c>/</c>
    /// (<c>https://u:p@h:8443/v1?k=s#f</c> gives <c>https://h:8443/v1</c>). A relative URI gives
    /// the fixed text <c>(relative)</c>, since nothing in it can be told apart from a secret. Pure
    /// and thread-safe.
    /// <para>
    /// The path is kept: a secret carried in the path (a webhook token such as
    /// <c>/services/T0/B0/SECRET</c>) stays, and so does the whole of a URI without an authority
    /// (<c>data:text/plain;base64,…</c>, <c>urn:…</c>). For <c>mailto:</c> only the host is kept
    /// (<c>mailto:example.com</c>).
    /// </para>
    /// </remarks>
    /// <param name="uri">The URI to display.</param>
    /// <returns>The display text; never <see langword="null"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="uri"/> is <see langword="null"/>.</exception>
    public static string ToDisplayString(this Uri uri)
    {
        ArgumentNullException.ThrowIfNull(uri);

        return uri.IsAbsoluteUri
            ? uri.GetComponents(UriComponents.SchemeAndServer | UriComponents.Path, UriFormat.UriEscaped)
            : "(relative)";
    }
}
