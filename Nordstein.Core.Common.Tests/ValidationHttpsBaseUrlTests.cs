using System.ComponentModel.DataAnnotations;
using AwesomeAssertions;
using Check = Nordstein.Core.Common.Validation.Validation;

namespace Nordstein.Core.Common.Tests;

/// <summary>
/// <see cref="Check.HttpsBaseUrl"/>: an absolute https URL without user information, query or
/// fragment passes; anything else fails naming the member, never the URL.
/// </summary>
[TestClass]
public sealed class ValidationHttpsBaseUrlTests
{
    [TestMethod]
    [DataRow("https://api.example.org")]
    [DataRow("https://api.example.org/")]
    [DataRow("https://api.example.org/v1/rest-api")]
    [DataRow("HTTPS://API.EXAMPLE.ORG:8443/v1")]
    [DataRow("https://[::1]/v1")]
    [DataRow("https://xn--bcher-kva.example/")]
    public void HttpsBaseUrl_AbsoluteHttpsWithoutExtras_Passes(string url)
    {
        Check.HttpsBaseUrl(new Uri(url)).Should().Be(ValidationResult.Success);
    }

    [TestMethod]
    [DataRow("http://api.example.org", DisplayName = "http")]
    [DataRow("ftp://api.example.org", DisplayName = "another scheme")]
    [DataRow("https://user:pass@api.example.org", DisplayName = "user information")]
    [DataRow("https://user@api.example.org", DisplayName = "user name only")]
    [DataRow("https://api.example.org/?key=1", DisplayName = "query")]
    [DataRow("https://api.example.org/?", DisplayName = "empty query")]
    [DataRow("https://api.example.org/#part", DisplayName = "fragment")]
    [DataRow("https://api.example.org/#", DisplayName = "empty fragment")]
    public void HttpsBaseUrl_AnythingElse_FailsWithoutTheUrl(string url)
    {
        ValidationResult result = Check.HttpsBaseUrl(new Uri(url), "Endpoint");

        result.Should().NotBe(ValidationResult.Success);
        result.ErrorMessage.Should().Be("Endpoint must be an absolute https URL without user information, query or fragment");
        result.MemberNames.Should().Equal("Endpoint");
    }

    [TestMethod]
    public void HttpsBaseUrl_RelativeUrl_Fails()
    {
        Check.HttpsBaseUrl(new Uri("/v1/rest-api", UriKind.Relative), "Endpoint").Should().NotBe(ValidationResult.Success);
    }

    [TestMethod]
    public void HttpsBaseUrl_Null_Fails()
    {
        Check.HttpsBaseUrl(null, "Endpoint").MemberNames.Should().Equal("Endpoint");
    }

    [TestMethod]
    public void HttpsBaseUrl_WithoutName_NamesTheArgumentExpression()
    {
        Uri? endpoint = null;

        Check.HttpsBaseUrl(endpoint).MemberNames.Should().Equal(nameof(endpoint));
    }

    [TestMethod]
    public void HttpsBaseUrl_InAnOptionsRecord_FailsValidation()
    {
        var insecure = new ClientOptions { Endpoint = new Uri("http://api.example.org") };
        var defaults = new ClientOptions();

        Validator.TryValidateObject(insecure, new ValidationContext(insecure), null, true).Should().BeFalse();
        Validator.TryValidateObject(defaults, new ValidationContext(defaults), null, true).Should().BeTrue();
    }

    private sealed record ClientOptions : IValidatableObject
    {
        public Uri Endpoint { get; init; } = new("https://api.example.org/v1");

        public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
        {
            yield return Check.HttpsBaseUrl(Endpoint, nameof(Endpoint));
        }
    }
}
