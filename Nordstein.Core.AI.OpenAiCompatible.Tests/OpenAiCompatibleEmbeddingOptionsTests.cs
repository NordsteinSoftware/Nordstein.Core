using AwesomeAssertions;

namespace Nordstein.Core.AI.OpenAiCompatible.Tests;

[TestClass]
public sealed class OpenAiCompatibleEmbeddingOptionsTests
{
    private static readonly OpenAiCompatibleEmbeddingOptions Valid = new()
    {
        BaseUrl = new Uri("https://api.example.com/v1"),
        ApiKey = "secret-key-value",
        Model = "example-embed-1",
        Dimension = 4,
    };

    [TestMethod]
    public void Validate_ValidOptions_PassesWithDefaults()
    {
        Valid.Validate();

        Valid.BatchSize.Should().Be(32);
        Valid.Timeout.Should().Be(TimeSpan.FromSeconds(60));
        Valid.SendDimension.Should().BeFalse();
    }

    [TestMethod]
    [DataRow("ftp://api.example.com/v1")]
    [DataRow("https://user:pass@api.example.com/v1")]
    [DataRow("https://api.example.com/v1?key=1")]
    [DataRow("https://api.example.com/v1#part")]
    public void Validate_UnusableBaseUrl_ThrowsWithoutTheUrl(string url)
    {
        Action act = (Valid with { BaseUrl = new Uri(url) }).Validate;

        act.Should().Throw<InvalidOperationException>().Which.Message.Should().Contain("BaseUrl").And.NotContain("example.com");
    }

    [TestMethod]
    public void Validate_RelativeOrMissingBaseUrl_Throws()
    {
        Action relative = (Valid with { BaseUrl = new Uri("/v1", UriKind.Relative) }).Validate;
        Action missing = (Valid with { BaseUrl = Internal.Missing.Reference<Uri>() }).Validate;

        relative.Should().Throw<InvalidOperationException>();
        missing.Should().Throw<InvalidOperationException>();
    }

    [TestMethod]
    public void Validate_KeyOverPlainHttpToAnotherHost_Throws()
    {
        Action act = (Valid with { BaseUrl = new Uri("http://api.example.com/v1") }).Validate;

        act.Should().Throw<InvalidOperationException>().Which.Message.Should().Contain("ApiKey").And.NotContain("secret-key-value");
    }

    [TestMethod]
    [DataRow("http://localhost:11434/v1")]
    [DataRow("http://127.0.0.1:8080/v1")]
    [DataRow("http://[::1]:8080/v1")]
    public void Validate_KeyOverPlainHttpToLoopback_Passes(string url)
        => (Valid with { BaseUrl = new Uri(url) }).Validate();

    [TestMethod]
    [DataRow(null)]
    [DataRow("")]
    public void Validate_NoKeyOverPlainHttp_Passes(string? key)
        => (Valid with { BaseUrl = new Uri("http://models.internal.example/v1"), ApiKey = key }).Validate();

    [TestMethod]
    public void Validate_OtherUnusableValues_Throw()
    {
        ((Action)(Valid with { Model = " " }).Validate).Should().Throw<InvalidOperationException>().WithMessage("*Model*");
        ((Action)(Valid with { Dimension = 0 }).Validate).Should().Throw<InvalidOperationException>().WithMessage("*Dimension*");
        ((Action)(Valid with { BatchSize = 0 }).Validate).Should().Throw<InvalidOperationException>().WithMessage("*BatchSize*");
        ((Action)(Valid with { BatchSize = OpenAiCompatibleEmbeddingOptions.MaxBatchSize + 1 }).Validate)
            .Should().Throw<InvalidOperationException>().WithMessage("*BatchSize*");
        ((Action)(Valid with { Timeout = TimeSpan.Zero }).Validate).Should().Throw<InvalidOperationException>().WithMessage("*Timeout*");
        (Valid with { BatchSize = OpenAiCompatibleEmbeddingOptions.MaxBatchSize }).Validate();
    }

    [TestMethod]
    public void ToString_WithAndWithoutKey_NeverPrintsTheKey()
    {
        Valid.ToString().Should().Contain("example-embed-1").And.Contain("ApiKey = set").And.NotContain("secret-key-value");
        (Valid with { ApiKey = null }).ToString().Should().Contain("ApiKey = none");
    }
}
