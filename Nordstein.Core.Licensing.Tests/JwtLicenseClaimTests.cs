using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using AwesomeAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.IdentityModel.Tokens;
using Nordstein.Core.Licensing.Internal;

namespace Nordstein.Core.Licensing.Tests;

/// <summary>
/// <see cref="JwtLicenseValidator"/> on tokens the shared factory cannot produce: no tier claim, a blank subject, an
/// <c>offline</c> claim of an object type, and a signature that fails under the trusted key it names.
/// </summary>
[TestClass]
public sealed class JwtLicenseClaimTests
{
    private const string Issuer = "https://license.example.com";
    private const string Audience = "example-product";

    [TestMethod]
    public void Validate_NoTierClaim_FallsBackToThePolicysFallbackTier()
    {
        using ECDsa key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        string jwt = Sign(key, [new Claim(JwtRegisteredClaimNames.Sub, "customer@example.com")]);

        LicenseSnapshot snapshot = Validator(key).Validate(jwt);

        snapshot.Tier.Should().Be(new TestTierPolicy().FallbackTier);
        snapshot.Status.Should().Be(LicenseStatus.Active);
    }

    [TestMethod]
    [DataRow("")]
    [DataRow("   ")]
    public void Validate_BlankSubject_HasNoCustomerEmail(string subject)
    {
        using ECDsa key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        string jwt = Sign(key, [new Claim("tier", TestTierPolicy.Premium), new Claim(JwtRegisteredClaimNames.Sub, subject)]);

        Validator(key).Validate(jwt).CustomerEmail.Should().BeNull();
    }

    [TestMethod]
    public void Validate_OfflineClaimThatIsAnObject_IsAnOnlineLicense()
    {
        using ECDsa key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        string jwt = Sign(key, [new Claim("tier", TestTierPolicy.Premium), new Claim("offline", """{"value":true}""", JsonClaimValueTypes.Json)]);

        Validator(key).Validate(jwt).Offline.Should().BeFalse();
    }

    [TestMethod]
    public void Validate_SignatureBrokenUnderTheNamedTrustedKey_IsBadSignature()
    {
        using ECDsa key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        string jwt = Sign(key, [new Claim("tier", TestTierPolicy.Premium)], keyId: "trusted");
        string[] parts = jwt.Split('.');
        string tampered = $"{parts[0]}.{parts[1]}.{(parts[2][0] == 'A' ? 'B' : 'A')}{parts[2][1..]}";

        Action act = () => Validator(key).Validate(tampered);

        act.Should().Throw<InvalidLicenseException>().Which.Reason.Should().Be(InvalidLicenseReason.BadSignature);
    }

    private static JwtLicenseValidator Validator(ECDsa key) =>
        new(
            new LicensingConfiguration
            {
                Issuer = Issuer,
                Audience = Audience,
                ServerUrl = "https://license.example.com",
                PublicKeys = [Convert.ToBase64String(key.ExportSubjectPublicKeyInfo())],
                CheckIntervalHours = 24,
                OfflineGracePeriodDays = 7,
                CacheFilePath = Path.Combine(Path.GetTempPath(), "never-written.json"),
            },
            new TestTierPolicy(),
            NullLogger<JwtLicenseValidator>.Instance);

    private static string Sign(ECDsa key, IEnumerable<Claim> claims, string? keyId = null)
    {
        var securityKey = new ECDsaSecurityKey(key) { KeyId = keyId };
        var token = new JwtSecurityToken(
            issuer: Issuer,
            audience: Audience,
            claims: claims,
            notBefore: DateTime.UtcNow.AddMinutes(-5),
            expires: DateTime.UtcNow.AddDays(30),
            signingCredentials: new SigningCredentials(securityKey, SecurityAlgorithms.EcdsaSha256));
        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
