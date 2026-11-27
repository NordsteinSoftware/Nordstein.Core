using System.Net;
using System.Net.Http.Headers;
using System.Text;
using Nordstein.Core.Testing.Http.Internal;

namespace Nordstein.Core.Testing.Http;

/// <summary>
/// A scripted stand-in for the network under an <see cref="HttpClient"/>: each send takes the next
/// step of the script (a response, an exception, a timeout, a hang) and is recorded in
/// <see cref="Requests"/> first. Nothing ever leaves the process.
/// </summary>
/// <remarks>
/// <para>
/// Build the script with the fluent methods (<c>new ScriptedHttpMessageHandler().Respond(…).Throw(…)</c>);
/// steps are taken in the order they were added, one per send. A send that finds the script empty
/// throws <see cref="InvalidOperationException"/>, so an unexpected call fails the test instead of
/// passing on a default answer. <see cref="RepeatLast"/> makes the last step answer every later
/// send instead.
/// </para>
/// <para>
/// A send whose token is already cancelled throws <see cref="TaskCanceledException"/> before it
/// is recorded or takes a step, like a real handler; so does a send after disposal, with
/// <see cref="ObjectDisposedException"/>. Otherwise the body is read (with the send's
/// token) and the request recorded before the step runs, so <see cref="Requests"/> holds even the
/// sends that a step then fails. Every response gets <see cref="HttpResponseMessage.RequestMessage"/>
/// set to its request unless the step set one.
/// </para>
/// <para>
/// <b>Thread-safety.</b> Adding steps and sending are safe from any thread, concurrently: each send
/// takes exactly one step, and the n-th recorded request is the one that took the n-th step. A step
/// built with <see cref="Respond(HttpStatusCode)"/> or <see cref="Respond(HttpStatusCode, string, string)"/>
/// creates a fresh response per send; a factory step runs once per send, concurrently if the sends
/// are concurrent.
/// </para>
/// </remarks>
public sealed class ScriptedHttpMessageHandler : HttpMessageHandler
{
    private readonly Lock gate = new();
    private readonly Queue<Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>>> steps = new();
    private readonly List<RecordedHttpRequest> requests = [];
    private Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>>? lastAdded;
    private Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>>? repeating;
    private volatile bool disposed;

    /// <summary>
    /// A snapshot of every request received so far, in the order the sends took their steps.
    /// </summary>
    public IReadOnlyList<RecordedHttpRequest> Requests
    {
        get
        {
            lock (gate)
            {
                return [.. requests];
            }
        }
    }

    /// <summary>
    /// How many added steps no send has taken yet; a repeating step is not counted. Zero at the end
    /// of a test proves every scripted call happened.
    /// </summary>
    public int RemainingSteps
    {
        get
        {
            lock (gate)
            {
                return steps.Count;
            }
        }
    }

    /// <summary>
    /// Adds a step answering <paramref name="status"/> with an empty body.
    /// </summary>
    /// <param name="status">The response's status code.</param>
    /// <returns>This handler, to add the next step.</returns>
    /// <exception cref="InvalidOperationException">The script already ends with <see cref="RepeatLast"/>.</exception>
    public ScriptedHttpMessageHandler Respond(HttpStatusCode status)
        => Add((_, _) => Task.FromResult(new HttpResponseMessage(status)));

    /// <summary>
    /// Adds a step answering <paramref name="status"/> with <paramref name="content"/> as a UTF-8
    /// body of <paramref name="mediaType"/>.
    /// </summary>
    /// <param name="status">The response's status code.</param>
    /// <param name="content">The body; may be empty.</param>
    /// <param name="mediaType">The body's media type; <c>application/json</c> unless given.</param>
    /// <returns>This handler, to add the next step.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="content"/> or <paramref name="mediaType"/> is null.</exception>
    /// <exception cref="FormatException"><paramref name="mediaType"/> is not a media type.</exception>
    /// <exception cref="InvalidOperationException">The script already ends with <see cref="RepeatLast"/>.</exception>
    public ScriptedHttpMessageHandler Respond(HttpStatusCode status, string content, string mediaType = "application/json")
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(mediaType);

