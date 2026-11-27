using System.ComponentModel.DataAnnotations;
using Check = Nordstein.Core.Common.Validation.Validation;

namespace Nordstein.Core.Common.Text;

/// <summary>
/// The sizes <see cref="ITextChunker"/> cuts to. Lengths are code points (about four per token of
/// running Latin text), except <see cref="MaxBreadcrumbLength"/>.
/// </summary>
/// <remarks>
/// Immutable. The chunker validates the options on every call and throws
/// <see cref="ValidationException"/> for sizes it cannot honour.
/// </remarks>
public sealed record TextChunkingOptions : IValidatableObject
{
    /// <summary>
    /// Gets the longest chunk in code points; above 0. Default 2048.
    /// </summary>
    public int MaxChunkLength { get; init; } = 2048;

    /// <summary>
    /// Gets the overlap the chunker aims for between consecutive chunks, in code points, realized in
    /// whole units (the overlapping units together are at most this long); at least 0. Default 300.
    /// </summary>
    public int OverlapLength { get; init; } = 300;

    /// <summary>
    /// Gets the longest stretch without a sentence end that the chunker keeps whole when it must cut
    /// a unit longer than <see cref="MaxChunkLength"/>, in code points: such a stretch is cut at the
    /// last whitespace before this length, or at this length when there is none. Above 0 and at most
    /// <see cref="MaxChunkLength"/>. Default 2048.
    /// </summary>
    public int MaxSentenceLength { get; init; } = 2048;

    /// <summary>
    /// Gets the longest <see cref="TextChunk.Breadcrumb"/> in UTF-16 code units (as
    /// <see cref="string.Length"/> counts, so it fits a column of that width); a longer one is cut,
    /// never inside a surrogate pair, and ends in <c>…</c>. Above 0. Default 1024.
    /// </summary>
    public int MaxBreadcrumbLength { get; init; } = 1024;

    /// <summary>
    /// Refuses sizes the chunker cannot honour: a sentence cap above the chunk cap would let a piece
    /// exceed the chunk it lands in.
    /// </summary>
    /// <param name="validationContext">The validation context.</param>
    /// <returns>One result per broken rule, each naming its member.</returns>
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        yield return Check.GreaterThan(MaxChunkLength, 0, nameof(MaxChunkLength));
        yield return Check.NotNegative(OverlapLength, nameof(OverlapLength));
        yield return Check.GreaterThan(MaxSentenceLength, 0, nameof(MaxSentenceLength));
        yield return Check.LessThanOrEqual(MaxSentenceLength, MaxChunkLength, nameof(MaxSentenceLength));
        yield return Check.GreaterThan(MaxBreadcrumbLength, 0, nameof(MaxBreadcrumbLength));
    }
}
