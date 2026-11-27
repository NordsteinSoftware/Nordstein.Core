using System.Globalization;
using System.Text;

namespace Nordstein.Core.Common.Text.Internal;

/// <summary>
/// The <see cref="ITextNormalizer"/>: three passes, each from a string to a string with its own map
/// of every input UTF-16 position to an output position, composed onto the raw positions after each
/// pass; a last step counts the output in code points.
/// </summary>
/// <remarks>
/// Each pass maps a position to a start (the first output character from the input at or after it)
/// and an end (just after the last output character from the input before it). The passes are
/// filtering (lone surrogates and U+FFFE replaced, removal, ligatures), composition (NFC per
/// grapheme cluster, so a position never needs to be guessed across a cluster that changed) and
/// whitespace collapse.
/// </remarks>
internal sealed class TextNormalizer : ITextNormalizer
{
    private const string ParagraphBreak = "\n\n";

    public NormalizedText Normalize(string raw) => Normalize(raw, new TextNormalizationOptions());

    public NormalizedText Normalize(string raw, TextNormalizationOptions options)
    {
        ArgumentNullException.ThrowIfNull(raw);
        ArgumentNullException.ThrowIfNull(options);

        int[] starts = new int[raw.Length + 1];
        int[] ends = new int[raw.Length + 1];
        for (int index = 0; index <= raw.Length; index++)
        {
            starts[index] = index;
            ends[index] = index;
        }

        string text = Apply(Filter(raw, options), starts, ends);
        if (options.Compose)
        {
            text = Apply(Compose(text), starts, ends);
        }

        if (options.CollapseWhitespace)
        {
            text = Apply(Collapse(text), starts, ends);
        }

        int[] codePoints = CodePointOffsets(text, out int length);
        for (int index = 0; index <= raw.Length; index++)
        {
            starts[index] = codePoints[starts[index]];
            ends[index] = codePoints[ends[index]];
        }

        return new NormalizedText(text, length, starts, ends);
    }

    // Composes a pass's own maps onto the raw positions' current ones.
    private static string Apply(Pass pass, int[] starts, int[] ends)
    {
        for (int index = 0; index < starts.Length; index++)
        {
            starts[index] = pass.Starts[starts[index]];
            ends[index] = pass.Ends[ends[index]];
        }

        return pass.Output;
    }

    private static Pass Filter(string input, TextNormalizationOptions options)
    {
        var output = new StringBuilder(input.Length);
        var pass = new Pass(input.Length);
        int index = 0;
        foreach (Rune enumerated in input.EnumerateRunes())
        {
            // A lone surrogate enumerates as one replacement character of width one; U+FFFE, which
            // .NET refuses to normalize, is read the same way.
            Rune rune = enumerated.Value == 0xFFFE ? Rune.ReplacementChar : enumerated;
            int width = rune.Utf16SequenceLength;
            int at = output.Length;
            if (IsRemoved(rune, options))
            {
                pass.Set(index, at, at);
                index += width;
                continue;
            }

            output.Append(options.FoldLigatures ? Fold(rune) : rune.ToString());
            pass.Set(index, at, at);
            for (int inside = 1; inside < width; inside++)
            {
                pass.Set(index + inside, at, output.Length);
            }

            index += width;
        }

        return pass.Finish(output.ToString());
    }

    private static Pass Compose(string input)
    {
        var pass = new Pass(input.Length);
        if (input.IsNormalized(NormalizationForm.FormC))
        {
            for (int index = 0; index < input.Length; index++)
            {
                pass.Set(index, index, index);
            }

            return pass.Finish(input);
        }

        // NFC composes only inside a grapheme cluster, so composing cluster by cluster is NFC of the
        // whole, and a cluster that changed is the finest unit a position can be mapped to.
        var output = new StringBuilder(input.Length);
        int start = 0;
        while (start < input.Length)
        {
            int width = StringInfo.GetNextTextElementLength(input, start);
            ReadOnlySpan<char> cluster = input.AsSpan(start, width);
            int at = output.Length;
            if (cluster.IsNormalized(NormalizationForm.FormC))
            {
                output.Append(cluster);
                for (int inside = 0; inside < width; inside++)
                {
                    pass.Set(start + inside, at + inside, at + inside);
                }
            }
            else
            {
                output.Append(cluster.ToString().Normalize(NormalizationForm.FormC));
                pass.Set(start, at, at);
                for (int inside = 1; inside < width; inside++)
                {
                    pass.Set(start + inside, at, output.Length);
                }
            }

            start += width;
        }

        return pass.Finish(output.ToString());
    }

