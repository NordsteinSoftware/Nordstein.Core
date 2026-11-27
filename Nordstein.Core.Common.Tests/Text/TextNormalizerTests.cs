using System.Text;
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Nordstein.Core.Common.Text;
using Nordstein.Core.Testing;

namespace Nordstein.Core.Common.Tests.Text;

/// <summary>
/// Text normalization: removal, ligature folding, NFC per grapheme cluster and whitespace collapse,
/// each step switchable, with an exact map from every raw UTF-16 position to the normalized code points.
/// </summary>
[TestClass]
public sealed class TextNormalizerTests : BaseTest<Module>
{
    private static readonly TextNormalizationOptions NothingOn = new()
    {
        Compose = false,
        FoldLigatures = false,
        RemoveInvisible = false,
        RemoveControls = false,
        CollapseWhitespace = false,
    };

    // Inputs that exercise every step and the boundaries between them.
    private static readonly string[] Tricky =
    [
        string.Empty,
        " ",
        "\u00AD\u200B",
        "\uFB01nd pru\u0308fung",
        "  one \t two\nthree\n\n\n four  ",
        "a\u0000b\u0007c\td\u0085e\u001Cf",
        "e\u200D\u0301 \u0301x",
        "\uFB01\u0301\r\n\r\n\U0001F600\u0301",
        "a\uD800b\uDC00 \uD83D",
        "\u1100\u1161\u11A8 a\u0301\u0323",
        "x\u2029y\u2028z\r\rw",
        "\U0001F468\u200D\U0001F469 ok.",
    ];

    [TestMethod]
    public void Normalize_LigatureAndDecomposedUmlaut_FoldsAndComposes()
    {
        NormalizedText result = Normalizer().Normalize("\uFB01nd pru\u0308fung");

        (result.Text, result.Length, result.RawLength).Should().Be(("find pr\u00FCfung", 12, 12));
    }

    [TestMethod]
    [DataRow("\u00AD", DisplayName = "soft hyphen")]
    [DataRow("\u200B", DisplayName = "zero-width space")]
    [DataRow("\u200C", DisplayName = "zero-width non-joiner")]
    [DataRow("\u200D", DisplayName = "zero-width joiner")]
    [DataRow("\u2060", DisplayName = "word joiner")]
    [DataRow("\uFEFF", DisplayName = "byte order mark")]
    [DataRow("\u0000", DisplayName = "NUL")]
    [DataRow("\u0007", DisplayName = "BEL")]
    [DataRow("\u001C", DisplayName = "file separator")]
    [DataRow("\u007F", DisplayName = "DEL")]
    [DataRow("\u0090", DisplayName = "a C1 control")]
    [DataRow("\u009F", DisplayName = "the last C1 control")]
    public void Normalize_InvisibleOrControlCharacter_IsRemovedAndTheNeighboursStayJoined(string removed)
    {
        NormalizedText result = Normalizer().Normalize($"a{removed}b");

        result.Text.Should().Be("ab");
        Starts(result).Should().Equal(0, 1, 1, 2);
        Ends(result).Should().Equal(0, 1, 1, 2);
    }

    [TestMethod]
    public void Normalize_WhitespaceControls_AreWhitespaceNotRemoved()
    {
        NormalizedText result = Normalizer().Normalize("a\tb\u000Bc\u000Cd\u0085e\u00A0f\u3000g");

        result.Text.Should().Be("a b c d e f g");
    }

    [TestMethod]
    public void Normalize_WhitespaceRuns_CollapseAndParagraphBreaksSurvive()
    {
        NormalizedText result = Normalizer().Normalize("  one \t two\nthree\n\n\n four  ");

        result.Text.Should().Be("one two three\n\nfour");
    }

