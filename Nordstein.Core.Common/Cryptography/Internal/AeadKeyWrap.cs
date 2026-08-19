using System.Security.Cryptography;

namespace Nordstein.Core.Common.Cryptography.Internal;

/// <summary>
/// AES-256-GCM key wrap with associated-data binding. See <see cref="IAeadKeyWrap"/>.
/// </summary>
internal sealed class AeadKeyWrap : IAeadKeyWrap
{
    private const int KekSize = 32;

    public WrappedKey Wrap(ReadOnlySpan<byte> kek, ReadOnlySpan<byte> dataKey, ReadOnlySpan<byte> associatedData)
    {
        RequireKekLength(kek);

        byte[] nonce = RandomNumberGenerator.GetBytes(AesGcmBox.NonceSize);
        byte[] ciphertext = AesGcmBox.Seal(kek, nonce, dataKey, associatedData);
        return new WrappedKey(nonce, ciphertext);
    }

    public byte[] Unwrap(ReadOnlySpan<byte> kek, WrappedKey wrapped, ReadOnlySpan<byte> associatedData)
    {
        ArgumentNullException.ThrowIfNull(wrapped);
        RequireKekLength(kek);

        ReadOnlySpan<byte> record = wrapped.Ciphertext.Span;
        if (record.Length < AesGcmBox.TagSize)
        {
            throw new AeadAuthenticationFailedException();
        }

        byte[] dataKey = new byte[record.Length - AesGcmBox.TagSize];
        if (!AesGcmBox.TryOpen(kek, wrapped.Nonce.Span, record, associatedData, dataKey))
        {
            CryptographicOperations.ZeroMemory(dataKey);
            throw new AeadAuthenticationFailedException();
        }

        return dataKey;
    }

    private static void RequireKekLength(ReadOnlySpan<byte> kek)
    {
        if (kek.Length != KekSize)
        {
            throw new ArgumentException(
                $"The key-encryption key must be {KekSize} bytes (256-bit).",
                nameof(kek));
        }
    }
}
