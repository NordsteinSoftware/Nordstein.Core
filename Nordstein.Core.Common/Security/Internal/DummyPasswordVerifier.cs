using System.Buffers;
using System.Security.Cryptography;
using System.Text;

namespace Nordstein.Core.Common.Security.Internal;

/// <summary>
/// The timing equalizer of refused sign-ins. See <see cref="IDummyPasswordVerifier"/>.
/// </summary>
/// <remarks>
/// Immutable after construction, so one instance serves concurrent callers. The unknown password comes from
/// <see cref="RandomNumberGenerator"/>; its secrecy protects nothing (a dummy result is never acted on), it
/// only keeps the dummy hash from being one anybody could precompute.
/// </remarks>
internal sealed class DummyPasswordVerifier : IDummyPasswordVerifier
{
    private const int UnknownPasswordBytes = 32;

    private readonly IPasswordHasher hasher;
    private readonly string dummyHash;

    public DummyPasswordVerifier(IPasswordHasher hasher)
    {
        ArgumentNullException.ThrowIfNull(hasher);
        this.hasher = hasher;
        Span<byte> unknown = stackalloc byte[UnknownPasswordBytes];
        try
        {
            RandomNumberGenerator.Fill(unknown);
            dummyHash = hasher.Hash(Convert.ToHexString(unknown));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(unknown);
        }
    }

    public void Verify(string? password)
    {
        // The hasher returns Failed without deriving for a password with no UTF-8 form, which would make
        // this refusal fast: the empty string keeps the derivation's cost.
        string derivable = password is not null && HasUtf8Form(password) ? password : string.Empty;
        _ = hasher.Verify(derivable, dummyHash);
    }

    private static bool HasUtf8Form(string value)
    {
        ReadOnlySpan<char> rest = value;
        while (!rest.IsEmpty)
        {
            if (Rune.DecodeFromUtf16(rest, out _, out int consumed) != OperationStatus.Done)
            {
                return false;
            }

            rest = rest[consumed..];
        }

        return true;
    }
}
