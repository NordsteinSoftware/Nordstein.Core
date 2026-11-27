using System.ComponentModel.DataAnnotations;
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Nordstein.Core.Common.Tests.Text.Internal;
using Nordstein.Core.Common.Text;
using Nordstein.Core.Testing;

namespace Nordstein.Core.Common.Tests.Text;

/// <summary>
/// Structure-aware chunking: whole units packed to the cap with overlap, long units cut at sentence
/// ends, then whitespace, then hard; markers taken in start order, clipped where they overlap, checked
/// against the text; breadcrumbs follow the heading chain; chunks cover every unit.
/// </summary>
[TestClass]
public sealed class TextChunkerTests : BaseTest<Module>
{
    [TestMethod]
    public void Chunk_ParagraphsUnderCap_OneChunkCoveringAll()
    {
        StructuredText text = StructuredText.FromBlocks("# T\n\nOne.\n\nTwo.");

        IReadOnlyList<TextChunk> chunks = Chunker().Chunk(text.Text, text.Markers);

        chunks.Should().Equal(new TextChunk(0, 0, text.Text.Length, "T", text.Text));
    }

    [TestMethod]
    public void Chunk_ParagraphsOverCap_PacksWholeParagraphsAndOverlaps()
    {
        StructuredText text = StructuredText.FromBlocks(string.Join("\n\n", Enumerable.Range(1, 10).Select(index => $"Paragraph {index} has a sentence.")));
        var options = new TextChunkingOptions { MaxChunkLength = 80, OverlapLength = 30, MaxSentenceLength = 80 };

        IReadOnlyList<TextChunk> chunks = Chunker().Chunk(text.Text, text.Markers, options);

        chunks.Should().HaveCountGreaterThan(2);
        chunks.Select(chunk => chunk.Ordinal).Should().Equal(Enumerable.Range(0, chunks.Count));
        foreach (TextChunk chunk in chunks)
        {
            (chunk.End - chunk.Start).Should().BeLessThanOrEqualTo(80);
            text.Markers.Should().Contain(marker => marker.Start == chunk.Start, "every chunk starts on a paragraph");
            text.Markers.Should().Contain(marker => marker.End == chunk.End, "every chunk ends on a paragraph");
        }

        for (int index = 1; index < chunks.Count; index++)
        {
            chunks[index].Start.Should().BeLessThan(chunks[index - 1].End, "consecutive chunks overlap");
            chunks[index].Start.Should().BeGreaterThan(chunks[index - 1].Start, "chunks always advance");
        }

        chunks[^1].End.Should().Be(text.Text.Length);
    }

    [TestMethod]
    public void Chunk_ParagraphLongerThanCap_SplitsAtSentenceEnds()
    {
        StructuredText text = StructuredText.FromBlocks(string.Join(" ", Enumerable.Range(1, 12).Select(index => $"Sentence number {index} ends here.")));
        var options = new TextChunkingOptions { MaxChunkLength = 100, OverlapLength = 0, MaxSentenceLength = 100 };

        IReadOnlyList<TextChunk> chunks = Chunker().Chunk(text.Text, text.Markers, options);

        chunks.Should().HaveCountGreaterThan(1);
        chunks.Should().OnlyContain(chunk => chunk.Text.EndsWith('.') && !chunk.Text.StartsWith(' ') && chunk.End - chunk.Start <= 100);
    }

    [TestMethod]
    [DataRow("!")]
    [DataRow("?")]
    [DataRow("\u2026")]
    public void Chunk_EveryTerminator_EndsASentence(string terminator)
    {
        string content = $"Aaaa bbbb{terminator} Cccc dddd{terminator} Eeee";
        var options = new TextChunkingOptions { MaxChunkLength = 12, OverlapLength = 0, MaxSentenceLength = 12 };

        IReadOnlyList<TextChunk> chunks = Chunker().Chunk(content, [new StructureMarker(StructureKind.Paragraph, 0, content.Length)], options);

        chunks.Select(chunk => chunk.Text).Should().Equal($"Aaaa bbbb{terminator}", $"Cccc dddd{terminator}", "Eeee");
    }

