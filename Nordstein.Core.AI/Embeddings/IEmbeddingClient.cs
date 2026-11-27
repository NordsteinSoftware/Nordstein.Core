using Nordstein.Core.AI.Clients;
using Nordstein.Core.AI.Embeddings;

namespace Nordstein.Core.AI.Embeddings;

/// <summary>
/// A client for one embedding model: turns texts into vectors of one output space. How a client is
/// obtained and which endpoint it calls are the consuming product's concern. Not disposable: an
/// implementation owns no per-call resource the caller must release.
/// </summary>
public interface IEmbeddingClient
{
    /// <summary>
    /// The model's identity. It keys the output space: vectors of two different identities are not
    /// comparable, so a caller that stores vectors records it and re-embeds when it changes.
    /// </summary>
    string ModelId { get; }

    /// <summary>The number of floats in every vector this client returns.</summary>
    int Dimension { get; }

    /// <summary>
    /// Embeds <paramref name="inputs"/>: one vector of <see cref="Dimension"/> floats per input, in
    /// input order. Empty inputs give an empty list without a call. An implementation may split the
    /// inputs into batches; the result is the same as one call.
    /// </summary>
    /// <param name="inputs">The texts to embed.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>One vector per input, in input order.</returns>
    /// <exception cref="InferenceUnavailableException">
    /// The endpoint could not be reached, timed out or answered with an error status: transient.
    /// </exception>
    /// <exception cref="InferenceContractException">
    /// The endpoint answered with the wrong shape (vector count or dimension): permanent until the
    /// configuration changes.
    /// </exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.</exception>
    Task<IReadOnlyList<ReadOnlyMemory<float>>> EmbedAsync(IReadOnlyList<string> inputs, CancellationToken cancellationToken = default);
}
