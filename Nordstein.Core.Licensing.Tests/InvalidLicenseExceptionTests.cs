using AwesomeAssertions;

namespace Nordstein.Core.Licensing.Tests;

/// <summary>
/// Direct coverage of the public <see cref="InvalidLicenseException"/> constructor overloads and
/// its <see cref="InvalidLicenseException.Reason"/> property. A pure value type — no container.
/// </summary>
[TestClass]
public sealed class InvalidLicenseExceptionTests
{
    [TestMethod]
    public void Ctor_ReasonOnly_SetsReasonAndSynthesizedMessage_WithoutInner()
    {
        var exception = new InvalidLicenseException(InvalidLicenseReason.Expired);

        exception.Reason.Should().Be(InvalidLicenseReason.Expired);
        exception.Message.Should().Be("The configured license is invalid: Expired.");
        exception.InnerException.Should().BeNull();
    }

    [TestMethod]
    public void Ctor_ReasonOnly_MessageReflectsTheGivenReason()
    {
        // The synthesized message must name the specific reason, not a generic string.
        var exception = new InvalidLicenseException(InvalidLicenseReason.WrongAudience);

        exception.Reason.Should().Be(InvalidLicenseReason.WrongAudience);
        exception.Message.Should().Contain(nameof(InvalidLicenseReason.WrongAudience));
    }

    [TestMethod]
    public void Ctor_ReasonAndMessage_UsesTheGivenMessage_WithoutInner()
    {
        var exception = new InvalidLicenseException(InvalidLicenseReason.WrongIssuer, "issuer mismatch");

        exception.Reason.Should().Be(InvalidLicenseReason.WrongIssuer);
        exception.Message.Should().Be("issuer mismatch");
        exception.InnerException.Should().BeNull();
    }

    [TestMethod]
    public void Ctor_ReasonMessageAndInner_PreservesAllThree()
    {
        var inner = new InvalidOperationException("underlying");

        var exception = new InvalidLicenseException(InvalidLicenseReason.BadSignature, "outer", inner);

        exception.Reason.Should().Be(InvalidLicenseReason.BadSignature);
        exception.Message.Should().Be("outer");
        exception.InnerException.Should().BeSameAs(inner);
    }

    [TestMethod]
    public void Ctor_ReasonOnly_MalformedReason_ProducesReadableDefaultMessage()
    {
        var exception = new InvalidLicenseException(InvalidLicenseReason.Malformed);

        exception.Message.Should().Be("The configured license is invalid: Malformed.");
        exception.Reason.Should().Be(InvalidLicenseReason.Malformed);
    }
}
