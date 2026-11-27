using System.Net;

namespace Nordstein.Core.Common.Tests.Http.Internal;

/// <summary>
/// A proxy setting that sends every request, loopback included, through one proxy: the in-process
/// stand-in for an <c>HTTP_PROXY</c> environment variable, installed as
/// <see cref="HttpClient.DefaultProxy"/>.
/// </summary>
internal sealed class FixedWebProxy : IWebProxy
{
    private readonly Uri proxy;

    public FixedWebProxy(Uri proxy)
    {
        this.proxy = proxy;
    }

    public ICredentials? Credentials
    {
        get => null;
        set => throw new NotSupportedException("The test proxy takes no credentials.");
    }

    public Uri GetProxy(Uri destination) => proxy;

    public bool IsBypassed(Uri host) => false;
}
