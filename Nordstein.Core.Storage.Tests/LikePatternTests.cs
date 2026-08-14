using AwesomeAssertions;

namespace Nordstein.Core.Storage.Tests;

/// <summary>
/// The infix-search pattern builder lowers the term (callers lower the column too) and escapes the
/// LIKE wildcards so user-typed <c>%</c>/<c>_</c> match literally.
/// </summary>
[TestClass]
public sealed class LikePatternTests
{
    [TestMethod]
    public void Contains_WrapsLowercasedTermInWildcards()
    {
        LikePattern.Contains("  MixedCase  ").Should().Be("%mixedcase%");
    }

    [TestMethod]
    public void Contains_EscapesLikeWildcards()
    {
        LikePattern.Contains("100%").Should().Be("%100\\%%");
        LikePattern.Contains("a_b").Should().Be("%a\\_b%");
    }

    [TestMethod]
    public void Contains_EscapesTheEscapeCharacterFirst()
    {
        // The backslash the escaping introduces must not itself be re-escaped: the literal
        // backslash the user typed becomes "\\", and the wildcards keep a single escape.
        LikePattern.Contains("a\\%").Should().Be("%a\\\\\\%%");
    }
}
