namespace Nordstein.Core.Search;

/// <summary>
/// The tuning of one search: how many candidates each leg contributes, the constant of the
/// reciprocal rank fusion, and how many hits come back. Immutable and validated on construction.
/// </summary>
public sealed record SearchParameters
{
    /// <summary>
    /// The largest <see cref="CandidatesPerLeg"/>.
    /// </summary>
    public const int MaxCandidatesPerLeg = 1000;

    /// <summary>
    /// The largest <see cref="RrfK"/>.
    /// </summary>
    public const int MaxRrfK = 1000;

    /// <summary>
    /// The largest <see cref="MaxResults"/>.
    /// </summary>
    public const int MaxMaxResults = 200;

    /// <summary>
    /// 100 candidates per leg, a fusion constant of 60, 10 results.
    /// </summary>
    public static SearchParameters Default { get; } = new(100, 60, 10);

    /// <summary>
    /// Creates parameters.
    /// </summary>
    /// <param name="candidatesPerLeg">
    /// How many best entries each leg ranks, 1 to <see cref="MaxCandidatesPerLeg"/>.
    /// </param>
    /// <param name="rrfK">
    /// The fusion constant <c>k</c> of <c>1 / (k + rank)</c>, 1 to <see cref="MaxRrfK"/>. A larger
    /// value flattens the advantage of a top rank.
    /// </param>
    /// <param name="maxResults">How many fused hits to return, 1 to <see cref="MaxMaxResults"/>.</param>
    /// <exception cref="ArgumentOutOfRangeException">An argument is out of its range.</exception>
    public SearchParameters(int candidatesPerLeg, int rrfK, int maxResults)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(candidatesPerLeg, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(candidatesPerLeg, MaxCandidatesPerLeg);
        ArgumentOutOfRangeException.ThrowIfLessThan(rrfK, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(rrfK, MaxRrfK);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxResults, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(maxResults, MaxMaxResults);
        CandidatesPerLeg = candidatesPerLeg;
        RrfK = rrfK;
        MaxResults = maxResults;
    }

    /// <summary>
    /// How many best entries each leg ranks.
    /// </summary>
    public int CandidatesPerLeg { get; }

    /// <summary>
    /// The fusion constant <c>k</c> of <c>1 / (k + rank)</c>.
    /// </summary>
    public int RrfK { get; }

    /// <summary>
    /// How many fused hits a search returns at most.
    /// </summary>
    public int MaxResults { get; }
}