    [TestMethod]
    public void Chunk_TerminatorInsideAWord_IsNoSentenceEnd()
    {
        // "3.14" holds no sentence end: the stretch is cut at its last whitespace instead.
        const string content = "Value 3.14 units, more";
        var options = new TextChunkingOptions { MaxChunkLength = 12, OverlapLength = 0, MaxSentenceLength = 12 };

        IReadOnlyList<TextChunk> chunks = Chunker().Chunk(content, [new StructureMarker(StructureKind.Paragraph, 0, content.Length)], options);

        chunks.Select(chunk => chunk.Text).Should().Equal("Value 3.14", "units, more");
    }

    [TestMethod]
    public void Chunk_SentenceLongerThanMaxSentence_SplitsAtWhitespaceAsLastResort()
    {
        string content = string.Join(" ", Enumerable.Range(1, 60).Select(index => $"w{index}"));
        var options = new TextChunkingOptions { MaxChunkLength = 50, OverlapLength = 0, MaxSentenceLength = 50 };

        IReadOnlyList<TextChunk> chunks = Chunker().Chunk(content, [new StructureMarker(StructureKind.Paragraph, 0, content.Length)], options);

        chunks.Should().HaveCountGreaterThan(1);
        chunks.Should().OnlyContain(chunk => chunk.End - chunk.Start <= 50 && !chunk.Text.StartsWith(' ') && !chunk.Text.EndsWith(' '));
    }

    [TestMethod]
    public void Chunk_StretchWithoutWhitespace_IsCutHardAtTheSentenceCap()
    {
        string content = new('a', 25);
        var options = new TextChunkingOptions { MaxChunkLength = 10, OverlapLength = 0, MaxSentenceLength = 8 };

        IReadOnlyList<TextChunk> chunks = Chunker().Chunk(content, [new StructureMarker(StructureKind.Paragraph, 0, 25)], options);

        chunks.Select(chunk => (chunk.Start, chunk.End)).Should().Equal((0, 8), (8, 16), (16, 25));
    }

    [TestMethod]
    public void Chunk_RestOfALongUnitThatFits_IsOnePieceWithoutASentenceEnd()
    {
        const string content = "Alpha beta gamma. Delta epsilon zeta";
        var options = new TextChunkingOptions { MaxChunkLength = 20, OverlapLength = 0, MaxSentenceLength = 20 };

        IReadOnlyList<TextChunk> chunks = Chunker().Chunk(content, [new StructureMarker(StructureKind.Paragraph, 0, content.Length)], options);

        chunks.Select(chunk => (chunk.Start, chunk.End, chunk.Text)).Should().Equal((0, 17, "Alpha beta gamma."), (18, 36, "Delta epsilon zeta"));
    }

    [TestMethod]
    public void Chunk_LongUnitEndingInWhitespace_DropsTheTrailingWhitespace()
    {
        const string content = "Aaa bbb. Ccc ddd.   ";
        var options = new TextChunkingOptions { MaxChunkLength = 10, OverlapLength = 0, MaxSentenceLength = 10 };

        IReadOnlyList<TextChunk> chunks = Chunker().Chunk(content, [new StructureMarker(StructureKind.Paragraph, 0, content.Length)], options);

        chunks.Select(chunk => (chunk.Start, chunk.End, chunk.Text)).Should().Equal((0, 8, "Aaa bbb."), (9, 17, "Ccc ddd."));
    }

    [TestMethod]
    public void Chunk_WordsSeparatedByTwoSpaces_AreCutWithoutEitherSpace()
    {
        const string content = "w1  w2  w3  w4";
        var options = new TextChunkingOptions { MaxChunkLength = 4, OverlapLength = 0, MaxSentenceLength = 4 };

        IReadOnlyList<TextChunk> chunks = Chunker().Chunk(content, [new StructureMarker(StructureKind.Paragraph, 0, content.Length)], options);

        chunks.Select(chunk => chunk.Text).Should().Equal("w1", "w2", "w3", "w4");
    }