    [TestMethod]
    [DataRow("a\nb", "a b", DisplayName = "LF")]
    [DataRow("a\r\nb", "a b", DisplayName = "CRLF is one break")]
    [DataRow("a\rb", "a b", DisplayName = "CR")]
    [DataRow("a\n\nb", "a\n\nb", DisplayName = "LF LF")]
    [DataRow("a\r\n\r\nb", "a\n\nb", DisplayName = "CRLF CRLF")]
    [DataRow("a\r\rb", "a\n\nb", DisplayName = "CR CR")]
    [DataRow("a\n\rb", "a\n\nb", DisplayName = "LF CR")]
    [DataRow("a\r\n\nb", "a\n\nb", DisplayName = "CRLF LF")]
    [DataRow("a\r \nb", "a\n\nb", DisplayName = "CR space LF")]
    [DataRow("a\u0085\u0085b", "a\n\nb", DisplayName = "NEL NEL")]
    [DataRow("a\u2028b", "a b", DisplayName = "line separator")]
    [DataRow("a\u2028\u2028b", "a\n\nb", DisplayName = "two line separators")]
    [DataRow("a\u2029b", "a\n\nb", DisplayName = "paragraph separator")]
    [DataRow("a\f\fb", "a b", DisplayName = "form feeds are no line break")]
    [DataRow("a\v\vb", "a b", DisplayName = "vertical tabs are no line break")]
    public void Normalize_MixedLineEndings_CountLineBreaksOncePerBreak(string raw, string expected)
    {
        Normalizer().Normalize(raw).Text.Should().Be(expected);
    }

    [TestMethod]
    [DataRow("\uFB00", "ff")]
    [DataRow("\uFB01", "fi")]
    [DataRow("\uFB02", "fl")]
    [DataRow("\uFB03", "ffi")]
    [DataRow("\uFB04", "ffl")]
    public void Normalize_EachLigature_IsFoldedToItsLetters(string ligature, string letters)
    {
        NormalizedText result = Normalizer().Normalize($"o{ligature}ce");

        result.Text.Should().Be($"o{letters}ce");
        result.Length.Should().Be(3 + letters.Length);
        Starts(result).Should().Equal(0, 1, 1 + letters.Length, 2 + letters.Length, 3 + letters.Length);
        Ends(result).Should().Equal(0, 1, 1 + letters.Length, 2 + letters.Length, 3 + letters.Length);
    }

    [TestMethod]
    public void Normalize_OffsetMap_PointsRawIndicesAtTheirCodePoints()
    {
        // a ﬁ ' ' (high low) b: the ligature becomes two code points, the emoji one.
        NormalizedText result = Normalizer().Normalize("a\uFB01 \U0001F600b");

        (result.Text, result.Length).Should().Be(("afi \U0001F600b", 6));
        Starts(result).Should().Equal(0, 1, 4, 4, 4, 5, 6);
        Ends(result).Should().Equal(0, 1, 3, 3, 5, 5, 6);
    }

    [TestMethod]
    public void Normalize_RangeStartingOnIndentation_StartsOnItsTextNotOnTheParagraphBreak()
    {
        // "One.\n\n  Two.": a range beginning on the indentation (raw 6) begins at "Two." (code point 6).
        NormalizedText result = Normalizer().Normalize("One.\n\n  Two.");

        result.Text.Should().Be("One.\n\nTwo.");
        result.StartOffsetOf(6).Should().Be(6);
        result.StartOffsetOf(4).Should().Be(6, "a range starting on the break starts after it");
        result.EndOffsetOf(4).Should().Be(4, "a range ending at the first newline ends after 'One.'");
        result.EndOffsetOf(8).Should().Be(4, "a range ending before 'Two.' never ends on the separator");
        result.ToNormalizedRange(8, 12).Should().Be((6, 10));
    }

    [TestMethod]
    public void Normalize_EmptyInput_IsEmptyWithOnePosition()
    {
        NormalizedText result = Normalizer().Normalize(string.Empty);

        (result.Text, result.Length, result.RawLength).Should().Be((string.Empty, 0, 0));
        (result.StartOffsetOf(0), result.EndOffsetOf(0), result.ToNormalizedRange(0, 0)).Should().Be((0, 0, (0, 0)));
    }

    [TestMethod]
    public void Normalize_InputThatNormalizesToEmpty_MapsEveryPositionToZero()
    {
        const string raw = "  \u00AD\t\u200B \r\n\u0000";

        NormalizedText result = Normalizer().Normalize(raw);

        (result.Text, result.Length, result.RawLength).Should().Be((string.Empty, 0, raw.Length));
        Starts(result).Should().OnlyContain(offset => offset == 0);
        Ends(result).Should().OnlyContain(offset => offset == 0);
        result.ToNormalizedRange(0, raw.Length).Should().Be((0, 0));
    }

