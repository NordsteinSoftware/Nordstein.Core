using Nordstein.Core.Search.Internal;

namespace Nordstein.Core.Search;

/// <summary>
/// The identity of one search index: its name, the embedding model whose vectors it holds, their
/// dimension, and the language configuration of its keyword leg. Immutable and validated on
/// construction; two definitions are equal when all four values are.
/// </summary>
/// <remarks>
/// Vectors of two models, or of two dimensions, are not comparable. <see cref="ISearchSchema"/>
/// therefore binds a definition when it first creates the index and refuses a different one later.
/// </remarks>
public sealed record SearchIndexDefinition
{
    /// <summary>
    /// The largest <see cref="Dimension"/> an index accepts.
    /// </summary>
    public const int MaxDimension = 4000;

    /// <summary>
    /// The longest <see cref="EmbeddingModelId"/> an index accepts.
    /// </summary>
    public const int MaxEmbeddingModelIdLength = 128;

    /// <summary>
    /// Creates a definition.
    /// </summary>
    /// <param name="name">
    /// The index name: a lower-case letter, then up to 39 lower-case letters, digits or underscores.
    /// A provider derives the names of its storage objects from it.
    /// </param>
    /// <param name="embeddingModelId">
    /// The identity of the embedding model, 1 to <see cref="MaxEmbeddingModelIdLength"/> characters,
    /// neither starting nor ending with white space.
    /// </param>
    /// <param name="dimension">The number of floats in every vector, 1 to <see cref="MaxDimension"/>.</param>
    /// <param name="textSearchConfig">
    /// The language configuration of the keyword leg, as the provider names it (for example
    /// <c>english</c>): a lower-case letter or underscore, then up to 62 lower-case letters, digits
    /// or underscores.
    /// </param>
    /// <exception cref="ArgumentNullException">A string argument is null.</exception>
    /// <exception cref="ArgumentException">A string argument does not have the form above.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="dimension"/> is out of range.</exception>
    public SearchIndexDefinition(string name, string embeddingModelId, int dimension, string textSearchConfig)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(embeddingModelId);
        ArgumentNullException.ThrowIfNull(textSearchConfig);
        if (!SearchNames.IsIndexName(name))
        {
            throw new ArgumentException(
                $"'{name}' is not an index name: a lower-case letter, then up to 39 lower-case letters, digits or underscores.",
                nameof(name));
        }

        if (embeddingModelId.Length is 0 or > MaxEmbeddingModelIdLength
            || char.IsWhiteSpace(embeddingModelId[0])
            || char.IsWhiteSpace(embeddingModelId[^1]))
        {
            throw new ArgumentException(
                $"An embedding model id has 1 to {MaxEmbeddingModelIdLength} characters and no white space at either end.",
                nameof(embeddingModelId));
        }

        ArgumentOutOfRangeException.ThrowIfLessThan(dimension, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(dimension, MaxDimension);
        if (!SearchNames.IsTextSearchConfig(textSearchConfig))
        {
            throw new ArgumentException(
                $"'{textSearchConfig}' is not a text-search configuration name: a lower-case letter or underscore, then up to 62 lower-case letters, digits or underscores.",
                nameof(textSearchConfig));
        }

        Name = name;
        EmbeddingModelId = embeddingModelId;
        Dimension = dimension;
        TextSearchConfig = textSearchConfig;
    }

    /// <summary>
    /// Whether <paramref name="name"/> has the form of an index name: a lower-case letter, then up
    /// to 39 lower-case letters, digits or underscores. Null is not a name.
    /// </summary>
    /// <param name="name">The candidate name.</param>
    public static bool IsValidName(string? name) => name is not null && SearchNames.IsIndexName(name);

    /// <summary>
    /// The index name.
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// The identity of the embedding model whose vectors the index holds.
    /// </summary>
    public string EmbeddingModelId { get; }

    /// <summary>
    /// The number of floats in every vector of the index.
    /// </summary>
    public int Dimension { get; }

    /// <summary>
    /// The language configuration of the keyword leg, as the provider names it.
    /// </summary>
    public string TextSearchConfig { get; }
}
