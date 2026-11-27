using AwesomeAssertions;
using Nordstein.Core.Common.Text;

namespace Nordstein.Core.Common.Tests.Text;

/// <summary>
/// Storable text: at most the given UTF-16 length, no U+0000, no lone surrogate; refusals name the
/// parameter and never quote the text.
/// </summary>
[TestClass]
public sealed class StorableTextTests
{
    [TestMethod]
    [DataRow("", 0)]
    [DataRow("plain", 5)]
    [DataRow("\u00E4\u00F6\u00FC \u20AC", 10)]
    public void RequireStorable_StorableText_ReturnsIt(string text, int maxLength)
    {
        text.RequireStorable(maxLength).Should().BeSameAs(text);
    }

    [TestMethod]
    public void RequireStorable_SurrogatePairs_AreStorable()
    {
        const string text = "\U0001F600x\U0001F680";

        text.RequireStorable(text.Length).Should().Be(text);
    }

    [TestMethod]
    public void RequireStorable_LongerThanTheLimit_ThrowsWithTheLengthsOnly()
    {
        const string secret = "secret-value";

        Action act = () => secret.RequireStorable(5);

        act.Should().Throw<ArgumentException>().WithParameterName("secret")
            .Which.Message.Should().Contain("5").And.Contain("12").And.NotContain(secret);
    }

    [TestMethod]
    public void RequireStorable_UnderAnotherCulture_WritesTheLengthsInvariantly()
    {
        System.Globalization.CultureInfo original = System.Globalization.CultureInfo.CurrentCulture;
        try
        {
            System.Globalization.CultureInfo.CurrentCulture = new System.Globalization.CultureInfo("de-AT") { NumberFormat = { PositiveSign = "plus" } };
            string text = new('x', 1200);

            Action act = () => text.RequireStorable(1000);

            act.Should().Throw<ArgumentException>().Which.Message.Should().StartWith("At most 1000 characters are stored; this holds 1200.");
        }
        finally
        {
            System.Globalization.CultureInfo.CurrentCulture = original;
        }
    }

    [TestMethod]
    public void RequireStorable_LengthCountsUtf16Units()
    {
        // One code point, two UTF-16 units.
        Action act = () => "\U0001F600".RequireStorable(1);

        act.Should().Throw<ArgumentException>();
    }

    [TestMethod]
    public void RequireStorable_Nul_Throws()
    {
        string text = "pass\u0000word";

        Action act = () => text.RequireStorable(100);

        act.Should().Throw<ArgumentException>().WithParameterName("text")
            .Which.Message.Should().Contain("U+0000").And.NotContain("pass");
    }

    [TestMethod]
    public void RequireStorable_LoneSurrogates_Throw()
    {
        // Built in code, not as [DataRow] constants: attribute metadata cannot carry a lone surrogate.
        string[] texts = ["a\uD800b", "a\uDC00b", "a\uD83D", "\uDC00\uD800", "\uDE00"];

        foreach (string text in texts)
        {
            Action act = () => text.RequireStorable(100, "field");

            act.Should().Throw<ArgumentException>().WithParameterName("field").Which.Message.Should().Contain("surrogate");
        }
    }

    [TestMethod]
    public void RequireStorable_NegativeLimit_Throws()
    {
        Action act = () => "a".RequireStorable(-1);

        act.Should().Throw<ArgumentOutOfRangeException>().WithParameterName("maxLength");
    }

    [TestMethod]
    public void RequireStorable_Null_Throws()
    {
        // WHY: the null-forgiving operator is banned, tests included. A default array element is a
        // genuine null the compiler types as non-null, as a mis-wired caller would pass.
        string missing = (new string[1])[0];

        Action act = () => missing.RequireStorable(1);

        act.Should().Throw<ArgumentNullException>().WithParameterName("missing");
    }
}
