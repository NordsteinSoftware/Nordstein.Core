using System.Security.Cryptography;
using System.Text;

namespace Nordstein.Core.Common.Security;

/// <summary>
/// Hex-encoded SHA-256 for stable deterministic hashes. Suitable for high-entropy, verify-only
/// secrets and content fingerprints. Not suitable for passwords or other human-chosen secrets,
/// which require a salted, deliberately slow password-hashing algorithm.
/// </summary>
public static class Sha256
{
    /// <summary>Returns the upper-case hex-encoded SHA-256 of <paramref name="value"/> (64 chars).</summary>
    public static string HexHash(string value)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
}