    [TestMethod]
    public void Normalize_SurrogatePair_APositionBetweenItsHalvesWidensToTheWholeCharacter()
    {
        NormalizedText result = Normalizer().Normalize("x\U0001F600y");

        result.StartOffsetOf(2).Should().Be(1, "a range starting inside the emoji starts before it");
        result.EndOffsetOf(2).Should().Be(2, "a range ending inside the emoji ends after it");
        result.ToNormalizedRange(1, 2).Should().Be((1, 2), "half the emoji maps to the whole emoji");
        result.ToNormalizedRange(2, 4).Should().Be((1, 3));
    }

    [TestMethod]
    public void Normalize_LoneSurrogate_ReadsAsTheReplacementCharacter()
    {
        // Built in code, not as [DataRow] constants: attribute metadata cannot carry a lone surrogate.
        (string Raw, string Expected)[] cases =
        [
            ("a\uD800b", "a\uFFFDb"),
            ("a\uDC00b", "a\uFFFDb"),
            ("a\uD83D", "a\uFFFD"),
            ("\uDC00\uD800", "\uFFFD\uFFFD"),
        ];

        foreach ((string raw, string expected) in cases)
        {
            NormalizedText result = Normalizer().Normalize(raw);

            result.Text.Should().Be(expected);
            result.Length.Should().Be(new CodePointText(raw).Length);
            Starts(result).Should().Equal(Enumerable.Range(0, raw.Length + 1));
        }
    }

    [TestMethod]
    public void Normalize_ByteSwappedByteOrderMark_ReadsAsTheReplacementCharacterInsteadOfThrowing()
    {
        // .NET refuses to compose a text holding U+FFFE; it reads as U+FFFD, like a lone surrogate.
        Normalizer().Normalize("e\u0301\uFFFEx").Text.Should().Be("\u00E9\uFFFDx");
        Normalizer().Normalize("a\uFFFE", NothingOn).Text.Should().Be("a\uFFFD");
        Normalizer().Normalize("a\uFFFF\uFDD0").Text.Should().Be("a\uFFFF\uFDD0", "other noncharacters compose and stay");
    }

    [TestMethod]
    public void Normalize_LoneSurrogateWithEveryStepOff_IsStillReplaced()
    {
        Normalizer().Normalize("a\uD800", NothingOn).Text.Should().Be("a\uFFFD");
    }

    [TestMethod]
    public void Normalize_CombiningMark_ComposesAndAPositionInsideTheClusterWidens()
    {
        NormalizedText result = Normalizer().Normalize("e\u0301x");

        (result.Text, result.Length).Should().Be(("\u00E9x", 2));
        Starts(result).Should().Equal(0, 0, 1, 2);
        Ends(result).Should().Equal(0, 1, 1, 2);
        result.ToNormalizedRange(0, 1).Should().Be((0, 1), "the bare letter of a composed cluster maps to the composed character");
        result.ToNormalizedRange(1, 2).Should().Be((0, 1), "the mark alone maps to the composed character too");
    }

    [TestMethod]
    public void Normalize_CombiningMarksOutOfCanonicalOrder_AreReorderedAndComposed()
    {
        // a + acute (above, 230) + dot below (220): NFC orders the dot first and composes it.
        NormalizedText result = Normalizer().Normalize("a\u0301\u0323");

        (result.Text, result.Length).Should().Be(("\u1EA1\u0301", 2));
        Starts(result).Should().Equal(0, 0, 0, 2);
        Ends(result).Should().Equal(0, 2, 2, 2);
    }

    [TestMethod]
    public void Normalize_HangulJamo_ComposeIntoOneSyllable()
    {
        NormalizedText result = Normalizer().Normalize("\u1100\u1161\u11A8!");

        (result.Text, result.Length).Should().Be(("\uAC01!", 2));
        Starts(result).Should().Equal(0, 0, 0, 1, 2);
        Ends(result).Should().Equal(0, 1, 1, 1, 2);
    }

    [TestMethod]
    public void Normalize_UnchangedClusterInATextThatChanges_MapsOneToOne()
    {
        // The emoji cluster is already composed; only the trailing letter and mark compose.
        NormalizedText result = Normalizer().Normalize("\U0001F600 e\u0301", new TextNormalizationOptions { CollapseWhitespace = false });

        result.Text.Should().Be("\U0001F600 \u00E9");
        Starts(result).Should().Equal(0, 0, 1, 2, 2, 3);
        Ends(result).Should().Equal(0, 1, 1, 2, 3, 3);
    }

