using AwesomeAssertions;
using Nordstein.Core.AI.Clients;

namespace Nordstein.Core.AI.Tests.Clients;

[TestClass]
public sealed class InferenceExceptionsTests
{
    [TestMethod]
    public void Unavailable_CarriesItsMessageAndTheTransportError()
    {
        var transport = new HttpRequestException("connection refused");

        var plain = new InferenceUnavailableException("The endpoint did not answer.");
        var wrapped = new InferenceUnavailableException("The endpoint did not answer.", transport);

        plain.Message.Should().Be("The endpoint did not answer.");
        plain.InnerException.Should().BeNull();
        wrapped.InnerException.Should().BeSameAs(transport);
        typeof(InferenceUnavailableException).IsSealed.Should().BeTrue();
    }

    [TestMethod]
    public void Contract_CarriesItsMessageAndCause_AndProductsMayDeriveFromIt()
    {
        var cause = new FormatException("not JSON");

        var plain = new InferenceContractException("Two vectors for three inputs.");
        var wrapped = new InferenceContractException("The answer is not an embeddings response.", cause);

        plain.Message.Should().Be("Two vectors for three inputs.");
        plain.InnerException.Should().BeNull();
        wrapped.InnerException.Should().BeSameAs(cause);
        typeof(InferenceContractException).IsSealed.Should().BeFalse();
    }
}
