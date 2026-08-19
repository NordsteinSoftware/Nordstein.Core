using System.Buffers;
using System.Security.Cryptography;

namespace Nordstein.Core.Common.Cryptography.Internal;

/// <summary>
/// Chunked AES-256-GCM STREAM codec with a per-message HKDF-SHA256 subkey. See
/// <see cref="IAeadStreamCodec"/>.
/// </summary>
internal sealed class AeadStreamCodec : IAeadStreamCodec
{
    private const int SubkeySize = 32;
    private const int SaltSize = 16;

    public int SaltSizeInBytes => SaltSize;

    public byte[] NewSalt() => RandomNumberGenerator.GetBytes(SaltSize);

    public async Task EncryptAsync(
        Stream plaintext,
        Stream destination,
        ReadOnlyMemory<byte> key,
        ReadOnlyMemory<byte> salt,
        ReadOnlyMemory<byte> info,
        ReadOnlyMemory<byte> associatedData,
        ChunkParameters? parameters = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(plaintext);
        ArgumentNullException.ThrowIfNull(destination);
        if (salt.Length != SaltSize)
        {
            throw new ArgumentException(
                $"The salt must be {SaltSize} bytes; use {nameof(NewSalt)}.",
                nameof(salt));
        }

        int chunkSize = (parameters ?? ChunkParameters.Default).ChunkSize;

        byte[] subkey = DeriveSubkey(key.Span, salt.Span, info.Span);
        byte[] current = ArrayPool<byte>.Shared.Rent(chunkSize);
        byte[] next = ArrayPool<byte>.Shared.Rent(chunkSize);
        byte[] output = ArrayPool<byte>.Shared.Rent(chunkSize + AesGcmBox.TagSize);
        byte[] nonce = new byte[AesGcmBox.NonceSize];
        try
        {
            int currentLength = await ReadChunkAsync(plaintext, current, chunkSize, cancellationToken)
                .ConfigureAwait(false);
            uint index = 0;

            while (true)
            {
                int nextLength = await ReadChunkAsync(plaintext, next, chunkSize, cancellationToken)
                    .ConfigureAwait(false);
                bool isFinal = nextLength == 0;

                if (!isFinal && index >= StreamNonce.MaxChunkIndex)
                {
                    throw new ChunkCountExceededException();
                }

                StreamNonce.Write(nonce, index, isFinal);
                int recordLength = currentLength + AesGcmBox.TagSize;
                AesGcmBox.Seal(
                    subkey,
                    nonce,
                    current.AsSpan(0, currentLength),
                    associatedData.Span,
                    output.AsSpan(0, recordLength));
                await destination.WriteAsync(output.AsMemory(0, recordLength), cancellationToken)
                    .ConfigureAwait(false);

                if (isFinal)
                {
                    break;
                }

                (current, next) = (next, current);
                currentLength = nextLength;
                index++;
            }
        }
        finally
        {
            CryptographicOperations.ZeroMemory(subkey);
            ArrayPool<byte>.Shared.Return(current, clearArray: true);
            ArrayPool<byte>.Shared.Return(next, clearArray: true);
            ArrayPool<byte>.Shared.Return(output, clearArray: true);
        }
    }

    public Stream OpenRead(
        Stream ciphertext,
        ReadOnlyMemory<byte> key,
        ReadOnlyMemory<byte> salt,
        ReadOnlyMemory<byte> info,
        ReadOnlyMemory<byte> associatedData,
        ChunkParameters? parameters = null)
    {
        ArgumentNullException.ThrowIfNull(ciphertext);
        if (salt.Length != SaltSize)
        {
            throw new ArgumentException(
                $"The salt must be {SaltSize} bytes; use {nameof(NewSalt)}.",
                nameof(salt));
        }

        int chunkSize = (parameters ?? ChunkParameters.Default).ChunkSize;
        byte[] subkey = DeriveSubkey(key.Span, salt.Span, info.Span);
        return new AeadDecryptStream(ciphertext, subkey, associatedData.ToArray(), chunkSize);
    }

    private static byte[] DeriveSubkey(ReadOnlySpan<byte> key, ReadOnlySpan<byte> salt, ReadOnlySpan<byte> info)
    {
        byte[] subkey = new byte[SubkeySize];
        HKDF.DeriveKey(HashAlgorithmName.SHA256, key, subkey, salt, info);
        return subkey;
    }

    private static async Task<int> ReadChunkAsync(
        Stream source,
        byte[] buffer,
        int count,
        CancellationToken cancellationToken)
    {
        int total = 0;
        while (total < count)
        {
            int read = await source.ReadAsync(buffer.AsMemory(total, count - total), cancellationToken)
                .ConfigureAwait(false);
            if (read == 0)
            {
                break;
            }

            total += read;
        }

        return total;
    }
}