    [TestMethod]
    public void Normalize_RemovedCharacterBetweenLetterAndMark_TheyStillCompose()
    {
        NormalizedText result = Normalizer().Normalize("e\u200D\u0301");

        result.Text.Should().Be("\u00E9");
        Starts(result).Should().Equal(0, 0, 0, 1);
        Ends(result).Should().Equal(0, 1, 1, 1);
    }

    [TestMethod]
    public void Normalize_LigatureBeforeAMark_ComposesAsItsLetters()
    {
        Normalizer().Normalize("\uFB01\u0301").Text.Should().Be("f\u00ED");
    }

    [TestMethod]
    public void Normalize_ComposeOff_KeepsDecomposedTextWithOneToOneOffsets()
    {
        NormalizedText result = Normalizer().Normalize("e\u0301x", new TextNormalizationOptions { Compose = false });

        (result.Text, result.Length).Should().Be(("e\u0301x", 3));
        Starts(result).Should().Equal(0, 1, 2, 3);
    }

    [TestMethod]
    public void Normalize_FoldLigaturesOff_KeepsTheLigature()
    {
        Normalizer().Normalize("\uFB01x", new TextNormalizationOptions { FoldLigatures = false }).Text.Should().Be("\uFB01x");
    }

    [TestMethod]
    public void Normalize_RemoveInvisibleOff_KeepsInvisibleButStillRemovesControls()
    {
        NormalizedText result = Normalizer().Normalize("a\u00AD\u0000b", new TextNormalizationOptions { RemoveInvisible = false });

        result.Text.Should().Be("a\u00ADb");
    }

    [TestMethod]
    public void Normalize_RemoveControlsOff_KeepsControlsButStillRemovesInvisible()
    {
        NormalizedText result = Normalizer().Normalize("a\u00AD\u0000b", new TextNormalizationOptions { RemoveControls = false });

        result.Text.Should().Be("a\u0000b");
    }

    [TestMethod]
    public void Normalize_CollapseWhitespaceOff_KeepsWhitespaceVerbatimWithOneToOneOffsets()
    {
        const string raw = "  a \t\n\n b ";

        NormalizedText result = Normalizer().Normalize(raw, new TextNormalizationOptions { CollapseWhitespace = false });

        result.Text.Should().Be(raw);
        Starts(result).Should().Equal(Enumerable.Range(0, raw.Length + 1));
        Ends(result).Should().Equal(Enumerable.Range(0, raw.Length + 1));
    }

    [TestMethod]
    public void Normalize_EveryStepOff_IsTheIdentity()
    {
        const string raw = " \uFB01\u00AD\u0000e\u0301\n\n";

        NormalizedText result = Normalizer().Normalize(raw, NothingOn);

        result.Text.Should().Be(raw);
        Starts(result).Should().Equal(Enumerable.Range(0, raw.Length + 1));
        Ends(result).Should().Equal(Enumerable.Range(0, raw.Length + 1));
    }

    [TestMethod]
    public void Normalize_Tricky_IsComposedAndIdempotent()
    {
        ITextNormalizer normalizer = Normalizer();

        foreach (string raw in Tricky)
        {
            string once = normalizer.Normalize(raw).Text;

            once.IsNormalized(NormalizationForm.FormC).Should().BeTrue(raw);
            normalizer.Normalize(once).Text.Should().Be(once, raw);
        }
    }

    [TestMethod]
    public void Normalize_Tricky_MapsAreBoundedNonDecreasingAndCoverTheWholeText()
    {
        ITextNormalizer normalizer = Normalizer();

        foreach (string raw in Tricky)
        {
            NormalizedText result = normalizer.Normalize(raw);

            result.Length.Should().Be(new CodePointText(result.Text).Length, raw);
            Starts(result).Should().BeInAscendingOrder(raw).And.OnlyContain(offset => offset >= 0 && offset <= result.Length, raw);
            Ends(result).Should().BeInAscendingOrder(raw).And.OnlyContain(offset => offset >= 0 && offset <= result.Length, raw);
            (result.StartOffsetOf(0), result.EndOffsetOf(raw.Length)).Should().Be((0, result.Length), raw);
            result.ToNormalizedRange(0, raw.Length).Should().Be((0, result.Length), raw);
        }
    }

