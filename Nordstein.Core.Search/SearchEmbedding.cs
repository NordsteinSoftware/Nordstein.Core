namespace Nordstein.Core.Search;

/// <summary>
/// The vector of one entry, together with the text it was computed from. The index stores the
/// vector only while the entry still holds exactly that text, so a vector of an outdated text is
/// never stored.
/// </summary>
/// <param name="EntryId">The entry the vector belongs to.</param>
/// <param name="Text">The entry text the vector was computed from.</param>
/// <param name="Vector">
/// The vector: as many floats as the index's dimension, each finite.
/// </param>
public sealed record SearchEmbedding(Guid EntryId, string Text, ReadOnlyMemory<float> Vector);
