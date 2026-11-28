using Nordstein.Core.Common.Text;

namespace Nordstein.Core.Search;

/// <summary>
/// One search: the text for the keyword leg, optionally its embedding for the vector leg, a facet
/// filter and the tuning. Immutable and validated on construction.
/// </summary>
/// <remarks>
/// Without an embedding the keyword leg alone answers, which is how a consumer keeps searching
/// while its embedding endpoint is down. With an embedding and a blank text the vector leg alone
/// answers.
/// </remarks>
public sealed record SearchQuery
{
    /// <summary>
    /// The longest <see cref="Text"/> a query accepts, in UTF-16 code units.
    /// </summary>
    public const int MaxTextLength = 4000;

    /// <summary>
    /// Creates a query.
    /// </summary>
    /// <param name="text">
    /// The words to look for, at most <see cref="MaxTextLength"/> code units, without U+0000 and
    /// without a lone surrogate. Blank only together with an <paramref name="embedding"/>.
    /// </param>
    /// <param name="embedding">
    /// The embedding of <paramref name="text"/> from the index's model, or null to search by keyword
    /// only. Its length and values are checked by the index.
    /// </param>
    /// <param name="filter">The facet filter; <see cref="SearchFilter.None"/> for none.</param>
    /// <param name="parameters">The tuning; <see cref="SearchParameters.Default"/> for the defaults.</param>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="text"/>, <paramref name="filter"/> or <paramref name="parameters"/> is null.
    /// </exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="text"/> is too long, holds U+0000 or a lone surrogate, or it is blank and
    /// there is no embedding.
    /// </exception>
    public SearchQuery(string text, ReadOnlyMemory<float>? embedding, SearchFilter filter, SearchParameters parameters)
    {
        text.RequireStorable(MaxTextLength);
        ArgumentNullException.ThrowIfNull(filter);
        ArgumentNullException.ThrowIfNull(parameters);

        if (embedding is null && string.IsNullOrWhiteSpace(text))
        {
            throw new ArgumentException("A query needs a text, an embedding or both.", nameof(text));
        }

        Text = text;
        Embedding = embedding;
        Filter = filter;
        Parameters = parameters;
    }

    /// <summary>
    /// The words the keyword leg looks for.
    /// </summary>
    public string Text { get; }

    /// <summary>
    /// The embedding the vector leg ranks by, or null when only the keyword leg runs.
    /// </summary>
    public ReadOnlyMemory<float>? Embedding { get; }

    /// <summary>
    /// The facet filter both legs apply.
    /// </summary>
    public SearchFilter Filter { get; }

    /// <summary>
    /// The tuning of this search.
    /// </summary>
    public SearchParameters Parameters { get; }
}