    private static Pass Collapse(string input)
    {
        var output = new StringBuilder(input.Length);
        var pass = new Pass(input.Length);
        var pending = new List<int>();
        int lineBreaks = -1; // -1: no whitespace run pending; otherwise the line breaks in the run
        bool afterCarriageReturn = false;
        int index = 0;
        foreach (Rune rune in input.EnumerateRunes())
        {
            int width = rune.Utf16SequenceLength;
            if (Rune.IsWhiteSpace(rune))
            {
                lineBreaks = Math.Max(lineBreaks, 0) + LineBreaksOf(rune, afterCarriageReturn);
                afterCarriageReturn = rune.Value == '\r';

                // A range ending in the run ends before its separator; one starting in it starts after
                // the separator, which is only known once the next kept character lands.
                for (int inside = 0; inside < width; inside++)
                {
                    pass.Set(index + inside, 0, output.Length);
                    pending.Add(index + inside);
                }

                index += width;
                continue;
            }

            int beforeSeparator = output.Length;
            if (lineBreaks >= 0 && output.Length > 0)
            {
                output.Append(lineBreaks >= 2 ? ParagraphBreak : " ");
            }

            lineBreaks = -1;
            afterCarriageReturn = false;
            foreach (int position in pending)
            {
                pass.SetStart(position, output.Length);
            }

            pending.Clear();
            int at = output.Length;
            output.Append(rune.ToString());
            pass.Set(index, at, beforeSeparator);
            for (int inside = 1; inside < width; inside++)
            {
                pass.Set(index + inside, at, output.Length);
            }

            index += width;
        }

        // Trailing whitespace becomes nothing: a range starting in it starts at the end.
        foreach (int position in pending)
        {
            pass.SetStart(position, output.Length);
        }

        return pass.Finish(output.ToString());
    }

    private static int LineBreaksOf(Rune rune, bool afterCarriageReturn)
        => rune.Value switch
        {
            '\n' => afterCarriageReturn ? 0 : 1,
            '\r' or 0x0085 or 0x2028 => 1,
            0x2029 => 2,
            _ => 0,
        };

    private static bool IsRemoved(Rune rune, TextNormalizationOptions options)
        => (options.RemoveInvisible && rune.Value is 0x00AD or 0x200B or 0x200C or 0x200D or 0x2060 or 0xFEFF)
            || (options.RemoveControls && rune.Value is < 0x20 or (>= 0x7F and <= 0x9F) && !Rune.IsWhiteSpace(rune));

    private static string Fold(Rune rune)
        => rune.Value switch
        {
            0xFB00 => "ff",
            0xFB01 => "fi",
            0xFB02 => "fl",
            0xFB03 => "ffi",
            0xFB04 => "ffl",
            _ => rune.ToString(),
        };

    // The code-point offset of every UTF-16 position of a well-formed text; a position inside a
    // surrogate pair gets the pair's offset (no mapped position lands there).
    private static int[] CodePointOffsets(string text, out int length)
    {
        int[] offsets = new int[text.Length + 1];
        length = 0;
        for (int index = 0; index < text.Length; index++)
        {
            offsets[index] = char.IsLowSurrogate(text[index]) ? length - 1 : length++;
        }

        offsets[text.Length] = length;
        return offsets;
    }

    /// <summary>
    /// One pass's output and the start and end of every input UTF-16 position in it.
    /// </summary>
    private sealed class Pass
    {
        public Pass(int inputLength)
        {
            Starts = new int[inputLength + 1];
            Ends = new int[inputLength + 1];
        }

        public int[] Starts { get; }

        public int[] Ends { get; }

        public string Output { get; private set; } = string.Empty;

        public void Set(int position, int start, int end)
        {
            Starts[position] = start;
            Ends[position] = end;
        }

        public void SetStart(int position, int start) => Starts[position] = start;

        public Pass Finish(string output)
        {
            Output = output;
            Starts[^1] = output.Length;
            Ends[^1] = output.Length;
            return this;
        }
    }
}
