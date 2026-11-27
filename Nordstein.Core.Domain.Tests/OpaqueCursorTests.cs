using System.Buffers.Text;
using System.Globalization;
using System.Text;
using AwesomeAssertions;
using Nordstein.Core.Domain.Paging;

namespace Nordstein.Core.Domain.Tests;

[TestClass]
public sealed class OpaqueCursorTests
{
    private static readonly SamplePosition Sample = new(
        42,
        1234.5678m,
        new DateOnly(2026, 3, 1),
        new DateTimeOffset(2026, 3, 1, 13, 45, 30, 123, TimeSpan.FromHours(2)),
        Guid.Parse("8f14e45f-ceea-467f-a8b0-3d6c2b6e1a9d"),
        "Grüße & <tags>",
        null,
        [new SampleItem("first", 1), new SampleItem("second", long.MaxValue)]);

    [TestMethod]
    public void MaxLength_Is4096()
    {
        OpaqueCursor.MaxLength.Should().Be(4096);
    }

    [TestMethod]
    public void EncodeThenDecode_RoundTripsEveryMemberType()
    {
        string cursor = OpaqueCursor.Encode(Sample);

        SamplePosition decoded = OpaqueCursor.Decode<SamplePosition>(cursor);

        decoded.Number.Should().Be(Sample.Number);
        decoded.Amount.Should().Be(Sample.Amount);
        decoded.Day.Should().Be(Sample.Day);
        decoded.At.Should().Be(Sample.At);
        decoded.At.Offset.Should().Be(Sample.At.Offset);
        decoded.Id.Should().Be(Sample.Id);
        decoded.Name.Should().Be(Sample.Name);
        decoded.Note.Should().BeNull();
        decoded.Items.Should().Equal(Sample.Items);
    }

    [TestMethod]
    public void EncodeThenDecode_WithNullableMemberSet_RoundTripsIt()
    {
        SamplePosition position = Sample with { Note = "set" };

        SamplePosition decoded = OpaqueCursor.Decode<SamplePosition>(OpaqueCursor.Encode(position));

        decoded.Note.Should().Be("set");
    }

    [TestMethod]
    public void EncodeThenDecode_RoundTripsAPrimitivePosition()
    {
        OpaqueCursor.Decode<int>(OpaqueCursor.Encode(17)).Should().Be(17);
    }

    [TestMethod]
    public void Encode_WritesUnpaddedBase64UrlOfUtf8Json()
    {
        string cursor = OpaqueCursor.Encode(new SingleName("x"));

        cursor.Should().MatchRegex("^[A-Za-z0-9_-]+$");
        Encoding.UTF8.GetString(Base64Url.DecodeFromChars(cursor)).Should().Be("""{"Name":"x"}""");
    }

    [TestMethod]
    public void Encode_DoesNotDependOnTheCurrentCulture()
    {
        CultureInfo original = CultureInfo.CurrentCulture;
        string invariant;
        string austrian;
        SamplePosition decoded;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
            invariant = OpaqueCursor.Encode(Sample);
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("de-AT");
            austrian = OpaqueCursor.Encode(Sample);
            decoded = OpaqueCursor.Decode<SamplePosition>(invariant);
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }

