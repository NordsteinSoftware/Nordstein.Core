using System.Globalization;

namespace Nordstein.Core.Search.Postgres.Internal;

/// <summary>
/// The names and column type of one index's storage objects, derived from its definition. Every
/// value is safe to interpolate into a statement: <see cref="SearchIndexDefinition"/> admits only
/// plain identifiers and an integer.
/// </summary>
/// <remarks>
/// pgvector's HNSW index takes a <c>vector</c> of at most 2,000 dimensions and a <c>halfvec</c>
/// (half precision) of at most 4,000. An index therefore stores full precision whenever HNSW can
/// index it, and half precision above that.
/// </remarks>
internal sealed class IndexObjects
{
    public const string IdentityTable = "search_identity";
    private const int MaxFullPrecisionDimension = 2000;

    public IndexObjects(SearchIndexDefinition definition)
        : this(definition.Name, definition.Dimension, definition.TextSearchConfig)
    {
    }

    private IndexObjects(string name, int dimension, string textSearchConfig)
    {
        Table = $"search_{name}_entries";
        PrimaryKey = $"pk_search_{name}_entries";
        HnswIndex = $"ix_search_{name}_hnsw";
        TextIndex = $"ix_search_{name}_tsv";
        FacetIndex = $"ix_search_{name}_facets";
        OwnerIndex = $"ix_search_{name}_owner";
        PendingIndex = $"ix_search_{name}_pending";
        bool full = dimension <= MaxFullPrecisionDimension;
        VectorType = full ? "vector" : "halfvec";
        VectorOperatorClass = full ? "vector_cosine_ops" : "halfvec_cosine_ops";
        Dimension = dimension.ToString(CultureInfo.InvariantCulture);
        TextSearchConfig = textSearchConfig;
    }

    public string Table { get; }

    public string PrimaryKey { get; }

    public string HnswIndex { get; }

    public string TextIndex { get; }

    public string FacetIndex { get; }

    public string OwnerIndex { get; }

    public string PendingIndex { get; }

    public string VectorType { get; }

    public string VectorOperatorClass { get; }

    public string Dimension { get; }

    public string TextSearchConfig { get; }

    public IReadOnlyList<string> Indexes => [HnswIndex, TextIndex, FacetIndex, OwnerIndex, PendingIndex];

    /// <summary>
    /// The table name of the index called <paramref name="name"/>, for a caller that has no
    /// definition (removal).
    /// </summary>
    public static string TableOf(string name) => $"search_{name}_entries";
}