        // Refused here, while the script is written, rather than on the send that would use it.
        _ = MediaTypeHeaderValue.Parse(mediaType);
        return Add((_, _) => Task.FromResult(new HttpResponseMessage(status)
        {
            Content = new StringContent(content, Encoding.UTF8, mediaType),
        }));
    }

    /// <summary>
    /// Adds a step whose response <paramref name="respond"/> builds from the request; use it for
    /// what the status-and-body overloads cannot express (headers, a redirect, a custom
    /// <see cref="HttpContent"/>).
    /// </summary>
    /// <param name="respond">Builds the response; must not return null.</param>
    /// <returns>This handler, to add the next step.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="respond"/> is null.</exception>
    /// <exception cref="InvalidOperationException">The script already ends with <see cref="RepeatLast"/>.</exception>
    public ScriptedHttpMessageHandler Respond(Func<HttpRequestMessage, HttpResponseMessage> respond)
    {
        ArgumentNullException.ThrowIfNull(respond);
        return Add((request, _) => Task.FromResult(respond(request)));
    }

    /// <summary>
    /// Adds an asynchronous step; it receives the send's token, which <see cref="HttpClient"/>
    /// cancels on its <see cref="HttpClient.Timeout"/>, so a step that waits must honour it.
    /// </summary>
    /// <param name="respond">Builds the response; must not return null or a null task.</param>
    /// <returns>This handler, to add the next step.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="respond"/> is null.</exception>
    /// <exception cref="InvalidOperationException">The script already ends with <see cref="RepeatLast"/>.</exception>
    public ScriptedHttpMessageHandler Respond(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond)
    {
        ArgumentNullException.ThrowIfNull(respond);
        return Add(respond);
    }

    /// <summary>
    /// Adds a step that throws <paramref name="exception"/>, such as an
    /// <see cref="HttpRequestException"/> for a refused connection.
    /// </summary>
    /// <param name="exception">The exception to throw; the same instance on every send it answers.</param>
    /// <returns>This handler, to add the next step.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="exception"/> is null.</exception>
    /// <exception cref="InvalidOperationException">The script already ends with <see cref="RepeatLast"/>.</exception>
    public ScriptedHttpMessageHandler Throw(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        return Add((_, _) => Task.FromException<HttpResponseMessage>(exception));
    }

    /// <summary>
    /// Adds a step that fails the way an elapsed <see cref="HttpClient.Timeout"/> does: a
    /// <see cref="TaskCanceledException"/> whose inner exception is a <see cref="TimeoutException"/>,
    /// while the caller's token stays uncancelled.
    /// </summary>
    /// <returns>This handler, to add the next step.</returns>
    /// <exception cref="InvalidOperationException">The script already ends with <see cref="RepeatLast"/>.</exception>
    public ScriptedHttpMessageHandler TimeOut()
        => Add((_, _) => Task.FromException<HttpResponseMessage>(new TaskCanceledException(
            "The scripted request timed out, as an elapsed HttpClient.Timeout reports it.",
            new TimeoutException("The scripted request timed out."))));

    /// <summary>
    /// Adds a step that never answers: it waits until the send's token is cancelled (by the
    /// caller, or by <see cref="HttpClient"/> when its <see cref="HttpClient.Timeout"/> elapses)
    /// and then throws <see cref="TaskCanceledException"/>. Without either, the send never ends.
    /// </summary>
    /// <returns>This handler, to add the next step.</returns>
    /// <exception cref="InvalidOperationException">The script already ends with <see cref="RepeatLast"/>.</exception>
    public ScriptedHttpMessageHandler Hang()
        => Add((_, cancellationToken) => new TaskCompletionSource<HttpResponseMessage>().Task.WaitAsync(cancellationToken));

    /// <summary>
    /// Makes the last added step answer every later send instead of being taken once; it ends the
    /// script. A response step still builds a fresh response per send.
    /// </summary>
    /// <returns>This handler.</returns>
    /// <exception cref="InvalidOperationException">No step was added yet, or the script already ends with a repeating step.</exception>
    public ScriptedHttpMessageHandler RepeatLast()
    {
        lock (gate)
        {
            ThrowIfRepeatingLocked();
            if (lastAdded is not { } last)
            {
                throw new InvalidOperationException("The script has no step to repeat; add one first.");
            }

            // The last added step answers from where it stands in the queue onwards: once every
            // earlier step was taken, it is taken by every later send.
            repeating = last;
            if (steps.Count > 0)
            {
                Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>>[] queued = [.. steps];
                steps.Clear();
                foreach (Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> step in queued[..^1])
                {
                    steps.Enqueue(step);
                }
            }

            return this;
        }
    }

    /// <inheritdoc />
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ObjectDisposedException.ThrowIf(disposed, this);
        if (cancellationToken.IsCancellationRequested)
        {
            throw new TaskCanceledException("The request was cancelled before it was sent.", null, cancellationToken);
        }

        RecordedHttpRequest recorded = await request.RecordAsync(cancellationToken);
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>>? step;
        int number;
        lock (gate)
        {
            requests.Add(recorded);
            number = requests.Count;
            step = steps.Count > 0 ? steps.Dequeue() : repeating;
        }

        if (step is null)
        {
            throw new InvalidOperationException(
                $"The script of this {nameof(ScriptedHttpMessageHandler)} is exhausted: no scripted step is left for request {number}: {recorded}. "
                + "Add a step for every call the code under test is meant to make.");
        }

        Task<HttpResponseMessage>? pending = step(request, cancellationToken);
        HttpResponseMessage? response = pending is null ? null : await pending;
        if (response is null)
        {
            throw new InvalidOperationException($"The scripted step for request {number} ({recorded}) returned no response.");
        }

        response.RequestMessage ??= request;
        return response;
    }

    /// <inheritdoc />
    protected override HttpResponseMessage Send(HttpRequestMessage request, CancellationToken cancellationToken)
        => SendAsync(request, cancellationToken).GetAwaiter().GetResult();

    /// <summary>
    /// Marks the handler disposed: a later send throws <see cref="ObjectDisposedException"/>, as a
    /// real handler's does. <see cref="Requests"/> stays readable.
    /// </summary>
    /// <param name="disposing">Whether the call comes from <see cref="HttpMessageHandler.Dispose()"/>.</param>
    protected override void Dispose(bool disposing)
    {
        disposed = true;
        base.Dispose(disposing);
    }

    private ScriptedHttpMessageHandler Add(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> step)
    {
        lock (gate)
        {
            ThrowIfRepeatingLocked();
            steps.Enqueue(step);
            lastAdded = step;
            return this;
        }
    }

    private void ThrowIfRepeatingLocked()
    {
        if (repeating is not null)
        {
            throw new InvalidOperationException(
                "The script ends with a repeating step (RepeatLast); no step can follow it.");
        }
    }
}
