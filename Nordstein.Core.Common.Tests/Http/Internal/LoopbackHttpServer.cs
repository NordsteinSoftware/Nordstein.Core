using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace Nordstein.Core.Common.Tests.Http.Internal;

/// <summary>
/// A one-response HTTP/1.1 server on <c>127.0.0.1</c> and a free port: it answers every request with
/// the fixed raw response it was built with and records each request line. Built and disposed inside
/// one test method.
/// </summary>
/// <remarks>
/// Connections are served one at a time; the request head and a <c>Content-Length</c> body are read
/// before the response is written, so the client never sees a reset. The response should carry
/// <c>Connection: close</c>: the server closes every connection after one response. A failure while
/// serving faults the accept loop and surfaces from <see cref="DisposeAsync"/>.
/// </remarks>
internal sealed class LoopbackHttpServer : IAsyncDisposable
{
    private const int MaxHeadBytes = 64 * 1024;

    private static readonly byte[] HeadEnd = "\r\n\r\n"u8.ToArray();

    private readonly TcpListener listener;
    private readonly byte[] response;
    private readonly ConcurrentQueue<string> requestLines = new();
    private readonly CancellationTokenSource stopping = new();
    private readonly Task serving;

    public LoopbackHttpServer(string response)
    {
        this.response = Encoding.ASCII.GetBytes(response);
        listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        BaseAddress = new Uri($"http://127.0.0.1:{((IPEndPoint)listener.LocalEndpoint).Port}/");
        serving = ServeAsync(stopping.Token);
    }

    /// <summary>
    /// <c>http://127.0.0.1:&lt;port&gt;/</c>.
    /// </summary>
    public Uri BaseAddress { get; }

    /// <summary>
    /// The request line of every request served so far (<c>GET / HTTP/1.1</c>; through a proxy the
    /// absolute form <c>GET http://host:port/ HTTP/1.1</c>), in arrival order.
    /// </summary>
    public IReadOnlyList<string> RequestLines => [.. requestLines];

    public async ValueTask DisposeAsync()
    {
        await stopping.CancelAsync();
        try
        {
            await serving;
        }
        catch (OperationCanceledException) when (stopping.IsCancellationRequested)
        {
            // The accept loop ends by this cancellation; any other failure surfaces to the test.
        }
        finally
        {
            listener.Dispose();
            stopping.Dispose();
        }
    }

    private async Task ServeAsync(CancellationToken cancellationToken)
    {
        while (true)
        {
            using TcpClient client = await listener.AcceptTcpClientAsync(cancellationToken);
            await using NetworkStream stream = client.GetStream();
            await AnswerAsync(stream, cancellationToken);
        }
    }

    private async Task AnswerAsync(NetworkStream stream, CancellationToken cancellationToken)
    {
        byte[] head = await ReadHeadAsync(stream, cancellationToken);
        string[] lines = Encoding.ASCII.GetString(head).Split("\r\n");
        requestLines.Enqueue(lines[0]);

        int bodyLength = lines
            .Skip(1)
            .Where(line => line.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase))
            .Select(line => int.Parse(line["Content-Length:".Length..].Trim(), CultureInfo.InvariantCulture))
            .FirstOrDefault();
        await stream.ReadExactlyAsync(new byte[bodyLength], cancellationToken);

        await stream.WriteAsync(response, cancellationToken);
        await stream.FlushAsync(cancellationToken);
    }

    private static async Task<byte[]> ReadHeadAsync(NetworkStream stream, CancellationToken cancellationToken)
    {
        var head = new List<byte>();
        var one = new byte[1];
        while (head.Count < HeadEnd.Length || !head[^HeadEnd.Length..].SequenceEqual(HeadEnd))
        {
            if (head.Count >= MaxHeadBytes || await stream.ReadAsync(one, cancellationToken) == 0)
            {
                throw new InvalidOperationException("The connection ended before a complete request head.");
            }

            head.Add(one[0]);
        }

        return [.. head[..^HeadEnd.Length]];
    }
}
