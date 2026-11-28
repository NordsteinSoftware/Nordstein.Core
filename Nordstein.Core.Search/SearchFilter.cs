namespace Nordstein.Core.Search;

/// <summary>
/// A facet filter in conjunctive form. An entry passes when it carries at least one facet of every
/// group in <see cref="Require"/> and no facet of <see cref="Exclude"/>. Immutable and validated on
/// construction.
/// </summary>
/// <remarks>
/// One group per dimension expresses "any of these values"; a group of one facet expresses "must
/// carry it". For example <c>[{kind:note, kind:memo}, {state:open}]</c> passes open notes and open
/// memos. Equality compares the collections by reference, as for any record that holds one.
/// </remarks>
public sealed record SearchFilter
{
    /// <summary>
    /// The largest number of groups in <see cref="Require"/>.
    /// </summary>
    public const int MaxGroups = 16;

    /// <summary>
    /// The largest number of facets in one group and in <see cref="Exclude"/>.
    /// </summary>
    public const int MaxFacetsPerGroup = 64;

    /// <summary>
    /// The filter every entry passes.
    /// </summary>
    public static SearchFilter None { get; } = new([], []);

    /// <summary>
    /// Creates a filter.
    /// </summary>
    /// <param name="require">
    /// At most <see cref="MaxGroups"/> groups, each of 1 to <see cref="MaxFacetsPerGroup"/> facets.
    /// Copied.
    /// </param>
    /// <param name="exclude">At most <see cref="MaxFacetsPerGroup"/> facets. Copied.</param>
    /// <exception cref="ArgumentNullException">An argument or a group is null.</exception>
    /// <exception cref="ArgumentException">
    /// There are too many groups, a group is empty or too large, or a facet is not a valid facet
    /// (<see cref="SearchEntry.MaxFacetLength"/>).
    /// </exception>
    public SearchFilter(IEnumerable<IEnumerable<string>> require, IEnumerable<string> exclude)
    {
        ArgumentNullException.ThrowIfNull(require);
        ArgumentNullException.ThrowIfNull(exclude);
        var groups = new List<IReadOnlySet<string>>();
        foreach (IEnumerable<string> group in require)
        {
            IReadOnlySet<string> copy = Internal.Facets.Copy(group, MaxFacetsPerGroup, nameof(require));
            if (copy.Count == 0)
            {
                throw new ArgumentException("A required group holds at least one facet.", nameof(require));
            }

            groups.Add(copy);
        }

        if (groups.Count > MaxGroups)
        {
            throw new ArgumentException($"At most {MaxGroups} required groups are allowed; got {groups.Count}.", nameof(require));
        }

        Require = groups.AsReadOnly();
        Exclude = Internal.Facets.Copy(exclude, MaxFacetsPerGroup, nameof(exclude));
    }

    /// <summary>
    /// The groups an entry must match: at least one facet of each.
    /// </summary>
    public IReadOnlyList<IReadOnlySet<string>> Require { get; }

    /// <summary>
    /// The facets an entry must not carry.
    /// </summary>
    public IReadOnlySet<string> Exclude { get; }
}
