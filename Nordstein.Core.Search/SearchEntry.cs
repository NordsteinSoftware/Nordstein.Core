using Nordstein.Core.Common.Text;

namespace Nordstein.Core.Search;

/// <summary>
/// One searchable unit of text in an index. Several entries may belong to one owner: the object of
/// the consumer that the text was cut from. Immutable and validated on construction.
/// </summary>
/// <remarks>
/// Facets are opaque strings the index filters on (<see cref="SearchFilter"/>), compared ordinally.
/// A consumer encodes its own dimensions into them, for example <c>kind:note</c> or
/// <c>state:open</c>; the index knows none of them. Equality compares <see cref="Facets"/> by
/// reference, as for any record that holds a collection.
/// </remarks>
public sealed record SearchEntry
{
    /// <summary>
    /// The longest <see cref="Text"/> an entry accepts, in UTF-16 code units.
    /// </summary>
    public const int MaxTextLength = 32_768;

    /// <summary>
    /// The largest number of <see cref="Facets"/> an entry accepts.
    /// </summary>
    public const int MaxFacets = 64;

    /// <summary>
    /// The longest facet an entry or a filter accepts, in UTF-16 code units.
    /// </summary>
    public const int MaxFacetLength = 128;

    /// <summary>
    /// Creates an entry.
    /// </summary>
    /// <param name="id">The entry's identity; never <see cref="Guid.Empty"/>.</param>
    /// <param name="ownerId">The identity of the owning object; never <see cref="Guid.Empty"/>.</param>
    /// <param name="text">
    /// The text to search: not blank, at most <see cref="MaxTextLength"/> code units, without
    /// U+0000 and without a lone surrogate.
    /// </param>
    /// <param name="facets">
    /// At most <see cref="MaxFacets"/> facets, each 1 to <see cref="MaxFacetLength"/> code units
    /// without control characters or lone surrogates. Copied; duplicates collapse.
    /// </param>
    /// <exception cref="ArgumentNullException"><paramref name="text"/> or <paramref name="facets"/> is null.</exception>
    /// <exception cref="ArgumentException">An argument breaks a rule above.</exception>
    public SearchEntry(Guid id, Guid ownerId, string text, IEnumerable<string> facets)
    {
        text.RequireStorable(MaxTextLength);
        if (id == Guid.Empty)
        {
            throw new ArgumentException("An entry id is never the empty GUID.", nameof(id));
        }

        if (ownerId == Guid.Empty)
        {
            throw new ArgumentException("An owner id is never the empty GUID.", nameof(ownerId));
        }

        if (string.IsNullOrWhiteSpace(text))
        {
            throw new ArgumentException("An entry text is never blank.", nameof(text));
        }

        Id = id;
        OwnerId = ownerId;
        Text = text;
        Facets = Internal.Facets.Copy(facets, MaxFacets, nameof(facets));
    }

    /// <summary>
    /// The entry's identity.
    /// </summary>
    public Guid Id { get; }

    /// <summary>
    /// The identity of the object the text was cut from.
    /// </summary>
    public Guid OwnerId { get; }

    /// <summary>
    /// The text both search legs work on.
    /// </summary>
    public string Text { get; }

    /// <summary>
    /// The entry's facets, compared ordinally.
    /// </summary>
    public IReadOnlySet<string> Facets { get; }
}
