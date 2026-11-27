namespace Nordstein.Core.Common.Http;

/// <summary>
/// Builds the HTTP handler for an outbound adapter whose configured endpoint must be the only host
/// its requests reach: no proxy hop and no redirect hop the configuration does not show.
/// </summary>
/// <remarks>
/// Meant as the one source of handlers for every outbound adapter of a product, so a second
/// adapter cannot drift from the first.
/// </remarks>
public static class DirectHttpHandlerFactory
{
    /// <summary>
    /// Creates a new handler that sends each request only to the host its request URI names.
    /// </summary>
    /// <remarks>
    /// <list type="bullet">
    /// <item><description>
    /// <see cref="SocketsHttpHandler.UseProxy"/> is <see langword="false"/>: the process-wide
    /// <see cref="HttpClient.DefaultProxy"/>, built from the <c>HTTP_PROXY</c>, <c>HTTPS_PROXY</c>
    /// and <c>ALL_PROXY</c> environment variables (also lower case) and, on Windows and macOS, the
    /// system proxy settings, is ignored, and so is any <see cref="SocketsHttpHandler.Proxy"/>. The
    /// configured endpoint is the only hop.
    /// </description></item>
    /// <item><description>
    /// <see cref="SocketsHttpHandler.AllowAutoRedirect"/> is <see langword="false"/>: a <c>3xx</c>
    /// response reaches the caller as it is, a non-success status, and its <c>Location</c> is never
    /// requested. Following a <c>307</c> or <c>308</c> would re-send the request body to whatever
    /// host that header names.
    /// </description></item>
    /// <item><description>
    /// <see cref="SocketsHttpHandler.PooledConnectionLifetime"/> is two minutes: a pooled connection
    /// never re-resolves DNS, so recycling it lets a moved endpoint become reachable again without a
    /// restart of a process that keeps one handler for its lifetime.
    /// </description></item>
    /// <item><description>
    /// Every other setting, TLS certificate validation included, is the runtime's default.
    /// </description></item>
    /// </list>
    /// <para>
    /// Pure and safe to call from any thread: each call returns a new, unused handler, never a
    /// shared one. The caller owns it and disposes it, or hands it to a container that does. One
    /// handler is meant to live for the process and serve every <see cref="HttpClient"/> of the
    /// adapter; a handler per request exhausts sockets. The handler is safe for concurrent requests.
    /// Its properties can be changed only until its first request; changing
    /// <see cref="SocketsHttpHandler.UseProxy"/> or <see cref="SocketsHttpHandler.AllowAutoRedirect"/>
    /// voids the guarantees above.
    /// </para>
    /// </remarks>
    /// <returns>A new <see cref="SocketsHttpHandler"/>; never <see langword="null"/>.</returns>
    public static SocketsHttpHandler Create()
        => new()
        {
            UseProxy = false,
            AllowAutoRedirect = false,
            PooledConnectionLifetime = TimeSpan.FromMinutes(2),
        };
}