    [TestMethod]
    public void Chunk_SingleUnitLargerThanTheCapWithAstralCharacters_CountsCodePoints()
    {
        // Each word is an emoji and a letter: two code points, three UTF-16 units.
        string content = string.Join(" ", Enumerable.Range(0, 20).Select(_ => "\U0001F600a"));
        int length = new CodePointText(content).Length;
        var options = new TextChunkingOptions { MaxChunkLength = 10, OverlapLength = 0, MaxSentenceLength = 10 };

        IReadOnlyList<TextChunk> chunks = Chunker().Chunk(content, [new StructureMarker(StructureKind.Paragraph, 0, length)], options);

        chunks.Should().HaveCountGreaterThan(1);
        chunks.Should().OnlyContain(chunk => chunk.End - chunk.Start <= 10);
        chunks.Should().OnlyContain(chunk => chunk.Text == new CodePointText(content).Slice(chunk.Start, chunk.End));
        chunks[^1].End.Should().Be(length);
    }

    [TestMethod]
    public void Chunk_HeadingChain_BreadcrumbReflectsNesting()
    {
        StructuredText text = StructuredText.FromBlocks("# Report\n\n## Costs\n\n### Travel\n\nFlights.\n\n## Revenue\n\nSales.");
        var options = new TextChunkingOptions { MaxChunkLength = 20, OverlapLength = 0, MaxSentenceLength = 20 };

        IReadOnlyList<TextChunk> chunks = Chunker().Chunk(text.Text, text.Markers, options);

        // The first chunk starts on "Report": the chain in effect there is that heading alone.
        chunks.Select(chunk => chunk.Breadcrumb).Should().Equal("Report", "Report > Costs > Travel", "Report > Revenue");
    }

    [TestMethod]
    public void Chunk_NoHeadingBeforeTheChunk_HasAnEmptyBreadcrumb()
    {
        StructuredText text = StructuredText.FromBlocks("Intro.\n\n# Later");

        Chunker().Chunk(text.Text, text.Markers, new TextChunkingOptions { MaxChunkLength = 6, MaxSentenceLength = 6 })
            .Select(chunk => chunk.Breadcrumb).Should().Equal(string.Empty, "Later");
    }

    [TestMethod]
    public void Chunk_HeadingChainLongerThanTheCap_IsCutWithAnEllipsis()
    {
        StructuredText text = StructuredText.FromBlocks($"# {new string('H', 1500)}\n\nBody.");

        TextChunk chunk = Chunker().Chunk(text.Text, text.Markers).Should().ContainSingle().Subject;

        chunk.Breadcrumb.Should().HaveLength(1024).And.EndWith("\u2026");
    }

    [TestMethod]
    public void Chunk_CustomBreadcrumbCap_IsHonoured()
    {
        StructuredText text = StructuredText.FromBlocks("# Alpha\n\n## Beta\n\nBody text here.");
        var options = new TextChunkingOptions { MaxChunkLength = 15, MaxSentenceLength = 15, MaxBreadcrumbLength = 8 };

        IReadOnlyList<TextChunk> chunks = Chunker().Chunk(text.Text, text.Markers, options);

        chunks.Select(chunk => chunk.Breadcrumb).Should().Equal("Alpha", "Alpha >\u2026");
    }

    [TestMethod]
    public void Chunk_BreadcrumbCutInsideAnAstralCharacter_EndsBeforeItOnACodePoint()
    {
        string heading = new string('H', 1022) + "\U0001F600tail";
        StructuredText text = StructuredText.FromBlocks($"# {heading}\n\nBody.");

        TextChunk chunk = Chunker().Chunk(text.Text, text.Markers).Should().ContainSingle().Subject;

        chunk.Breadcrumb.Should().Be(new string('H', 1022) + "\u2026");
    }

