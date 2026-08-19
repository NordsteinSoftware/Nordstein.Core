using System.Buffers;
using System.Security.Cryptography;

namespace Nordstein.Core.Common.Cryptography.Internal;

/// <summary>
/// Forward-only, read-only stream that authenticates and decrypts a chunked AES-256-GCM STREAM
/// message one chunk at a time. Returned by <see cref="AeadStreamCodec.OpenRead"/>.
/// </summary>
/// <remarks>
/// Truncation is told apart from tampering by re-checking the last present record as a non-final
/// chunk: if it authenticates that way, the real final chunk is missing
/// (<see cref="CiphertextTruncatedException"/>); otherwise the failure is an authentication failure.
/// </remarks>
internal sealed class AeadDecryptStream : Stream
{
    private readonly Stream source;
    private readonly byte[] subkey;
    private readonly byte[] associatedData;
    private readonly int chunkSize;
    private readonly int recordSize;
    private readonly byte[] recordBuffer;
    private readonly byte[] plainBuffer;

    private int plainLength;
    private int plainPosition;
    private uint index;
    private bool finished;
    private bool disposed;
    private int carry = -1;

    internal AeadDecryptStream(Stream source, byte[] subkey, byte[] associatedData, int chunkSize)
    {
        this.source = source;
        this.subkey = subkey;
        this.associatedData = associatedData;
        this.chunkSize = chunkSize;
        recordSize = chunkSize + AesGcmBox.TagSize;
        recordBuffer = ArrayPool<byte>.Shared.Rent(recordSize);
        plainBuffer = ArrayPool<byte>.Shared.Rent(chunkSize);
    }

    public override bool CanRead => true;

    public override bool CanSeek => false;

    public override bool CanWrite => false;

    public override long Length => throw new NotSupportedException();

    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    public override void Flush()
    {
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        ArgumentNullException.ThrowIfNull(buffer);
        ArgumentOutOfRangeException.ThrowIfNegative(offset);
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(count, buffer.Length - offset);

        if (count == 0)
        {
            return 0;
        }

        if (plainPosition >= plainLength)
        {
            if (!DecryptNextChunk())
            {
                return 0;
            }
        }

        int available = plainLength - plainPosition;
        if (available <= 0)
        {
            return 0;
        }

        int toCopy = Math.Min(available, count);
        Array.Copy(plainBuffer, plainPosition, buffer, offset, toCopy);
        plainPosition += toCopy;
        return toCopy;
    }

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    public override void SetLength(long value) => throw new NotSupportedException();

    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    private bool DecryptNextChunk()
    {
        if (finished)
        {
            return false;
        }

        int recordLength = ReadRecord(out bool isLast);

        if (index > StreamNonce.MaxChunkIndex)
        {
            throw new ChunkCountExceededException();
        }

        Span<byte> nonce = stackalloc byte[AesGcmBox.NonceSize];

        if (isLast)
        {
            if (recordLength < AesGcmBox.TagSize)
            {
                throw new CiphertextTruncatedException();
            }

            StreamNonce.Write(nonce, index, isFinal: true);
            if (AesGcmBox.TryOpen(subkey, nonce, recordBuffer.AsSpan(0, recordLength), associatedData, plainBuffer))
            {
                plainLength = recordLength - AesGcmBox.TagSize;
                plainPosition = 0;
                finished = true;
                return true;
            }

            // A full record that authenticates as a non-final chunk means the final chunk is missing.
            if (recordLength == recordSize)
            {
                StreamNonce.Write(nonce, index, isFinal: false);
                if (AesGcmBox.TryOpen(subkey, nonce, recordBuffer.AsSpan(0, recordLength), associatedData, plainBuffer))
                {
                    throw new CiphertextTruncatedException();
                }
            }

            throw new AeadAuthenticationFailedException();
        }

        StreamNonce.Write(nonce, index, isFinal: false);
        if (!AesGcmBox.TryOpen(subkey, nonce, recordBuffer.AsSpan(0, recordLength), associatedData, plainBuffer))
        {
            throw new AeadAuthenticationFailedException();
        }

        plainLength = recordLength - AesGcmBox.TagSize;
        plainPosition = 0;
        index++;
        return true;
    }

    private int ReadRecord(out bool isLast)
    {
        int length = 0;
        if (carry >= 0)
        {
            recordBuffer[0] = (byte)carry;
            carry = -1;
            length = 1;
        }

        while (length < recordSize)
        {
            int read = source.Read(recordBuffer, length, recordSize - length);
            if (read == 0)
            {
                break;
            }

            length += read;
        }

        if (length == recordSize)
        {
            int probe = source.ReadByte();
            if (probe < 0)
            {
                isLast = true;
            }
            else
            {
                carry = probe;
                isLast = false;
            }
        }
        else
        {
            isLast = true;
        }

        return length;
    }

    protected override void Dispose(bool disposing)
    {
        if (!disposed)
        {
            CryptographicOperations.ZeroMemory(subkey);
            CryptographicOperations.ZeroMemory(associatedData);
            ArrayPool<byte>.Shared.Return(recordBuffer, clearArray: true);
            ArrayPool<byte>.Shared.Return(plainBuffer, clearArray: true);
            disposed = true;
        }

        base.Dispose(disposing);
    }
}
