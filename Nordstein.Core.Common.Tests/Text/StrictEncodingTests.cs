using System.Text;
using AwesomeAssertions;
using Nordstein.Core.Common.Text;

namespace Nordstein.Core.Common.Tests.Text;

/// <summary>
/// The shared strict UTF-8 encoding: no byte-order mark, invalid bytes and lone surrogates throw,
/// and the one instance cannot be changed.
/// </summary>
[TestClass]
public sealed class StrictEncodingTests
{
    [TestMethod]
    public void Utf8_IsOneInstance()
    {
        StrictEncoding.Utf8.Should().BeSameAs(StrictEncoding.Utf8);
    }

    [TestMethod]
    public void Utf8_RoundTripsWellFormedText()
    {
        const string text = "a\u00E4\u20AC\U0001F600";

        byte[] bytes = StrictEncoding.Utf8.GetBytes(text);

        bytes.Should().Equal(Encoding.UTF8.GetBytes(text));
        StrictEncoding.Utf8.GetString(bytes).Should().Be(text);
    }

    [TestMethod]
    public void Utf8_WritesNoByteOrderMark()
    {
        StrictEncoding.Utf8.GetPreamble().Should().BeEmpty();

        using var stream = new MemoryStream();
        using (var writer = new StreamWriter(stream, StrictEncoding.Utf8, leaveOpen: true))
        {
            writer.Write("a");
        }

        stream.ToArray().Should().Equal((byte)'a');
    }

    [TestMethod]
    [DataRow(new byte[] { 0xC3 }, DisplayName = "truncated sequence")]
    [DataRow(new byte[] { 0xFF }, DisplayName = "never valid")]
    [DataRow(new byte[] { 0xC0, 0xAF }, DisplayName = "overlong")]
    [DataRow(new byte[] { 0xED, 0xA0, 0x80 }, DisplayName = "encoded surrogate")]
    public void Utf8_InvalidBytes_ThrowInsteadOfBecomingReplacementCharacters(byte[] bytes)
    {
        Action act = () => StrictEncoding.Utf8.GetString(bytes);

        act.Should().Throw<DecoderFallbackException>();
    }

    [TestMethod]
    public void Utf8_LoneSurrogate_ThrowsInsteadOfBecomingAReplacementCharacter()
    {
        string text = "a\uD800";

        Action act = () => StrictEncoding.Utf8.GetBytes(text);

        act.Should().Throw<EncoderFallbackException>();
    }

    [TestMethod]
    public void Utf8_IsReadOnly_SoNoCallerCanLoosenIt()
    {
        StrictEncoding.Utf8.IsReadOnly.Should().BeTrue();

        Action loosenEncoder = () => StrictEncoding.Utf8.EncoderFallback = EncoderFallback.ReplacementFallback;
        Action loosenDecoder = () => StrictEncoding.Utf8.DecoderFallback = DecoderFallback.ReplacementFallback;

        loosenEncoder.Should().Throw<InvalidOperationException>();
        loosenDecoder.Should().Throw<InvalidOperationException>();
    }

    [TestMethod]
    public void Utf8_Clone_IsAWritableCopyThatLeavesTheSharedOneStrict()
    {
        var copy = (Encoding)StrictEncoding.Utf8.Clone();
        copy.DecoderFallback = new DecoderReplacementFallback("\uFFFD");

        copy.GetString([0xFF]).Should().Be("\uFFFD");
        FluentActions.Invoking(() => StrictEncoding.Utf8.GetString([0xFF])).Should().Throw<DecoderFallbackException>();
    }
}