        austrian.Should().Be(invariant);
        decoded.Amount.Should().Be(Sample.Amount);
        decoded.At.Should().Be(Sample.At);
    }

    [TestMethod]
    public void Encode_ForNull_ThrowsArgumentNullException()
    {
        Action act = () => OpaqueCursor.Encode<SingleName?>(null);

        act.Should().Throw<ArgumentNullException>().WithParameterName("position");
    }

    [TestMethod]
    public void Encode_ForPositionAtMaxLength_Succeeds_AndDecodes()
    {
        // {"Name":"…"} is 11 bytes of JSON plus the name; 3072 bytes encode to 4096 characters.
        var position = new SingleName(new string('a', 3061));

        string cursor = OpaqueCursor.Encode(position);

        cursor.Length.Should().Be(OpaqueCursor.MaxLength);
        OpaqueCursor.Decode<SingleName>(cursor).Should().Be(position);
    }

    [TestMethod]
    public void Encode_ForPositionLongerThanMaxLength_ThrowsArgumentException()
    {
        var position = new SingleName(new string('a', 3062));

        Action act = () => OpaqueCursor.Encode(position);

        act.Should().Throw<ArgumentException>().WithParameterName("position");
    }

    [TestMethod]
    public void Decode_ForNull_Throws()
    {
        ShouldReject(() => OpaqueCursor.Decode<SingleName>(null));
    }

    [TestMethod]
    public void Decode_ForEmpty_Throws()
    {
        ShouldReject(() => OpaqueCursor.Decode<SingleName>(""));
    }

    [TestMethod]
    public void Decode_ForNonBase64Url_Throws()
    {
        ShouldReject(() => OpaqueCursor.Decode<SingleName>("not base64url!"));
    }

    [TestMethod]
    public void Decode_ForStandardBase64Alphabet_Throws()
    {
        // "?>?" is 3F 3E 3F: base64 "Pz4/", base64url "Pz4_".
        string standard = Convert.ToBase64String("?>?"u8);
        standard.Should().Contain("/");

        ShouldReject(() => OpaqueCursor.Decode<string>(standard));
    }

    [TestMethod]
    public void Decode_ForPaddedInput_Throws()
    {
        string unpadded = OpaqueCursor.Encode(new SingleName("ab"));
        unpadded.Length.Should().NotBe(unpadded.Length / 4 * 4, "the sample must need padding");
        string padded = unpadded + new string('=', 4 - (unpadded.Length % 4));

        ShouldReject(() => OpaqueCursor.Decode<SingleName>(padded));
    }

    [TestMethod]
    public void Decode_ForEmbeddedWhitespace_Throws()
    {
        string cursor = OpaqueCursor.Encode(new SingleName("abc"));
        string spaced = cursor[..4] + " " + cursor[4..];

        ShouldReject(() => OpaqueCursor.Decode<SingleName>(spaced));
    }

    [TestMethod]
    public void Decode_ForNonJson_Throws()
    {
        ShouldReject(() => OpaqueCursor.Decode<SingleName>(Raw("42|abc")));
    }

    [TestMethod]
    public void Decode_ForInvalidUtf8_Throws()
    {
        ShouldReject(() => OpaqueCursor.Decode<SingleName>(Base64Url.EncodeToString([0x7B, 0xFF, 0x7D])));
    }

    [TestMethod]
    public void Decode_ForJsonNullLiteral_Throws()
    {
        ShouldReject(() => OpaqueCursor.Decode<SingleName>(Raw("null")));
    }

    [TestMethod]
    public void Decode_ForTrailingData_Throws()
    {
        ShouldReject(() => OpaqueCursor.Decode<SingleName>(Raw("""{"Name":"x"}{"Name":"y"}""")));
    }

    [TestMethod]
    public void Decode_ForTrailingComma_Throws()
    {
        ShouldReject(() => OpaqueCursor.Decode<SingleName>(Raw("""{"Name":"x",}""")));
    }

    [TestMethod]
    public void Decode_ForComment_Throws()
    {
        ShouldReject(() => OpaqueCursor.Decode<SingleName>(Raw("""{"Name":"x"}/* c */""")));
    }

    [TestMethod]
    public void Decode_ForMissingRequiredMember_Throws()
    {
        ShouldReject(() => OpaqueCursor.Decode<TwoMembers>(Raw("""{"Name":"x"}""")));
    }

    [TestMethod]
    public void Decode_ForUnknownMember_Throws()
    {
        ShouldReject(() => OpaqueCursor.Decode<SingleName>(Raw("""{"Name":"x","Extra":1}""")));
    }

    [TestMethod]
    public void Decode_ForUnknownMemberInNestedObject_Throws()
    {
        string json = Encoding.UTF8.GetString(Base64Url.DecodeFromChars(OpaqueCursor.Encode(Sample)))
            .Replace("\"Key\":\"first\"", "\"Key\":\"first\",\"Extra\":true", StringComparison.Ordinal);
        json.Should().Contain("Extra");

        ShouldReject(() => OpaqueCursor.Decode<SamplePosition>(Raw(json)));
    }

    [TestMethod]
    public void Decode_ForDuplicateMember_Throws()
    {
        ShouldReject(() => OpaqueCursor.Decode<SingleName>(Raw("""{"Name":"x","Name":"y"}""")));
    }

    [TestMethod]
    public void Decode_ForMemberNameInOtherCase_Throws()
    {
        ShouldReject(() => OpaqueCursor.Decode<SingleName>(Raw("""{"name":"x"}""")));
    }

    [TestMethod]
    public void Decode_ForWrongType_Throws()
    {
        ShouldReject(() => OpaqueCursor.Decode<TwoMembers>(Raw("""{"Name":"x","Count":"1"}""")));
    }

    [TestMethod]
    public void Decode_ForMalformedGuid_Throws()
    {
        string json = Encoding.UTF8.GetString(Base64Url.DecodeFromChars(OpaqueCursor.Encode(Sample)))
            .Replace(Sample.Id.ToString(), "not-a-guid", StringComparison.Ordinal);
        json.Should().Contain("not-a-guid");

        ShouldReject(() => OpaqueCursor.Decode<SamplePosition>(Raw(json)));
    }

    [TestMethod]
    public void Decode_ForNullInNonNullableMember_Throws()
    {
        ShouldReject(() => OpaqueCursor.Decode<TwoMembers>(Raw("""{"Name":null,"Count":1}""")));
    }

    [TestMethod]
    public void Decode_ForNullInNullableMember_Succeeds()
    {
        OpaqueCursor.Decode<OptionalNote>(Raw("""{"Note":null}""")).Should().Be(new OptionalNote(null));
    }

    [TestMethod]
    public void Decode_ForInputLongerThanMaxLength_Throws()
    {
        // A well-formed cursor one name character past the limit: only the length is wrong.
        string tooLong = Raw($$"""{"Name":"{{new string('a', 3062)}}"}""");
        tooLong.Length.Should().BeGreaterThan(OpaqueCursor.MaxLength);

        ShouldReject(() => OpaqueCursor.Decode<SingleName>(tooLong));
    }

    [TestMethod]
    public void Decode_ForNumberOutOfRange_Throws()
    {
        ShouldReject(() => OpaqueCursor.Decode<int>(Raw("4294967296")));
    }

    private static string Raw(string json) => Base64Url.EncodeToString(Encoding.UTF8.GetBytes(json));

    private static void ShouldReject(Action decode)
    {
        decode.Should().Throw<ArgumentException>()
            .Where(e => e.GetType() == typeof(ArgumentException), "no subclass such as ArgumentNullException")
            .WithParameterName("cursor");
    }
}

internal sealed record SampleItem(string Key, long Value);

internal sealed record SamplePosition(
    int Number,
    decimal Amount,
    DateOnly Day,
    DateTimeOffset At,
    Guid Id,
    string Name,
    string? Note,
    IReadOnlyList<SampleItem> Items);

internal sealed record SingleName(string Name);

internal sealed record TwoMembers(string Name, int Count);

internal sealed record OptionalNote(string? Note);