    [TestMethod]
    public void Chunk_BlankHeadingAndBlankParagraph_AreNoUnitsAndNoBreadcrumb()
    {
        const string content = "   \n\nBody.\n\n  ";
        StructureMarker[] markers =
        [
            new(StructureKind.Heading, 0, 3, 1),
            new(StructureKind.Paragraph, 5, 10),
            new(StructureKind.Paragraph, 12, 14),
        ];

        IReadOnlyList<TextChunk> chunks = Chunker().Chunk(content, markers);

        chunks.Should().Equal(new TextChunk(0, 5, 10, string.Empty, "Body."));
    }

    [TestMethod]
    public void Chunk_SameInputTwice_IsDeterministic()
    {
        StructuredText text = StructuredText.FromBlocks(string.Join("\n\n", Enumerable.Range(1, 30).Select(index => $"Row {index} of text.")));
        var options = new TextChunkingOptions { MaxChunkLength = 60, OverlapLength = 15, MaxSentenceLength = 60 };

        Chunker().Chunk(text.Text, text.Markers, options).Should().Equal(Chunker().Chunk(text.Text, text.Markers, options));
    }

    [TestMethod]
    public void Chunk_EmptyText_NoChunks()
    {
        Chunker().Chunk(string.Empty, []).Should().BeEmpty();
    }

    [TestMethod]
    public void Chunk_TextWithoutUnitMarkers_NoChunks()
    {
        const string content = "Loose text\fmore";
        StructureMarker[] markers = [new(StructureKind.PageBreak, 11, 11, 2), new(StructureKind.Table, 0, 15), new((StructureKind)99, 0, 4)];

        Chunker().Chunk(content, markers).Should().BeEmpty();
    }

    [TestMethod]
    public void Chunk_TableAndRowMarkers_AreNotUnits()
    {
        const string content = "Intro\n\na | b\nc | d\n\nOutro";
        StructureMarker[] cells =
        [
            new(StructureKind.Paragraph, 0, 5),
            new(StructureKind.TableCell, 7, 8, 1),
            new(StructureKind.TableCell, 11, 12, 2),
            new(StructureKind.TableCell, 13, 14, 1),
            new(StructureKind.TableCell, 17, 18, 2),
            new(StructureKind.Paragraph, 20, 25),
        ];
        StructureMarker[] withTable = [.. cells, new(StructureKind.Table, 7, 18), new(StructureKind.TableRow, 7, 12), new(StructureKind.TableRow, 13, 18)];
        var options = new TextChunkingOptions { MaxChunkLength = 12, OverlapLength = 4, MaxSentenceLength = 12 };

        IReadOnlyList<TextChunk> without = Chunker().Chunk(content, cells, options);
        IReadOnlyList<TextChunk> with = Chunker().Chunk(content, withTable, options);

        without.Should().HaveCountGreaterThan(1);
        with.Should().Equal(without);
    }

    [TestMethod]
    public void Chunk_MarkersOutOfOrder_ChunkAsInStartOrder()
    {
        StructuredText text = StructuredText.FromBlocks("# Top\n\nOne one.\n\n## Sub\n\nTwo two.\n\nThree three.");
        var options = new TextChunkingOptions { MaxChunkLength = 20, OverlapLength = 10, MaxSentenceLength = 20 };

        IReadOnlyList<TextChunk> ordered = Chunker().Chunk(text.Text, text.Markers, options);
        IReadOnlyList<TextChunk> reversed = Chunker().Chunk(text.Text, [.. text.Markers.Reverse()], options);

        reversed.Should().Equal(ordered);
        ordered.Select(chunk => chunk.Breadcrumb).Should().Contain("Top > Sub");
    }

    [TestMethod]
    public void Chunk_OverlappingMarkers_TheLaterOneKeepsOnlyItsPartAfterTheEarlier()
    {
        const string content = "aaaa bbbb cccc dddd";
        StructureMarker[] markers =
        [
            new(StructureKind.Paragraph, 0, 9),
            new(StructureKind.ListItem, 5, 19, 1),
            new(StructureKind.Heading, 2, 4, 1),
        ];
        var options = new TextChunkingOptions { MaxChunkLength = 9, OverlapLength = 0, MaxSentenceLength = 9 };

        IReadOnlyList<TextChunk> chunks = Chunker().Chunk(content, markers, options);

        // The nested heading is dropped (it names no breadcrumb either); the list item is clipped to [9, 19).
        chunks.Select(chunk => (chunk.Start, chunk.End, chunk.Breadcrumb)).Should().Equal((0, 9, string.Empty), (10, 19, string.Empty));
    }

