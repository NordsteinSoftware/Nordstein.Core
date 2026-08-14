using System.Security.Claims;
using AwesomeAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Nordstein.Core.Licensing.Internal;

namespace Nordstein.Core.Licensing.Tests;

[TestClass]
public sealed class JwtLicenseValidatorTests
{
    public required TestContext TestContext { get; init; }

    private readonly TestLicenseFactory factory = new();

    [TestCleanup]
    public void Teardown() => factory.Dispose();

    private JwtLicenseValidator CreateValidator()
        => new(factory.Configuration(), new TestTierPolicy(), NullLogger<JwtLicenseValidator>.Instance);

    [TestMethod]
    public void Validate_ValidPremiumToken_ReturnsActiveSnapshot()
    {
        var jwt = factory.CreateJwt(tier: TestTierPolicy.Premium);

        var snapshot = CreateValidator().Validate(jwt);

        snapshot.Tier.Should().Be(TestTierPolicy.Premium);
        snapshot.Status.Should().Be(LicenseStatus.Active);
        snapshot.CustomerEmail.Should().Be("customer@example.com");
        snapshot.Features.Should().Contain(TestTierPolicy.Analytics);
        snapshot.Jti.Should().NotBeNullOrEmpty();
    }

    [TestMethod]
    public void Validate_LegacyRs256Token_StillValidates()
    {
        // Backward compatibility: keys/licenses from before an ES256 migration must keep working.
        using var rsaFactory = new TestLicenseFactory(useEcdsa: false);
        var jwt = rsaFactory.CreateJwt(tier: TestTierPolicy.Premium);
        var validator = new JwtLicenseValidator(
            rsaFactory.Configuration(), new TestTierPolicy(), NullLogger<JwtLicenseValidator>.Instance);

        var snapshot = validator.Validate(jwt);

        snapshot.Tier.Should().Be(TestTierPolicy.Premium);
        snapshot.Status.Should().Be(LicenseStatus.Active);
    }

    [TestMethod]
    public void Validate_ExpiredToken_ThrowsExpired()
    {
        var jwt = factory.CreateJwt(expires: DateTimeOffset.UtcNow.AddMinutes(-1));

        FluentActions.Invoking(() => CreateValidator().Validate(jwt))
            .Should().Throw<InvalidLicenseException>()
            .Which.Reason.Should().Be(InvalidLicenseReason.Expired);
    }

    [TestMethod]
    public void Validate_WrongIssuer_ThrowsWrongIssuer()
    {
        var jwt = factory.CreateJwt(issuer: "https://evil.example.com");

        FluentActions.Invoking(() => CreateValidator().Validate(jwt))
            .Should().Throw<InvalidLicenseException>()
            .Which.Reason.Should().Be(InvalidLicenseReason.WrongIssuer);
    }

    [TestMethod]
    public void Validate_WrongAudience_ThrowsWrongAudience()
    {
        var jwt = factory.CreateJwt(audience: "someone-else");

        FluentActions.Invoking(() => CreateValidator().Validate(jwt))
            .Should().Throw<InvalidLicenseException>()
            .Which.Reason.Should().Be(InvalidLicenseReason.WrongAudience);
    }

    [TestMethod]
    public void Validate_TamperedSignature_ThrowsBadSignature()
    {
        // Signed with a different key than the validator is configured to trust.
        var jwt = factory.CreateJwt(sign: false);

        FluentActions.Invoking(() => CreateValidator().Validate(jwt))
            .Should().Throw<InvalidLicenseException>()
            .Which.Reason.Should().Be(InvalidLicenseReason.BadSignature);
    }

    [TestMethod]
    public void Validate_Garbage_ThrowsMalformed()
    {
        FluentActions.Invoking(() => CreateValidator().Validate("not-a-jwt"))
            .Should().Throw<InvalidLicenseException>()
            .Which.Reason.Should().Be(InvalidLicenseReason.Malformed);
    }

    [TestMethod]
    public void Validate_UnknownTier_FallsBackToFallbackDefinition()
    {
        var jwt = factory.CreateJwt(tier: "Platinum");

        var snapshot = CreateValidator().Validate(jwt);

        snapshot.Tier.Should().Be(TestTierPolicy.Basic);
        snapshot.Limits[TestTierPolicy.MaxProjects].Should().Be(1);
    }

    [TestMethod]
    public void Validate_TierMatchedCaseInsensitively_NormalizesToCanonicalName()
    {
        // The policy resolves names case-insensitively (products typically parse enums that
        // way); the snapshot must carry the canonical spelling, not the raw claim value.
        var jwt = factory.CreateJwt(tier: "premium");

        var snapshot = CreateValidator().Validate(jwt);

        snapshot.Tier.Should().Be(TestTierPolicy.Premium);
    }

