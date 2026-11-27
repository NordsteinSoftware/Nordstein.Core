using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Nordstein.Core.Common.Security;
using Nordstein.Core.Testing;

namespace Nordstein.Core.Common.Tests.Security;

[TestClass]
public sealed class SecretTokenFormatTests : BaseTest<Module>
{
    private const string ApiPrefix = "acme_api_";
    private const string SessionPrefix = "acme_ses_";
    private const string FiftyTwoA = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";

    private ISecretTokenFormat Format => GetServices().GetRequiredService<ISecretTokenFormat>();

    [TestMethod]
    public void Compose_ThirtyTwoBytes_IsThePrefixAndFiftyTwoBase32Characters()
    {
        byte[] secret = [.. Enumerable.Range(0, 32).Select(i => (byte)(i * 7))];

        string token = Format.Compose(ApiPrefix, secret);

        token.Should().HaveLength(ApiPrefix.Length + ISecretTokenFormat.SecretLength).And.StartWith(ApiPrefix);
        token[ApiPrefix.Length..].Should().MatchRegex("^[a-z2-7]{52}$");
        Format.Matches(ApiPrefix, token).Should().BeTrue();
    }

    [TestMethod]
    public void Compose_KnownSecrets_AreTheRfc4648Vectors()
    {
        ISecretTokenFormat format = Format;

        format.Compose(ApiPrefix, new byte[32]).Should().Be(ApiPrefix + FiftyTwoA);
        // 0xFF x 32 is 256 one bits: 51 x "7", then the last bit padded to 10000 = 16, "q".
        format.Compose(SessionPrefix, Enumerable.Repeat((byte)0xFF, 32).ToArray())
            .Should().Be(SessionPrefix + new string('7', 51) + "q");
    }

    [TestMethod]
    [DataRow(0)]
    [DataRow(16)]
    [DataRow(31)]
    [DataRow(33)]
    public void Compose_SecretNotThirtyTwoBytes_Throws(int length)
    {
        ISecretTokenFormat format = Format;

        Action act = () => format.Compose(ApiPrefix, new byte[length]);

        act.Should().Throw<ArgumentException>().Which.ParamName.Should().Be("secret");
    }

    [TestMethod]
    public void Generate_EachCall_DrawsAFreshWellFormedToken()
    {
        ISecretTokenFormat format = Format;

        string first = format.Generate(ApiPrefix);
        string second = format.Generate(ApiPrefix);

        first.Should().NotBe(second);
        format.Matches(ApiPrefix, first).Should().BeTrue();
        format.Matches(ApiPrefix, second).Should().BeTrue();
        format.Matches(SessionPrefix, first).Should().BeFalse();
    }

    [TestMethod]
    public void Generate_ManyTokens_UseTheWholeAlphabet()
    {
        ISecretTokenFormat format = Format;

        HashSet<char> seen = [.. Enumerable.Range(0, 200).SelectMany(_ => format.Generate(ApiPrefix)[ApiPrefix.Length..^1])];

        seen.Should().HaveCount(32, "51 free characters of 200 tokens draw every base32 character");
    }

    [TestMethod]
    [DataRow("a_")]
    [DataRow("z9_")]
    [DataRow("acme_api_")]
    [DataRow("a___")]
    [DataRow("abcdefghijklmnopqrstuvwxyz01234_")]
    public void Generate_ValidPrefix_IsAccepted(string prefix)
    {
        ISecretTokenFormat format = Format;

        string token = format.Generate(prefix);

        token.Should().StartWith(prefix).And.HaveLength(prefix.Length + 52);
        format.Matches(prefix, token).Should().BeTrue();
    }

    [TestMethod]
    [DataRow("")]
    [DataRow("_")]
    [DataRow("a")]
    [DataRow("ab")]
    [DataRow("_a_")]
    [DataRow("1a_")]
    [DataRow("A_")]
    [DataRow("aB_")]
    [DataRow("a-b_")]
    [DataRow("a b_")]
    [DataRow("ä_")]
    [DataRow("a\u0000_")]
    [DataRow("abcdefghijklmnopqrstuvwxyz012345_")]
    public void EveryMember_InvalidPrefix_Throws(string prefix)
    {
        ISecretTokenFormat format = Format;
        Action[] acts =
        [
            () => format.Generate(prefix),
            () => format.Compose(prefix, new byte[32]),
            () => format.Matches(prefix, prefix + FiftyTwoA),
        ];

        foreach (Action act in acts)
        {
            act.Should().Throw<ArgumentException>().Which.ParamName.Should().Be("prefix");
        }
    }

