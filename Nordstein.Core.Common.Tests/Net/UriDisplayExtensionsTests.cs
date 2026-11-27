using AwesomeAssertions;
using Nordstein.Core.Common.Net;

namespace Nordstein.Core.Common.Tests.Net;

[TestClass]
public sealed class UriDisplayExtensionsTests
{
    [TestMethod]
    [DataRow("https://u:p@h:8443/v1?k=s#f", "https://h:8443/v1")]
    [DataRow("https://user@h/v1/models?api_key=secret", "https://h/v1/models")]
    [DataRow("http://h:80/a/b#frag", "http://h/a/b")]
    [DataRow("https://h", "https://h/")]
    [DataRow("http://127.0.0.1:11434/v1", "http://127.0.0.1:11434/v1")]
    [DataRow("https://user:pw@[2001:db8::1]/v1#frag", "https://[2001:db8::1]/v1")]
    [DataRow("http://[::1]:8080/v1?x=1", "http://[::1]:8080/v1")]
    [DataRow("file:///tmp/a/b.txt?q#f", "file:///tmp/a/b.txt")]
    [DataRow("mailto:someone@example.com?subject=x", "mailto:example.com")]
    public void UriDisplayExtensions_UserInfoQueryFragment_Dropped(string input, string expected)
        => new Uri(input).ToDisplayString().Should().Be(expected);

    [TestMethod]
    [DataRow("data:text/plain;base64,SGVsbG8=")]
    [DataRow("urn:isbn:0451450523")]
    public void UriDisplayExtensions_UriWithoutAuthority_KeepsItsPath(string input)
        => new Uri(input).ToDisplayString().Should().Be(input);

    [TestMethod]
    [DataRow("v1/models")]
    [DataRow("/v1/models?api_key=secret")]
    public void UriDisplayExtensions_Relative_SaysRelative(string input)
        => new Uri(input, UriKind.Relative).ToDisplayString().Should().Be("(relative)");

    [TestMethod]
    public void UriDisplayExtensions_Null_ThrowsArgumentNull()
    {
        Uri missing = (new Uri[1])[0];

        Action display = () => missing.ToDisplayString();

        display.Should().Throw<ArgumentNullException>().WithParameterName("uri");
    }
}
