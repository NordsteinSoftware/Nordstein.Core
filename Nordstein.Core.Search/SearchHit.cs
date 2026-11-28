namespace Nordstein.Core.Search;

/// <summary>
/// One fused search result.
/// </summary>
/// <param name="EntryId">The entry that matched.</param>
/// <param name="OwnerId">The entry's owner.</param>
/// <param name="Text">The entry's text.</param>
/// <param name="Score">
/// The reciprocal rank fusion score: the sum of <c>1 / (k + rank)</c> over the legs that found the
/// entry. Comparable only within one search.
/// </param>
/// <param name="VectorRank">The entry's 1-based rank in the vector leg, or null when that leg did not find it.</param>
/// <param name="TextRank">The entry's 1-based rank in the keyword leg, or null when that leg did not find it.</param>
public sealed record SearchHit(Guid EntryId, Guid OwnerId, string Text, double Score, int? VectorRank, int? TextRank);
