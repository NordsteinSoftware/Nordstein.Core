namespace Nordstein.Core.Common.Text;

/// <summary>
/// The outcome of <see cref="QuoteMatchExtensions.FindExactlyOnce"/>: how often the quote occurs and,
/// when exactly once, the original range it covers.
/// </summary>
/// <remarks>
/// Immutable; equality compares both members.
/// </remarks>
public sealed record QuoteMatch
{
    internal QuoteMatch(int occurrences, (int Start, int End)? originalRange)
    {
        Occurrences = occurrences;
        OriginalRange = originalRange;
    }

    /// <summary>
    /// Gets how often the quote occurs in the searched text, overlapping occurrences counted (the
    /// quote <c>aa</c> occurs twice in <c>aaa</c>).
    /// </summary>
    public int Occurrences { get; }

    /// <summary>
    /// Gets the half-open code-point range of the searched text's original that the one occurrence
    /// came from; set exactly when <see cref="Occurrences"/> is 1, <see langword="null"/> otherwise.
    /// </summary>
    public (int Start, int End)? OriginalRange { get; }
}