    [TestMethod]
    public void Chunk_UnitWithWhitespaceAtItsEdges_IsTrimmed()
    {
        const string content = "  one two \n";

        Chunker().Chunk(content, [new StructureMarker(StructureKind.Paragraph, 0, content.Length)]).Should().Equal(new TextChunk(0, 2, 9, string.Empty, "one two"));
    }

    [TestMethod]
    public void Chunk_SameStartDifferentLength_TheLongerMarkerWins()
    {
        const string content = "aaaa bbbb";
        StructureMarker[] markers = [new(StructureKind.Paragraph, 0, 4), new(StructureKind.Paragraph, 0, 9)];

        Chunker().Chunk(content, markers).Should().Equal(new TextChunk(0, 0, 9, string.Empty, content));
    }

    [TestMethod]
    public void Chunk_ZeroLengthMarkers_AreIgnored()
    {
        const string content = "abc def";
        StructureMarker[] markers = [new(StructureKind.Paragraph, 3, 3), new(StructureKind.Heading, 0, 0, 1), new(StructureKind.Paragraph, 4, 7)];

        Chunker().Chunk(content, markers).Should().Equal(new TextChunk(0, 4, 7, string.Empty, "def"));
    }

    [TestMethod]
    public void Chunk_MarkerAtTheVeryEnd_IsInRange()
    {
        const string content = "abc";
        StructureMarker[] markers = [new(StructureKind.Paragraph, 0, 3), new(StructureKind.PageBreak, 3, 3, 2)];

        Chunker().Chunk(content, markers).Should().ContainSingle();
    }

    [TestMethod]
    [DataRow(-1, 2, DisplayName = "negative start")]
    [DataRow(2, 1, DisplayName = "end before start")]
    [DataRow(0, 4, DisplayName = "end after the text")]
    [DataRow(4, 4, DisplayName = "zero-length marker after the text")]
    public void Chunk_MarkerOutOfRange_ThrowsNamingTheMarker(int start, int end)
    {
        StructureMarker[] markers = [new(StructureKind.Paragraph, 0, 1), new(StructureKind.PageBreak, start, end)];

        Action act = () => Chunker().Chunk("abc", markers);

        act.Should().Throw<ArgumentOutOfRangeException>().WithParameterName("markers").WithMessage("*markers[1]*");
    }

