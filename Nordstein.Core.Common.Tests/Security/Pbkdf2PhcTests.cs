using AwesomeAssertions;
using Nordstein.Core.Common.Security.Internal;

namespace Nordstein.Core.Common.Tests.Security;

/// <summary>
/// The PHC parser's acceptance boundaries, tested without a derivation: proving the upper
/// iteration bound through <c>Verify</c> would cost ten million PBKDF2 rounds.
/// </summary>
[TestClass]
public sealed class Pbkdf2PhcTests
{
    private const string Salt16 = "AAECAwQFBgcICQoLDA0ODw";
    private const string Hash32 = "3NkHSPhtBEJ7V71tycgH0BEWOfIITqysFEFL5iBfsgk";

    [TestMethod]
    public void Pbkdf2Phc_Parse_MaxIterations_Accepted()
    {
        Pbkdf2PhcParts parts = Pbkdf2Phc.Parse("$pbkdf2-sha256$i=10000000$" + Salt16 + "$" + Hash32);

        parts.Iterations.Should().Be(10_000_000);
        parts.Salt.ToArray().Should().Equal(Enumerable.Range(0, 16).Select(i => (byte)i));
        parts.Hash.ToArray().Should().HaveCount(32);
    }

    [TestMethod]
    public void Pbkdf2Phc_Parse_MaxIterationsPlusOne_Throws()
    {
        Action act = () => Pbkdf2Phc.Parse("$pbkdf2-sha256$i=10000001$" + Salt16 + "$" + Hash32);

        act.Should().Throw<FormatException>();
    }

    [TestMethod]
    public void Pbkdf2Phc_FormatThenParse_RoundTrips()
    {
        byte[] salt = Enumerable.Range(0, 16).Select(i => (byte)i).ToArray();
        byte[] hash = Enumerable.Range(100, 32).Select(i => (byte)i).ToArray();

        string encoded = Pbkdf2Phc.Format(600_000, salt, hash);
        Pbkdf2PhcParts parts = Pbkdf2Phc.Parse(encoded);

        encoded.Should().Be("$pbkdf2-sha256$i=600000$" + Salt16 + "$ZGVmZ2hpamtsbW5vcHFyc3R1dnd4eXp7fH1+f4CBgoM");
        parts.Iterations.Should().Be(600_000);
        parts.Salt.ToArray().Should().Equal(salt);
        parts.Hash.ToArray().Should().Equal(hash);
    }
}
