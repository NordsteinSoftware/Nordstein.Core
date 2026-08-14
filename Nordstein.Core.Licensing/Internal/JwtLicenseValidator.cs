using System.IdentityModel.Tokens.Jwt;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;

namespace Nordstein.Core.Licensing.Internal;

/// <summary>
/// License JWT validator. Verifies ES256 (and legacy RS256) signatures against the configured SPKI
/// public keys and projects the <c>tier</c>, <c>feat</c>, and <c>lim</c> claims onto a
/// <see cref="LicenseSnapshot"/>, resolving names through the product's
/// <see cref="ILicenseTierPolicy"/>.
/// </summary>
internal sealed class JwtLicenseValidator : IJwtLicenseValidator
{
    private readonly string issuer;
    private readonly string audience;
    private readonly IReadOnlyList<SecurityKey> signingKeys;
    private readonly ILicenseTierPolicy policy;
    private readonly ILogger<JwtLicenseValidator> logger;

    public JwtLicenseValidator(
        LicensingConfiguration configuration,
        ILicenseTierPolicy policy,
        ILogger<JwtLicenseValidator> logger)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(policy);
        this.issuer = configuration.Issuer;
        this.audience = configuration.Audience;
        this.policy = policy;
        this.logger = logger;
        this.signingKeys = LoadKeys(configuration.PublicKeys);