    [TestMethod]
    public void Chunk_MarkerRangeInCodePointsNotUtf16_IsInRange()
    {
        // Two code points, four UTF-16 units: a marker ending at 3 lies outside.
        const string content = "\U0001F600\U0001F600";

        Chunker().Chunk(content, [new StructureMarker(StructureKind.Paragraph, 0, 2)]).Should().ContainSingle().Which.Text.Should().Be(content);
        Action act = () => Chunker().Chunk(content, [new StructureMarker(StructureKind.Paragraph, 0, 3)]);
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [TestMethod]
    public void Chunk_OverlapWouldNotReachPastThePreviousChunk_StartsAfterIt()
    {
        // Units [0,3) [4,7) [8,18): backing up to [4,7) for overlap could not reach past 7, since [4,18) exceeds the cap.
        const string content = "aaa bbb cccccccccc";
        StructureMarker[] markers = [new(StructureKind.Paragraph, 0, 3), new(StructureKind.Paragraph, 4, 7), new(StructureKind.Paragraph, 8, 18)];
        var options = new TextChunkingOptions { MaxChunkLength = 10, OverlapLength = 5, MaxSentenceLength = 10 };

        IReadOnlyList<TextChunk> chunks = Chunker().Chunk(content, markers, options);

        chunks.Select(chunk => (chunk.Start, chunk.End)).Should().Equal((0, 7), (8, 18));
    }

    [TestMethod]
    public void Chunk_OverlapAtLeastTheCap_StillAdvancesBothEnds()
    {
        string content = string.Join(" ", Enumerable.Range(0, 8).Select(_ => "abc"));
        StructureMarker[] markers = [.. Enumerable.Range(0, 8).Select(index => new StructureMarker(StructureKind.Paragraph, index * 4, (index * 4) + 3))];
        var options = new TextChunkingOptions { MaxChunkLength = 10, OverlapLength = 100, MaxSentenceLength = 10 };

        IReadOnlyList<TextChunk> chunks = Chunker().Chunk(content, markers, options);

        chunks.Select(chunk => (chunk.Start, chunk.End)).Should().Equal((0, 7), (4, 11), (8, 15), (12, 19), (16, 23), (20, 27), (24, 31));
    }

    [TestMethod]
    public void Chunk_ManyShapes_ChunksCoverEveryUnitAsTheContractSays()
    {
        TextChunkingOptions[] optionSets =
        [
            new() { MaxChunkLength = 7, OverlapLength = 0, MaxSentenceLength = 5 },
            new() { MaxChunkLength = 16, OverlapLength = 5, MaxSentenceLength = 16 },
            new() { MaxChunkLength = 30, OverlapLength = 12, MaxSentenceLength = 9 },
            new() { MaxChunkLength = 12, OverlapLength = 40, MaxSentenceLength = 12 },
        ];
        var shapes = new List<(string Text, IReadOnlyList<StructureMarker> Markers)>();
        StructuredText blocks = StructuredText.FromBlocks(
            "# Alpha\n\nShort one.\n\nA longer paragraph. It has three sentences! Does it?\n\n## Beta\n\n"
            + "nowhitespaceatallinthisstretchofletters\n\nTail \U0001F600 with an emoji. End.");
        shapes.Add((blocks.Text, blocks.Markers));
        StructuredText table = new StructuredText()
            .Unit(StructureKind.Heading, 1, "Table")
            .Table(StructuredText.Row("one two three", "four"), StructuredText.Row("five six. seven", "eight nine ten eleven"))
            .Unit(StructureKind.ListItem, 1, "An item that runs on and on. And on.");
        shapes.Add((table.Text, table.Markers));
        shapes.Add(("aaaa bbbb cccc dddd eeee", [new(StructureKind.ListItem, 5, 24, 1), new(StructureKind.Paragraph, 0, 12), new(StructureKind.Heading, 3, 7, 1)]));

        foreach (TextChunkingOptions options in optionSets)
        {
            foreach ((string content, IReadOnlyList<StructureMarker> markers) in shapes)
            {
                AssertContract(content, markers, options, Chunker().Chunk(content, markers, options));
            }
        }
    }

    [TestMethod]
    [DataRow(0, 0, 1, 1, "MaxChunkLength", DisplayName = "zero chunk length")]
    [DataRow(10, -1, 10, 1, "OverlapLength", DisplayName = "negative overlap")]
    [DataRow(10, 0, 0, 1, "MaxSentenceLength", DisplayName = "zero sentence length")]
    [DataRow(10, 0, 11, 1, "MaxSentenceLength", DisplayName = "sentence cap above the chunk cap")]
    [DataRow(10, 0, 10, 0, "MaxBreadcrumbLength", DisplayName = "zero breadcrumb length")]
    public void Chunk_InvalidOptions_AreRejected(int maxChunk, int overlap, int maxSentence, int maxBreadcrumb, string member)
    {
        var options = new TextChunkingOptions { MaxChunkLength = maxChunk, OverlapLength = overlap, MaxSentenceLength = maxSentence, MaxBreadcrumbLength = maxBreadcrumb };

        Action act = () => Chunker().Chunk("One.", [new StructureMarker(StructureKind.Paragraph, 0, 4)], options);

        act.Should().Throw<ValidationException>().Which.ValidationResult.MemberNames.Should().Equal(member);
    }

    [TestMethod]
    public void Chunk_DefaultOptions_AreValidAndDocumented()
    {
        var options = new TextChunkingOptions();

        (options.MaxChunkLength, options.OverlapLength, options.MaxSentenceLength, options.MaxBreadcrumbLength).Should().Be((2048, 300, 2048, 1024));
        Validator.TryValidateObject(options, new ValidationContext(options), null, true).Should().BeTrue();
    }

    [TestMethod]
    public void Chunk_NullArguments_Throw()
    {
        ITextChunker chunker = Chunker();

        // WHY: the null-forgiving operator is banned, tests included. A default array element is a
        // genuine null the compiler types as non-null, as a mis-wired caller would pass.
        string missingText = (new string[1])[0];
        IReadOnlyList<StructureMarker> missingMarkers = (new IReadOnlyList<StructureMarker>[1])[0];
        TextChunkingOptions missingOptions = (new TextChunkingOptions[1])[0];

        FluentActions.Invoking(() => chunker.Chunk(missingText, [])).Should().Throw<ArgumentNullException>().WithParameterName("text");
        FluentActions.Invoking(() => chunker.Chunk("a", missingMarkers)).Should().Throw<ArgumentNullException>().WithParameterName("markers");
        FluentActions.Invoking(() => chunker.Chunk("a", [], missingOptions)).Should().Throw<ArgumentNullException>().WithParameterName("options");
    }

    [TestMethod]
    public void Chunk_ConcurrentCallers_GetTheSameChunks()
    {
        ITextChunker chunker = Chunker();
        StructuredText text = StructuredText.FromBlocks(string.Join("\n\n", Enumerable.Range(1, 40).Select(index => $"# H{index}\n\nRow {index} of text. More.")));
        var options = new TextChunkingOptions { MaxChunkLength = 40, OverlapLength = 10, MaxSentenceLength = 20 };
        IReadOnlyList<TextChunk> expected = chunker.Chunk(text.Text, text.Markers, options);

        var results = new IReadOnlyList<TextChunk>[16];
        Parallel.For(0, results.Length, worker => results[worker] = chunker.Chunk(text.Text, text.Markers, options));

        results.Should().AllSatisfy(result => result.Should().Equal(expected));
    }

    [TestMethod]
    public void Module_RegistersTheChunkerOnce()
    {
        IServiceProvider services = GetServices();

        services.GetRequiredService<ITextChunker>().Should().BeSameAs(services.GetRequiredService<ITextChunker>());
    }

    private static void AssertContract(string content, IReadOnlyList<StructureMarker> markers, TextChunkingOptions options, IReadOnlyList<TextChunk> chunks)
    {
        var text = new CodePointText(content);
        string context = $"{content} / {options}";
        chunks.Select(chunk => chunk.Ordinal).Should().Equal(Enumerable.Range(0, chunks.Count), context);
        foreach (TextChunk chunk in chunks)
        {
            chunk.Text.Should().Be(text.Slice(chunk.Start, chunk.End), context);
            (chunk.End - chunk.Start).Should().BeInRange(1, options.MaxChunkLength, context);
            System.Text.Rune.IsWhiteSpace(text[chunk.Start]).Should().BeFalse(context);
            System.Text.Rune.IsWhiteSpace(text[chunk.End - 1]).Should().BeFalse(context);
        }

        for (int index = 1; index < chunks.Count; index++)
        {
            chunks[index].Start.Should().BeGreaterThan(chunks[index - 1].Start, context);
            chunks[index].End.Should().BeGreaterThan(chunks[index - 1].End, context);
        }

        // Every non-whitespace code point of a unit lies in a chunk.
        foreach (StructureMarker marker in markers.Where(marker => marker.Kind is StructureKind.Heading or StructureKind.Paragraph or StructureKind.ListItem or StructureKind.TableCell))
        {
            for (int at = marker.Start; at < marker.End; at++)
            {
                if (!System.Text.Rune.IsWhiteSpace(text[at]))
                {
                    chunks.Should().Contain(chunk => chunk.Start <= at && at < chunk.End, $"{context} code point {at}");
                }
            }
        }
    }

    private ITextChunker Chunker() => GetServices().GetRequiredService<ITextChunker>();
}
