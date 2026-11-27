using Nordstein.Core.Common.Validation;

namespace Nordstein.Core.Common.Text.Internal;

/// <summary>
/// The <see cref="ITextChunker"/>: units in start order, clipped and trimmed; long units cut into
/// pieces; pieces and units packed greedily, the next chunk backed up over whole units for overlap.
/// </summary>
internal sealed class TextChunker : ITextChunker
{
    private const string BreadcrumbSeparator = " > ";
    private const string CutMarker = "…";

    public IReadOnlyList<TextChunk> Chunk(string text, IReadOnlyList<StructureMarker> markers) => Chunk(text, markers, new TextChunkingOptions());

    public IReadOnlyList<TextChunk> Chunk(string text, IReadOnlyList<StructureMarker> markers, TextChunkingOptions options)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(markers);
        ArgumentNullException.ThrowIfNull(options);
        options.Validate();

        var codePoints = new CodePointText(text);
        markers.CheckWithin(codePoints.Length);
        (List<Unit> units, List<Heading> headings) = Units(codePoints, markers, options);

        var chunks = new List<TextChunk>();
        var breadcrumb = new Breadcrumb(headings, options.MaxBreadcrumbLength);
        int first = 0;
        while (first < units.Count)
        {
            int last = first;
            while (last + 1 < units.Count && units[last + 1].End - units[first].Start <= options.MaxChunkLength)
            {
                last++;
            }

            int start = units[first].Start;
            int end = units[last].End;
            chunks.Add(new TextChunk(chunks.Count, start, end, breadcrumb.At(start), codePoints.Slice(start, end)));
            if (last + 1 >= units.Count)
            {
                break;
            }

            // Back up over whole units while the overlap stays within its target and the next chunk
            // can still take the unit after this one, so both ends advance; always advance the start.
            int next = last + 1;
            while (next - 1 > first
                   && units[last].End - units[next - 1].Start <= options.OverlapLength
                   && units[last + 1].End - units[next - 1].Start <= options.MaxChunkLength)
            {
                next--;
            }

            first = next;
        }

        return chunks;
    }

    private static (List<Unit> Units, List<Heading> Headings) Units(CodePointText text, IReadOnlyList<StructureMarker> markers, TextChunkingOptions options)
    {
        var units = new List<Unit>();
        var headings = new List<Heading>();
        int covered = 0;
        foreach (StructureMarker marker in markers
                     .Where(marker => marker.Kind is StructureKind.Heading or StructureKind.Paragraph or StructureKind.ListItem or StructureKind.TableCell)
                     .OrderBy(marker => marker.Start)
                     .ThenByDescending(marker => marker.End))
        {
            // A marker starting inside an earlier unit keeps only its part after that unit.
            (int start, int end) = text.Trim(Math.Max(marker.Start, covered), marker.End);
            if (end <= start)
            {
                continue;
            }

            covered = end;
            if (marker.Kind == StructureKind.Heading)
            {
                headings.Add(new Heading(start, marker.Level, text.Slice(start, end)));
            }

            if (end - start <= options.MaxChunkLength)
            {
                units.Add(new Unit(start, end));
                continue;
            }

            units.AddRange(Pieces(text, start, end, options));
        }

        return (units, headings);
    }

    // The unit [start, end) is longer than the chunk cap and starts and ends on a code point that is not whitespace.
    private static IEnumerable<Unit> Pieces(CodePointText text, int start, int end, TextChunkingOptions options)
    {
        int cursor = start;
        while (true)
        {
            while (text.IsWhiteSpace(cursor))
            {
                cursor++;
            }

            if (end - cursor <= options.MaxChunkLength)
            {
                yield return new Unit(cursor, end);
                yield break;
            }

            int limit = cursor + options.MaxChunkLength;
            int cut = -1;
            for (int index = cursor; index < limit; index++)
            {
                if (IsSentenceEnd(text, index))
                {
                    cut = index + 1;
                }
            }

            if (cut < 0)
            {
                // No sentence end under the cap: the last whitespace within the sentence cap, else the cap.
                int sentenceLimit = cursor + options.MaxSentenceLength;
                for (int index = sentenceLimit - 1; index > cursor && cut < 0; index--)
                {
                    if (text.IsWhiteSpace(index))
                    {
                        cut = index;
                    }
                }

                if (cut < 0)
                {
                    cut = sentenceLimit;
                }
            }

            (int pieceStart, int pieceEnd) = text.Trim(cursor, cut);
            yield return new Unit(pieceStart, pieceEnd);
            cursor = cut;
        }
    }

    // A terminator followed by whitespace. Callers stay below the unit's end minus one, which is not
    // whitespace, so the next code point exists.
    private static bool IsSentenceEnd(CodePointText text, int index)
        => text[index].Value is '.' or '!' or '?' or '…' && text.IsWhiteSpace(index + 1);

    private readonly record struct Unit(int Start, int End);

    private sealed record Heading(int Start, int Level, string Text);

    /// <summary>
    /// The heading chain at increasing offsets: each call moves forward over the headings that start
    /// at or before the offset, so a whole chunking pass reads every heading once.
    /// </summary>
    private sealed class Breadcrumb
    {
        private readonly List<Heading> headings;
        private readonly int maxLength;
        private readonly List<Heading> chain = [];
        private int next;

        public Breadcrumb(List<Heading> headings, int maxLength)
        {
            this.headings = headings;
            this.maxLength = maxLength;
        }

        public string At(int offset)
        {
            for (; next < headings.Count && headings[next].Start <= offset; next++)
            {
                Heading heading = headings[next];
                chain.RemoveAll(outer => outer.Level >= heading.Level);
                chain.Add(heading);
            }

            return string.Join(BreadcrumbSeparator, chain.Select(heading => heading.Text)).Shorten(maxLength, CutMarker);
        }
    }
}
