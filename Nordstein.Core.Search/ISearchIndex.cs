namespace Nordstein.Core.Search;

/// <summary>
/// One search index: entries of text with facets, their vectors, and the hybrid search over both.
/// Obtained through <see cref="Open"/> for a definition that <see cref="ISearchSchema.EnsureAsync"/>
/// has created; calls on an index that was never ensured fail with the store's own exception.
/// </summary>
/// <remarks>
/// <para>
/// Writing and embedding are two steps on purpose. <see cref="UpsertAsync"/> makes a text findable
/// by keyword at once; a background job later reads <see cref="ListWithoutEmbeddingAsync"/>, calls
/// the embedding endpoint and stores the result with <see cref="SetEmbeddingsAsync"/>. An endpoint
/// outage delays the vector leg and never fails a write.
/// </para>
/// <para>
/// Every call takes part in the caller's ambient transaction when one is active, so a consumer can
/// write its own rows and the entries atomically. Implementations hold no state between calls.
/// </para>
/// </remarks>
public interface ISearchIndex
{
    /// <summary>
    /// Opens the index of a definition. Cheap: it performs no I/O.
    /// </summary>
    /// <param name="definition">The definition the index was ensured with.</param>
    /// <returns>The index.</returns>
    public delegate ISearchIndex Open(SearchIndexDefinition definition);

    /// <summary>
    /// The definition this index was opened with.
    /// </summary>
    SearchIndexDefinition Definition { get; }

    /// <summary>
    /// Adds entries, or replaces the ones whose id already exists. A replaced entry keeps its vector
    /// only when its text is unchanged; otherwise it is listed by
    /// <see cref="ListWithoutEmbeddingAsync"/> again.
    /// </summary>
    /// <param name="entries">The entries; ids must be distinct. Empty is a no-op.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <exception cref="ArgumentNullException"><paramref name="entries"/> or one of them is null.</exception>
    /// <exception cref="ArgumentException">Two entries share an id.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.</exception>
    Task UpsertAsync(IReadOnlyCollection<SearchEntry> entries, CancellationToken cancellationToken = default);

    /// <summary>
    /// Entries that have no vector yet, in ascending order of their id, so a job can embed them in
    /// batches.
    /// </summary>
    /// <remarks>
    /// An entry the embedding endpoint keeps refusing stays without a vector. A job passes the id of
    /// the last entry it has seen as <paramref name="afterId"/> to move past it within one pass,
    /// instead of reading the same refused entries again.
    /// </remarks>
    /// <param name="limit">The largest number of entries to return, 1 to 1000.</param>
    /// <param name="afterId">Only entries whose id sorts after this one; null starts at the first.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="limit"/> is out of range.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.</exception>
    Task<IReadOnlyList<SearchEntry>> ListWithoutEmbeddingAsync(int limit, Guid? afterId = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// How many entries have no vector yet. A health signal: a number that keeps growing means the
    /// embedding job is not keeping up or the endpoint is down.
    /// </summary>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.</exception>
    Task<long> CountWithoutEmbeddingAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Stores vectors. A vector is stored only while its entry exists and still holds the text the
    /// vector was computed from; any other is skipped without an error.
    /// </summary>
    /// <param name="embeddings">The vectors; entry ids must be distinct. Empty is a no-op.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>How many vectors were stored.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="embeddings"/> or one of them is null.</exception>
    /// <exception cref="ArgumentException">
    /// Two embeddings share an entry id, or a vector has another length than the index's dimension
    /// or a component that is not finite.
    /// </exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.</exception>
    Task<int> SetEmbeddingsAsync(IReadOnlyCollection<SearchEmbedding> embeddings, CancellationToken cancellationToken = default);

    /// <summary>
    /// Replaces the facets of every entry of one owner, for a change that does not touch the text
    /// (a state, a label). Vectors are kept.
    /// </summary>
    /// <param name="ownerId">The owner whose entries change.</param>
    /// <param name="facets">The new facets, under the rules of <see cref="SearchEntry"/>.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>How many entries changed.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="facets"/> is null.</exception>
    /// <exception cref="ArgumentException">A facet breaks a rule of <see cref="SearchEntry"/>.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.</exception>
    Task<int> ReplaceFacetsAsync(Guid ownerId, IReadOnlyCollection<string> facets, CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes every entry of the given owners. Unknown owners are ignored.
    /// </summary>
    /// <param name="ownerIds">The owners. Empty is a no-op.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>How many entries were removed.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="ownerIds"/> is null.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.</exception>
    Task<int> RemoveOwnersAsync(IReadOnlyCollection<Guid> ownerIds, CancellationToken cancellationToken = default);

    /// <summary>
    /// Searches the index. Each leg ranks its best candidates among the entries that pass the
    /// filter: the vector leg by cosine distance to the query's embedding, the keyword leg by how
    /// well the text matches. The legs are fused by reciprocal rank; ties break on the entry id.
    /// </summary>
    /// <param name="query">The query.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>The hits, best first, at most <see cref="SearchParameters.MaxResults"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="query"/> is null.</exception>
    /// <exception cref="ArgumentException">
    /// The query's embedding has another length than the index's dimension or a component that is
    /// not finite.
    /// </exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.</exception>
    Task<IReadOnlyList<SearchHit>> SearchAsync(SearchQuery query, CancellationToken cancellationToken = default);
}
