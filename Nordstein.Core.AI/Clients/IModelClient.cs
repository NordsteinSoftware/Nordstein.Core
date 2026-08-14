using Nordstein.Core.AI.Completions;
using Nordstein.Core.AI.Messages;

namespace Nordstein.Core.AI.Clients;

/// <summary>
/// A client for one model binding. Implementations own a disposable provider transport, so
/// callers MUST dispose the client (a <c>using</c>) once done. How a client is obtained —
/// factories, per-agent binding, call recording — is the consuming product's concern.
/// </summary>
public interface IModelClient : IDisposable
{
    /// <summary>Completes the conversation and returns the assistant's response with usage and latency.</summary>
    Task<ICompletion> CompleteAsync(
        Conversation conversation,
        ModelOptions? options = null,
        IReadOnlyDictionary<string, string>? promptVariables = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Completes the conversation and parses the response into <typeparamref name="TOutput"/>
    /// via the configured output format.
    /// </summary>
    Task<TypedCompletion<TOutput>> CompleteAsync<TOutput>(
        Conversation conversation,
        ModelOptions? options = null,
        IReadOnlyDictionary<string, string>? promptVariables = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Reconstructs the exact request <see cref="CompleteAsync(Conversation, ModelOptions?, IReadOnlyDictionary{string, string}?, CancellationToken)"/>
    /// would send — model, messages (system prompt merged in), and tools — without contacting
    /// the provider.
    /// </summary>
    ModelRequestPreview BuildRequestPreview(
        Conversation conversation,
        ModelOptions? options = null,
        IReadOnlyDictionary<string, string>? promptVariables = null);

    /// <summary>
    /// Streams a single completion turn. The caller supplies the system message explicitly so
    /// a stored system prompt can be overridden.
    /// </summary>
    IAsyncEnumerable<ModelStreamUpdate> StreamAsync(
        SystemMessage systemMessage,
        Conversation conversation,
        ModelOptions? options = null,
        CancellationToken cancellationToken = default);
}
