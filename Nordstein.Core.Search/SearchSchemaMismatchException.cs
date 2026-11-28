namespace Nordstein.Core.Search;

/// <summary>
/// An index exists under the requested name with another embedding model, dimension or text-search
/// configuration. Its stored vectors and keyword data cannot serve the requested definition; the
/// index must be removed and rebuilt, or the configuration corrected.
/// </summary>
public sealed class SearchSchemaMismatchException : InvalidOperationException
{
    /// <summary>
    /// Creates the exception.
    /// </summary>
    /// <param name="bound">The definition the index was created with.</param>
    /// <param name="requested">The definition that was asked for.</param>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    public SearchSchemaMismatchException(SearchIndexDefinition bound, SearchIndexDefinition requested)
        : base(Describe(bound, requested))
    {
        Bound = bound;
        Requested = requested;
    }

    /// <summary>
    /// The definition the index was created with.
    /// </summary>
    public SearchIndexDefinition Bound { get; }

    /// <summary>
    /// The definition that was asked for.
    /// </summary>
    public SearchIndexDefinition Requested { get; }

    private static string Describe(SearchIndexDefinition bound, SearchIndexDefinition requested)
    {
        ArgumentNullException.ThrowIfNull(bound);
        ArgumentNullException.ThrowIfNull(requested);
        return $"Search index '{bound.Name}' is bound to model '{bound.EmbeddingModelId}' with {bound.Dimension} dimensions "
            + $"and text-search configuration '{bound.TextSearchConfig}', but '{requested.EmbeddingModelId}' with "
            + $"{requested.Dimension} dimensions and '{requested.TextSearchConfig}' was requested. "
            + "Vectors of two models are not comparable: remove and rebuild the index, or correct the configuration.";
    }
}
