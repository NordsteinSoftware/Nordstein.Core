using Nordstein.Core.Testing.Http.Internal;

namespace Nordstein.Core.Testing.Http;

/// <summary>
/// A handler that refuses every send: it records the attempt in <see cref="Attempts"/> and throws
/// <see cref="InvalidOperationException"/>. Register it where the code under test would reach the
/// network to prove that no call leaves the process; a test that means to answer a call scripts it
/// with <see cref="ScriptedHttpMessageHandler"/> instead.
/// </summary>
/// <remarks>
/// <para>
/// The exception is deliberately not an <see cref="HttpRequestException"/> or an
/// <see cref="OperationCanceledException"/>: code that treats transport failures or timeouts as
/// transient does not swallow it, so a forgotten substitution fails the test. Code that swallows
/// every exception still leaves the attempt in <see cref="Attempts"/>, so assert
/// <c>Attempts.Should().BeEmpty()</c> where it matters. A send with an already cancelled token is
/// an attempt too.
/// </para>
/// <para>
/// The body is read in full (without the send's token) before the exception is thrown, so an
/// attempt shows what would have left. Safe for concurrent sends from any thread; one instance can
/// guard a whole test container.
/// </para>
/// </remarks>
public sealed class NoNetworkHttpMessageHandler : HttpMessageHandler
{
    private readonly Lock gate = new();
    private readonly List<RecordedHttpRequest> attempts = [];

    /// <summary>
    /// A snapshot of every send attempted so far, in order.
    /// </summary>
    public IReadOnlyList<RecordedHttpRequest> Attempts
    {
        get
        {
            lock (gate)
            {
                return [.. attempts];
            }
        }
    }

    /// <inheritdoc />
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        RecordedHttpRequest attempt = await request.RecordAsync(CancellationToken.None);
        lock (gate)
        {
            attempts.Add(attempt);
        }

        throw new InvalidOperationException(
            $"The code under test tried to reach the network: {attempt}. No test may send a request through a "
            + $"{nameof(NoNetworkHttpMessageHandler)}; script the call with {nameof(ScriptedHttpMessageHandler)} if it is meant to happen.");
    }

    /// <inheritdoc />
    protected override HttpResponseMessage Send(HttpRequestMessage request, CancellationToken cancellationToken)
        => SendAsync(request, cancellationToken).GetAwaiter().GetResult();
}