    [TestMethod]
    public void Normalize_Tricky_EveryRangeMapsInOrderAndWithinItsWiderRange()
    {
        ITextNormalizer normalizer = Normalizer();

        foreach (string raw in Tricky)
        {
            NormalizedText result = normalizer.Normalize(raw);
            for (int start = 0; start <= raw.Length; start++)
            {
                for (int end = start; end <= raw.Length; end++)
                {
                    (int mappedStart, int mappedEnd) = result.ToNormalizedRange(start, end);
                    (int wideStart, int wideEnd) = result.ToNormalizedRange(0, raw.Length);

                    mappedStart.Should().BeLessThanOrEqualTo(mappedEnd, $"{raw} [{start},{end})");
                    mappedStart.Should().BeGreaterThanOrEqualTo(wideStart);
                    mappedEnd.Should().BeLessThanOrEqualTo(wideEnd);
                }
            }
        }
    }

    [TestMethod]
    public void ToNormalizedRange_OnlyWhitespace_IsEmptyAtTheNextText()
    {
        NormalizedText result = Normalizer().Normalize("a   b");

        result.ToNormalizedRange(1, 4).Should().Be((2, 2));
        result.ToNormalizedRange(2, 2).Should().Be((2, 2), "an empty range inside the run sits after the separator");
    }

    [TestMethod]
    public void ToNormalizedRange_WholeWordsAroundARun_KeepTheSeparatorInside()
    {
        NormalizedText result = Normalizer().Normalize("one \n two");

        result.ToNormalizedRange(0, 9).Should().Be((0, 7));
        result.ToNormalizedRange(0, 5).Should().Be((0, 3), "the run at the end of the range is not part of it");
        result.ToNormalizedRange(3, 9).Should().Be((4, 7), "the run at the start of the range is not part of it");
    }

    [TestMethod]
    [DataRow(-1, 0, DisplayName = "negative start")]
    [DataRow(4, 4, DisplayName = "start beyond the raw text")]
    [DataRow(2, 1, DisplayName = "end before start")]
    [DataRow(0, 4, DisplayName = "end beyond the raw text")]
    public void ToNormalizedRange_OutOfRange_Throws(int start, int end)
    {
        NormalizedText result = Normalizer().Normalize("abc");

        Action act = () => result.ToNormalizedRange(start, end);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [TestMethod]
    [DataRow(-1)]
    [DataRow(4)]
    public void OffsetOf_PositionOutsideTheRawText_Throws(int rawIndex)
    {
        NormalizedText result = Normalizer().Normalize("abc");

        Action start = () => result.StartOffsetOf(rawIndex);
        Action end = () => result.EndOffsetOf(rawIndex);

        start.Should().Throw<ArgumentOutOfRangeException>().WithParameterName("rawIndex");
        end.Should().Throw<ArgumentOutOfRangeException>().WithParameterName("rawIndex");
    }

    [TestMethod]
    public void Normalize_NullArguments_Throw()
    {
        ITextNormalizer normalizer = Normalizer();

        // WHY: the null-forgiving operator is banned, tests included. A default array element is a
        // genuine null the compiler types as non-null, as a mis-wired caller would pass.
        string missingText = (new string[1])[0];
        TextNormalizationOptions missingOptions = (new TextNormalizationOptions[1])[0];

        Action noText = () => normalizer.Normalize(missingText);
        Action noOptions = () => normalizer.Normalize("a", missingOptions);

        noText.Should().Throw<ArgumentNullException>().WithParameterName("raw");
        noOptions.Should().Throw<ArgumentNullException>().WithParameterName("options");
    }

    [TestMethod]
    public void Normalize_ConcurrentCallers_GetTheSameResults()
    {
        ITextNormalizer normalizer = Normalizer();
        string[] expected = [.. Tricky.Select(raw => normalizer.Normalize(raw).Text)];

        string[][] results = new string[16][];
        Parallel.For(0, results.Length, worker => results[worker] = [.. Tricky.Select(raw => normalizer.Normalize(raw).Text)]);

        results.Should().AllSatisfy(result => result.Should().Equal(expected));
    }

    [TestMethod]
    public void Module_RegistersTheNormalizerOnce()
    {
        IServiceProvider services = GetServices();

        services.GetRequiredService<ITextNormalizer>().Should().BeSameAs(services.GetRequiredService<ITextNormalizer>());
    }

    private static IEnumerable<int> Starts(NormalizedText result) => Enumerable.Range(0, result.RawLength + 1).Select(result.StartOffsetOf);

    private static IEnumerable<int> Ends(NormalizedText result) => Enumerable.Range(0, result.RawLength + 1).Select(result.EndOffsetOf);

    private ITextNormalizer Normalizer() => GetServices().GetRequiredService<ITextNormalizer>();
}
