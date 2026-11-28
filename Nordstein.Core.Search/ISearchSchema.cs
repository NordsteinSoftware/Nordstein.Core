namespace Nordstein.Core.Search;

/// <summary>
/// Creates, verifies and removes the storage of search indexes. A host calls
/// <see cref="EnsureAsync"/> once at startup, after its own schema is in place and before the first
/// <see cref="ISearchIndex"/> call. Implementations are safe for concurrent callers.
/// </summary>
public interface ISearchSchema
{
    /// <summary>
    /// Creates the storage of the index when it is missing, and verifies it otherwise. Idempotent:
    /// a second call with the same definition changes nothing.
    /// </summary>
    /// <param name="definition">The index to create or verify.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <exception cref="ArgumentNullException"><paramref name="definition"/> is null.</exception>
    /// <exception cref="SearchSchemaMismatchException">
    /// An index of that name exists with another model, dimension or text-search configuration.
    /// </exception>
    /// <exception cref="ArgumentException">
    /// The store does not know the definition's text-search configuration.
    /// </exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.</exception>
    Task EnsureAsync(SearchIndexDefinition definition, CancellationToken cancellationToken = default);

    /// <summary>
    /// The definition the index of that name is bound to, or null when no such index exists.
    /// </summary>
    /// <param name="name">The index name, in the form <see cref="SearchIndexDefinition"/> requires.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <exception cref="ArgumentNullException"><paramref name="name"/> is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="name"/> is not an index name.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.</exception>
    Task<SearchIndexDefinition?> FindAsync(string name, CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes the index of that name with all its entries and vectors. Removing an index that does
    /// not exist is a no-op. This is the first step of moving an index to another embedding model.
    /// </summary>
    /// <param name="name">The index name, in the form <see cref="SearchIndexDefinition"/> requires.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <exception cref="ArgumentNullException"><paramref name="name"/> is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="name"/> is not an index name.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.</exception>
    Task RemoveAsync(string name, CancellationToken cancellationToken = default);
}
