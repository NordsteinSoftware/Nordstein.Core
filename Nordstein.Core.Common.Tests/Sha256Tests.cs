using AwesomeAssertions;
using Nordstein.Core.Common.Security;

namespace Nordstein.Core.Common.Tests;

[TestClass]
public sealed class Sha256Tests
{
    [TestMethod]
    public void HexHash_WithKnownInput_ReturnsKnownVector()
    {
        // The canonical SHA-256 test vector for "abc" (FIPS 180-4), upper-cased hex.
        string hash = Sha256.HexHash("abc");

        hash.Should().Be("BA7816BF8F01CFEA414140DE5DAE2223B00361A396177A9CB410FF61F20015AD");
    }

    [TestMethod]
    public void HexHash_WithEmptyString_ReturnsKnownVector()
    {
        string hash = Sha256.HexHash(string.Empty);

        hash.Should().Be("E3B0C44298FC1C149AFBF4C8996FB92427AE41E4649B934CA495991B7852B855");
    }

    [TestMethod]
    public void HexHash_IsDeterministic()
    {
        Sha256.HexHash("nordstein").Should().Be(Sha256.HexHash("nordstein"));
    }

    [TestMethod]
    public void HexHash_WithDifferentInputs_ProducesDifferentHashes()
    {
        Sha256.HexHash("alpha").Should().NotBe(Sha256.HexHash("beta"));
    }

    [TestMethod]
    public void HexHash_ReturnsUpperCaseHexOf64Characters()
    {
        string hash = Sha256.HexHash("some content");

        hash.Should().HaveLength(64);
        hash.Should().MatchRegex("^[0-9A-F]{64}$");
    }
}
