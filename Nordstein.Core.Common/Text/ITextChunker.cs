namespace Nordstein.Core.Common.Text;

/// <summary>
/// Cuts a text into overlapping chunks along its structure: whole headings, paragraphs, list items
/// and table cells, packed up to a length, each chunk with the heading chain it falls under.
/// Registered as a singleton by <c>Nordstein.Core.Common.Module</c>.
/// </summary>
/// <remarks>
/// <para>
/// <b>Units.</b> The <see cref="StructureKind.Heading"/>, <see cref="StructureKind.Paragraph"/>,
/// <see cref="StructureKind.ListItem"/> and <see cref="StructureKind.TableCell"/> markers, in start
/// order (a longer marker first on a tie), whatever order they are passed in, each trimmed of
/// whitespace at both ends. A marker that starts inside an earlier unit keeps only its part after
/// that unit's end, nothing when it ends inside it. Zero-length markers, markers of only whitespace
/// and markers of other kinds are not units. Text outside every unit is in no chunk; a text without
/// unit markers has no chunks.
/// </para>
/// <para>
/// <b>Long units.</b> A unit longer than <see cref="TextChunkingOptions.MaxChunkLength"/> is cut into
/// pieces, each the longest run from where the previous ended (whitespace skipped) that ends at a
/// sentence end and fits; the rest of the unit as one piece once it fits. A stretch with no sentence
/// end that fits is cut at its last whitespace within
/// <see cref="TextChunkingOptions.MaxSentenceLength"/>, else hard at that length (on a code point,
/// which may part a letter from its combining mark). A piece never starts or ends with whitespace.
/// A sentence ends at <c>.</c>, <c>!</c>, <c>?</c> or <c>…</c> followed by whitespace: that is the
/// known limit of the rule; abbreviations (<c>e.g. this</c>) end a sentence, and other scripts'
/// terminators (<c>。</c>) and a terminator before a closing quote do not.
/// </para>
/// <para>
/// <b>Chunks.</b> A chunk is the longest run of consecutive units (and pieces) whose span, from the
/// first one's start to the last one's end, fits <see cref="TextChunkingOptions.MaxChunkLength"/>.
/// The next chunk starts at the earliest unit of the previous chunk that keeps the overlapping
/// units within <see cref="TextChunkingOptions.OverlapLength"/>, but never so early that it could
/// not reach past the previous chunk's end. So chunk starts and ends both strictly increase, no
/// chunk lies inside another, every chunk starts at a unit's start and ends at a unit's end, and
/// every code point of every unit, whitespace at the edges of a cut aside, lies in at least one
/// chunk.
/// </para>
/// <para>
/// <b>Breadcrumbs.</b> The heading units in start order form a chain: a heading ends every heading of
/// its level or deeper before it joins. A chunk's breadcrumb is the chain at its start, a heading
/// that starts there included.
/// </para>
/// <para>
/// Pure and stateless: the same input gives the same chunks; safe for any number of concurrent
/// callers.
/// </para>
/// </remarks>
public interface ITextChunker
{
    /// <summary>
    /// Chunks <paramref name="text"/> along <paramref name="markers"/> with the default
    /// <see cref="TextChunkingOptions"/>.
    /// </summary>
    /// <param name="text">The text the markers point into.</param>
    /// <param name="markers">The structure markers, in code points, in any order.</param>
    /// <returns>The chunks in order; empty when the text has no unit.</returns>
    /// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// A marker starts before 0, ends before it starts, or ends after the text; the message names the
    /// marker's index and range, never the text.
    /// </exception>
    IReadOnlyList<TextChunk> Chunk(string text, IReadOnlyList<StructureMarker> markers);

    /// <summary>
    /// Chunks <paramref name="text"/> along <paramref name="markers"/> with <paramref name="options"/>.
    /// </summary>
    /// <param name="text">The text the markers point into.</param>
    /// <param name="markers">The structure markers, in code points, in any order.</param>
    /// <param name="options">The sizes to cut to.</param>
    /// <returns>The chunks in order; empty when the text has no unit.</returns>
    /// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
    /// <exception cref="System.ComponentModel.DataAnnotations.ValidationException"><paramref name="options"/> is invalid.</exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// A marker starts before 0, ends before it starts, or ends after the text; the message names the
    /// marker's index and range, never the text.
    /// </exception>
    IReadOnlyList<TextChunk> Chunk(string text, IReadOnlyList<StructureMarker> markers, TextChunkingOptions options);
}
