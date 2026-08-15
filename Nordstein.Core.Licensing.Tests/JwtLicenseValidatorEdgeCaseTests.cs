using AwesomeAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Nordstein.Core.Licensing.Internal;

namespace Nordstein.Core.Licensing.Tests;

/// <summary>
/// Fills the remaining <see cref="JwtLicenseValidator"/> branches not exercised by
/// <c>JwtLicenseValidatorTests</c>: the empty/whitespace short-circuit and the malformed
/// <c>lim</c> claim paths (no separator, and a leading separator).
/// </summary>
[TestClass]
public sealed class JwtLicenseValidatorEdgeCaseTests
{
    private static JwtLicenseValidator CreateValidator(TestLicenseFactory factory)
        => new(factory.Configuration(), new TestTierPolicy(), NullLogger<JwtLicenseValidator>.Instance);

    [TestMethod]
    public void Validate_EmptyString_ThrowsMalformedBeforeAnyParsing()
    {
        using var factory = new TestLicenseFactory();

        FluentActions.Invoking(() => CreateValidator(factory).Validate(string.Empty))
            .Should().Throw<InvalidLicenseException>()
            .Which.Reason.Should().Be(InvalidLicenseReason.Malformed);
    }

    [TestMethod]
    public void Validate_WhitespaceOnly_ThrowsMalformedBeforeAnyParsing()
    {
        using var factory = new TestLicenseFactory();

        FluentActions.Invoking(() => CreateValidator(factory).Validate("   \t  "))
            .Should().Throw<InvalidLicenseException>()
            .Which.Reason.Should().Be(InvalidLicenseReason.Malformed);
    }

    [TestMethod]
    public void Validate_LimitClaimWithoutSeparator_IsIgnoredButTokenStillValid()
    {
        // A "lim" claim of "NoSeparatorHere" has no '=' (IndexOf returns -1), so it is dropped and
        // only the tier defaults remain. The token itself is otherwise valid.
        using var factory = new TestLicenseFactory();
        var jwt = factory.CreateJwt(tier: TestTierPolicy.Basic, limits: ["NoSeparatorHere"]);

        var snapshot = CreateValidator(factory).Validate(jwt);

        snapshot.Status.Should().Be(LicenseStatus.Active);
        snapshot.Limits.Should().NotContainKey("NoSeparatorHere");
        snapshot.Limits[TestTierPolicy.MaxProjects].Should().Be(1);
        snapshot.Limits.Should().HaveCount(2, "only the two Basic-tier default limits survive");
    }

    [TestMethod]
    public void Validate_LimitClaimWithLeadingSeparator_IsIgnored()
    {
        // "=50" places the separator at index 0, so the name portion is empty; the guard
        // (separator <= 0) drops it rather than materializing an empty-named limit.
        using var factory = new TestLicenseFactory();
        var jwt = factory.CreateJwt(tier: TestTierPolicy.Basic, limits: ["=50"]);

        var snapshot = CreateValidator(factory).Validate(jwt);

        snapshot.Limits.Should().NotContainKey(string.Empty);
        snapshot.Limits.Should().HaveCount(2, "only the two Basic-tier default limits survive");
    }
}
