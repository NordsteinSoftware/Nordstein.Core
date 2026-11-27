namespace Nordstein.Core.Common.Text;

/// <summary>
/// One chunk of a text, as <see cref="ITextChunker"/> cuts it: its position in the sequence, its
/// half-open code-point range, the heading chain in effect where it starts and its text.
/// </summary>
/// <param name="Ordinal">The chunk's position, counted from 0 without gaps.</param>
/// <param name="Start">The first code point of the chunk in the chunked text, inclusive.</param>
/// <param name="End">The code point after the last, exclusive; always after <paramref name="Start"/>.</param>
/// <param name="Breadcrumb">
/// The texts of the headings in effect at <paramref name="Start"/>, outermost first, joined by
/// <c> &gt; </c> and cut to <see cref="TextChunkingOptions.MaxBreadcrumbLength"/>; empty when no
/// heading precedes the chunk.
/// </param>
/// <param name="Text">The chunked text's code points from <paramref name="Start"/> to <paramref name="End"/>, exactly.</param>
public sealed record TextChunk(int Ordinal, int Start, int End, string Breadcrumb, string Text);
