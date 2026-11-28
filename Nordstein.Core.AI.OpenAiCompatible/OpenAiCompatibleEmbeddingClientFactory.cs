using Nordstein.Core.AI.Embeddings;

namespace Nordstein.Core.AI.OpenAiCompatible;

/// <summary>
/// Creates an <see cref="IEmbeddingClient"/> for one model behind an OpenAI-compatible endpoint.
/// Cheap: it validates the options and performs no I/O. The client is stateless and safe for
/// concurrent callers; a host typically creates one at startup and registers it as its
/// <see cref="IEmbeddingClient"/>.
/// </summary>
/// <param name="options">The model and its endpoint.</param>
/// <returns>The client.</returns>
/// <exception cref="ArgumentNullException"><paramref name="options"/> is null.</exception>
/// <exception cref="InvalidOperationException">An option is unusable.</exception>
public delegate IEmbeddingClient OpenAiCompatibleEmbeddingClientFactory(OpenAiCompatibleEmbeddingOptions options);
