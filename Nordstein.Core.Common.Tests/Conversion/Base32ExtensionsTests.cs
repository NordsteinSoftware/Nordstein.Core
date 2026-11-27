using AwesomeAssertions;
using Nordstein.Core.Common.Conversion;
using System.Text;

namespace Nordstein.Core.Common.Tests.Conversion;

[TestClass]
public sealed class Base32ExtensionsTests
{
    // RFC 4648 section 10, lowercased and without padding.
    [TestMethod]
    [DataRow("", "")]
    [DataRow("f", "my")]
    [DataRow("fo", "mzxq")]
    [DataRow("foo", "mzxw6")]
    [DataRow("foob", "mzxw6yq")]
    [DataRow("fooba", "mzxw6ytb")]
    [DataRow("foobar", "mzxw6ytboi")]
    public void ToBase32_RfcVectors_LowercaseUnpadded(string input, string expected)
    {
        ReadOnlySpan<byte> bytes = Encoding.ASCII.GetBytes(input);

        bytes.ToBase32().Should().Be(expected);
    }

    [TestMethod]
    public void ToBase32_ThirtyTwoBytes_IsFiftyTwoCharactersOfTheAlphabet()
    {
        byte[] bytes = [.. Enumerable.Range(0, 32).Select(i => (byte)(i * 37))];

        string encoded = ((ReadOnlySpan<byte>)bytes).ToBase32();

        encoded.Should().HaveLength(52);
        encoded.ToCharArray().Should().OnlyContain(character => character.IsBase32());
    }

    [TestMethod]
    public void ToBase32_AllBitsSetAndClear_MapToTheAlphabetEnds()
    {
        ((ReadOnlySpan<byte>)new byte[] { 0, 0, 0, 0, 0 }).ToBase32().Should().Be("aaaaaaaa");
        ((ReadOnlySpan<byte>)new byte[] { 255, 255, 255, 255, 255 }).ToBase32().Should().Be("77777777");
    }

    [TestMethod]
    public void IsBase32_AlphabetOnly()
    {
        string alphabet = "abcdefghijklmnopqrstuvwxyz234567";

        alphabet.ToCharArray().Should().OnlyContain(character => character.IsBase32());
        "ABZ0189=_ é".ToCharArray().Should().OnlyContain(character => !character.IsBase32());
    }
}