    [TestMethod]
    public void Validate_FeatureOverlay_AddsFeatureToFallbackTier()
    {
        var jwt = factory.CreateJwt(tier: TestTierPolicy.Basic, features: [TestTierPolicy.Sso]);

        var snapshot = CreateValidator().Validate(jwt);

        snapshot.Features.Should().Contain(TestTierPolicy.Sso);
    }

    [TestMethod]
    public void Validate_UnknownFeatureClaim_IsIgnored()
    {
        var jwt = factory.CreateJwt(tier: TestTierPolicy.Basic, features: ["TimeTravel"]);

        var snapshot = CreateValidator().Validate(jwt);

        snapshot.Features.Should().BeEmpty("unknown feature names must not leak into the snapshot");
    }

    [TestMethod]
    public void Validate_LimitOverlay_OverridesDefaultLimit()
    {
        var jwt = factory.CreateJwt(tier: TestTierPolicy.Basic, limits: [$"{TestTierPolicy.MaxUsers}=50"]);

        var snapshot = CreateValidator().Validate(jwt);

        snapshot.Limits[TestTierPolicy.MaxUsers].Should().Be(50);
    }

    [TestMethod]
    public void Validate_UnknownLimitClaim_IsIgnored()
    {
        var jwt = factory.CreateJwt(tier: TestTierPolicy.Basic, limits: ["MaxUnicorns=7"]);

        var snapshot = CreateValidator().Validate(jwt);

        snapshot.Limits.Should().NotContainKey("MaxUnicorns");
    }

    [TestMethod]
    public void Validate_OfflineClaimTrue_SnapshotIsOffline()
    {
        var jwt = factory.CreateJwt(tier: TestTierPolicy.Premium, offline: true);

        var snapshot = CreateValidator().Validate(jwt);

        snapshot.Offline.Should().BeTrue();
        snapshot.Tier.Should().Be(TestTierPolicy.Premium);
        snapshot.Status.Should().Be(LicenseStatus.Active);
    }

    [TestMethod]
    public void Validate_NoOfflineClaim_SnapshotIsOnline()
    {
        // A normal mint omits the claim entirely; that must read as online (not offline).
        var jwt = factory.CreateJwt(tier: TestTierPolicy.Premium);

        var snapshot = CreateValidator().Validate(jwt);

        snapshot.Offline.Should().BeFalse();
    }

    [TestMethod]
    public void Validate_OfflineClaimFalse_SnapshotIsOnline()
    {
        // Defensive: a server never emits offline:false, but the client must not break on it.
        var jwt = factory.CreateJwt(tier: TestTierPolicy.Premium, offline: false);

        var snapshot = CreateValidator().Validate(jwt);

        snapshot.Offline.Should().BeFalse();
    }

    [TestMethod]
    public void Validate_OfflineClaimJsonString_TreatedAsOnline()
    {
        // The contract is "JSON boolean true" matched by type — a quoted string "true" is NOT a
        // boolean, so it must read as online. This pins the "do not string-match" requirement.
        var jwt = factory.CreateJwt(tier: TestTierPolicy.Premium, offlineRaw: ("true", ClaimValueTypes.String));

        var snapshot = CreateValidator().Validate(jwt);

        snapshot.Offline.Should().BeFalse();
    }

    [TestMethod]
    public void Validate_NoUsablePublicKeys_RejectsEveryTokenAsBadSignature()
    {
        // A misconfigured (empty) trust root is indistinguishable from a forged license by
        // design: nothing verifies, nothing is trusted, the deployment stays on the fallback
        // tier instead of crashing.
        var config = factory.Configuration() with { PublicKeys = ["", "   "] };
        var validator = new JwtLicenseValidator(config, new TestTierPolicy(), NullLogger<JwtLicenseValidator>.Instance);

        FluentActions.Invoking(() => validator.Validate(factory.CreateJwt()))
            .Should().Throw<InvalidLicenseException>()
            .Which.Reason.Should().Be(InvalidLicenseReason.BadSignature);
    }

    [TestMethod]
    public void Validate_OfflineClaimNumber_TreatedAsOnline()
    {
        // A numeric (or any non-boolean) offline claim must not flip the install offline.
        var jwt = factory.CreateJwt(tier: TestTierPolicy.Premium, offlineRaw: ("1", ClaimValueTypes.Integer));

        var snapshot = CreateValidator().Validate(jwt);

        snapshot.Offline.Should().BeFalse();
    }
}