    [TestMethod]
    public void EveryMember_NullPrefix_ThrowsArgumentNull()
    {
        ISecretTokenFormat format = Format;
        string missing = (new string[1])[0];
        Action[] acts =
        [
            () => format.Generate(missing),
            () => format.Compose(missing, new byte[32]),
            () => format.Matches(missing, ApiPrefix + FiftyTwoA),
        ];

        foreach (Action act in acts)
        {
            act.Should().Throw<ArgumentNullException>().Which.ParamName.Should().Be("prefix");
        }
    }

    [TestMethod]
    [DataRow(null)]
    [DataRow("")]
    [DataRow("acme_api_")]
    [DataRow("acme_api_AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")] // upper case
    [DataRow("acme_api_aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa1aaaa")] // '1' is not base32
    [DataRow("acme_xyz_aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")] // another prefix
    [DataRow("acme_ses_aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")] // the other kind
    [DataRow("acme_api_aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")] // 51 characters
    [DataRow("acme_api_aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")] // 53 characters
    [DataRow("acme_api_aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaab")] // last character not a or q
    [DataRow("acme_api_aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa7")] // last character not a or q
    [DataRow("ACME_API_aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")] // prefix in upper case
    [DataRow(" acme_api_aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")] // leading space
    [DataRow("acme_api_aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa ")] // trailing space
    public void Matches_MalformedCandidate_IsFalse(string? candidate)
        => Format.Matches(ApiPrefix, candidate).Should().BeFalse();

    [TestMethod]
    [DataRow('a')]
    [DataRow('q')]
    public void Matches_CanonicalLastCharacter_IsTrue(char last)
        => Format.Matches(ApiPrefix, ApiPrefix + FiftyTwoA[..^1] + last).Should().BeTrue();

    [TestMethod]
    public void Matches_TokenOfAPrefixThatStartsTheSame_IsFalseBothWays()
    {
        ISecretTokenFormat format = Format;
        string shortToken = format.Generate("acme_");
        string longToken = format.Generate("acme_api_");

        format.Matches("acme_api_", shortToken).Should().BeFalse();
        format.Matches("acme_", longToken).Should().BeFalse();
    }

    [TestMethod]
    public void Last4Of_Token_IsItsLastFourCharacters()
        => Format.Last4Of(ApiPrefix + new string('a', 48) + "wxyz").Should().Be("wxyz");

    [TestMethod]
    [DataRow("a")]
    [DataRow("wxyz")]
    public void Last4Of_FourOrFewerCharacters_IsTheWholeValue(string token)
        => Format.Last4Of(token).Should().Be(token);

    [TestMethod]
    public void Last4Of_Empty_Throws()
    {
        ISecretTokenFormat format = Format;

        Action act = () => format.Last4Of(string.Empty);

        act.Should().Throw<ArgumentException>().Which.ParamName.Should().Be("token");
    }

    [TestMethod]
    public void Last4Of_Null_ThrowsArgumentNull()
    {
        ISecretTokenFormat format = Format;

        string missing = (new string[1])[0];

        Action act = () => format.Last4Of(missing);

        act.Should().Throw<ArgumentNullException>().Which.ParamName.Should().Be("token");
    }

    [TestMethod]
    public void Constants_AreThePublishedSizes()
    {
        ISecretTokenFormat.SecretBytes.Should().Be(32);
        ISecretTokenFormat.SecretLength.Should().Be(52);
        ISecretTokenFormat.MaxPrefixLength.Should().Be(32);
    }

    [TestMethod]
    public void Container_ResolvesOneInstance()
    {
        IServiceProvider services = GetServices();

        services.GetRequiredService<ISecretTokenFormat>().Should().BeSameAs(services.GetRequiredService<ISecretTokenFormat>());
    }
}