        if (this.signingKeys.Count == 0)
        {
            // A misconfiguration, not a bad license: with no trusted keys every JWT is rejected
            // as BadSignature. Deliberately a warning rather than a throw — the engine's contract
            // is to degrade to the fallback tier, never to take the host down over licensing.
            logger.LogWarning("No usable license public keys configured; every license JWT will be rejected");
        }
    }

    public LicenseSnapshot Validate(string jwt)
    {
        if (string.IsNullOrWhiteSpace(jwt))
            throw new InvalidLicenseException(InvalidLicenseReason.Malformed);

        var handler = new JwtSecurityTokenHandler { MapInboundClaims = false };

        var parameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = issuer,
            ValidateAudience = true,
            ValidAudience = audience,
            ValidateIssuerSigningKey = true,
            IssuerSigningKeys = signingKeys,
            ValidateLifetime = true,
            ValidAlgorithms = [SecurityAlgorithms.EcdsaSha256, SecurityAlgorithms.RsaSha256],
            ClockSkew = TimeSpan.Zero,
        };

        JwtSecurityToken token;
        try
        {
            handler.ValidateToken(jwt, parameters, out var validated);
            token = (JwtSecurityToken)validated;
        }
        catch (SecurityTokenExpiredException ex)
        {
            throw new InvalidLicenseException(InvalidLicenseReason.Expired, ex.Message, ex);
        }
        catch (SecurityTokenInvalidIssuerException ex)
        {
            throw new InvalidLicenseException(InvalidLicenseReason.WrongIssuer, ex.Message, ex);
        }
        catch (SecurityTokenInvalidAudienceException ex)
        {
            throw new InvalidLicenseException(InvalidLicenseReason.WrongAudience, ex.Message, ex);
        }
        catch (SecurityTokenSignatureKeyNotFoundException ex)
        {
            throw new InvalidLicenseException(InvalidLicenseReason.BadSignature, ex.Message, ex);
        }
        catch (SecurityTokenInvalidSignatureException ex)
        {
            throw new InvalidLicenseException(InvalidLicenseReason.BadSignature, ex.Message, ex);
        }
        catch (Exception ex) when (ex is SecurityTokenException or ArgumentException)
        {
            throw new InvalidLicenseException(InvalidLicenseReason.Malformed, ex.Message, ex);
        }

        return BuildSnapshot(token);
    }

    private LicenseSnapshot BuildSnapshot(JwtSecurityToken token)
    {
        var tier = ParseTier(token);
        var definition = policy.GetDefinition(tier);

        var features = new HashSet<string>(definition.Features);
        foreach (var claim in token.Claims.Where(c => c.Type == "feat"))
        {
            if (policy.TryResolveFeature(claim.Value, out var feature))
                features.Add(feature);
            else
                logger.LogWarning("Ignoring unknown license feature claim '{Feature}'", claim.Value);
        }

        var limits = new Dictionary<string, long>(definition.Limits);
        foreach (var claim in token.Claims.Where(c => c.Type == "lim"))
        {
            // Encoded as "Name=Value", e.g. "MaxUsers=50".
            var separator = claim.Value.IndexOf('=');
            if (separator <= 0)
            {
                logger.LogWarning("Ignoring malformed license limit claim '{Limit}'", claim.Value);
                continue;
            }

            var name = claim.Value[..separator];
            var rawValue = claim.Value[(separator + 1)..];
            if (policy.TryResolveLimit(name, out var limit)
                && long.TryParse(rawValue, out var value))
            {
                limits[limit] = value;
            }
            else
            {
                logger.LogWarning("Ignoring unparseable license limit claim '{Limit}'", claim.Value);
            }
        }

        DateTimeOffset? expiresAt = token.Payload.Expiration is { } exp
            ? DateTimeOffset.FromUnixTimeSeconds(exp)
            : null;

        var email = token.Subject;

        return new LicenseSnapshot(
            tier,
            LicenseStatus.Active,
            expiresAt,
            GracePeriodEndsAt: null,
            CustomerEmail: string.IsNullOrWhiteSpace(email) ? null : email,
            Jti: token.Id,
            features,
            limits,
            Offline: ReadOfflineClaim(token));
    }

    /// <summary>
    /// Reads the optional <c>offline</c> claim, matched strictly by <b>JSON type</b> per the wire
    /// contract: offline-only is signalled only by a JSON boolean <c>true</c>. A missing claim,
    /// <c>false</c>, a quoted string (even <c>"true"</c>), or a number all read as a normal online
    /// license. Never string-matched — the value must be a real boolean <c>true</c> to flip the
    /// install offline. (Depending on the JWT library's deserialization the boolean surfaces as a
    /// CLR <see cref="bool"/> or a <see cref="JsonElement"/>, so both are accepted.)
    /// </summary>
    private static bool ReadOfflineClaim(JwtSecurityToken token)
    {
        if (!token.Payload.TryGetValue("offline", out var raw) || raw is null)
            return false;

        return raw switch
        {
            bool flag => flag,
            JsonElement { ValueKind: JsonValueKind.True } => true,
            _ => false,
        };
    }

    private string ParseTier(JwtSecurityToken token)
    {
        var raw = token.Claims.FirstOrDefault(c => c.Type == "tier")?.Value;
        if (policy.TryResolveTier(raw, out var tier))
            return tier;

        logger.LogWarning(
            "Unknown license tier '{Tier}'; falling back to {Fallback}",
            raw,
            policy.FallbackTier);
        return policy.FallbackTier;
    }

    private static IReadOnlyList<SecurityKey> LoadKeys(IReadOnlyList<string> base64SpkiKeys)
    {
        var keys = new List<SecurityKey>();
        foreach (var encoded in base64SpkiKeys)
        {
            if (string.IsNullOrWhiteSpace(encoded))
                continue;

            var der = Convert.FromBase64String(encoded.Trim());

            // Prefer ECDSA (P-256, ES256). Fall back to RSA (RS256) for legacy keys: the SPKI
            // AlgorithmIdentifier OID won't match an EC key, so the import throws and we retry.
            try
            {
                var ecdsa = ECDsa.Create();
                ecdsa.ImportSubjectPublicKeyInfo(der, out _);
                keys.Add(new ECDsaSecurityKey(ecdsa));
            }
            catch (CryptographicException)
            {
                var rsa = RSA.Create();
                rsa.ImportSubjectPublicKeyInfo(der, out _);
                keys.Add(new RsaSecurityKey(rsa));
            }
        }

        return keys;
    }
}
