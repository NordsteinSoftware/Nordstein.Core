using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Nordstein.Core.Common.Cryptography;
using Nordstein.Core.Testing;

namespace Nordstein.Core.Common.Tests.Cryptography;

[TestClass]
public sealed class AeadStreamCodecTests : BaseTest<Module>
{
    private static readonly byte[] Key = "root-key-material-32-bytes-long!!"u8.ToArray();
    private static readonly byte[] Info = "context"u8.ToArray();
    private static readonly byte[] Aad = "an-opaque-header"u8.ToArray();
    private static readonly ChunkParameters SmallChunks = new() { ChunkSize = 16 };

    private IAeadStreamCodec Codec => GetServices().GetRequiredService<IAeadStreamCodec>();

    [DataTestMethod]
    [DataRow(0)]
    [DataRow(1)]
    [DataRow(15)]
    [DataRow(16)]
    [DataRow(17)]
    [DataRow(31)]
    [DataRow(32)]
    [DataRow(48)]
    [DataRow(100)]
    public async Task EncryptThenDecrypt_AcrossChunkBoundaries_RoundTrips(int size)
    {
        IAeadStreamCodec codec = Codec;
        byte[] salt = codec.NewSalt();
        byte[] plaintext = Payload(size);

        byte[] ciphertext = await Encrypt(codec, plaintext, salt, Aad);
        byte[] roundTripped = Decrypt(codec, ciphertext, Key, salt, Aad);

        roundTripped.Should().Equal(plaintext);
    }

    [TestMethod]
    public async Task Encrypt_ProducesExpectedCiphertextLength()
    {
        IAeadStreamCodec codec = Codec;
        byte[] salt = codec.NewSalt();

        byte[] ciphertext = await Encrypt(codec, Payload(40), salt, Aad);

        // 40 bytes over 16-byte chunks => 3 chunks (16, 16, 8), each carrying a 16-byte tag.
        ciphertext.Length.Should().Be(40 + (3 * 16));
    }

    [TestMethod]
    public async Task Decrypt_WithTamperedByte_ThrowsAuthenticationFailed()
    {
        IAeadStreamCodec codec = Codec;
        byte[] salt = codec.NewSalt();
        byte[] ciphertext = await Encrypt(codec, Payload(40), salt, Aad);
        ciphertext[20] ^= 0xFF;

        Action act = () => Decrypt(codec, ciphertext, Key, salt, Aad);

        act.Should().Throw<AeadAuthenticationFailedException>();
    }

    [TestMethod]
    public async Task Decrypt_WithFinalChunkRemoved_ThrowsTruncated()
    {
        IAeadStreamCodec codec = Codec;
        byte[] salt = codec.NewSalt();
        // Exactly two full chunks => two records of (16 + 16) bytes.
        byte[] ciphertext = await Encrypt(codec, Payload(32), salt, Aad);
        byte[] truncated = ciphertext[..(ciphertext.Length - (16 + 16))];

        Action act = () => Decrypt(codec, truncated, Key, salt, Aad);

        act.Should().Throw<CiphertextTruncatedException>();
    }

    [TestMethod]
    public async Task Decrypt_WithWrongKey_ThrowsAuthenticationFailed()
    {
        IAeadStreamCodec codec = Codec;
        byte[] salt = codec.NewSalt();
        byte[] ciphertext = await Encrypt(codec, Payload(40), salt, Aad);
        byte[] wrongKey = "a-different-root-key-of-any-len!!"u8.ToArray();

        Action act = () => Decrypt(codec, ciphertext, wrongKey, salt, Aad);

        act.Should().Throw<AeadAuthenticationFailedException>();
    }

    [TestMethod]
    public async Task Decrypt_WithWrongAssociatedData_ThrowsAuthenticationFailed()
    {
        IAeadStreamCodec codec = Codec;
        byte[] salt = codec.NewSalt();
        byte[] ciphertext = await Encrypt(codec, Payload(20), salt, Aad);

        Action act = () => Decrypt(codec, ciphertext, Key, salt, "different-header"u8.ToArray());

        act.Should().Throw<AeadAuthenticationFailedException>();
    }

    [TestMethod]
    public async Task Decrypt_WithWrongSalt_ThrowsAuthenticationFailed()
    {
        IAeadStreamCodec codec = Codec;
        byte[] salt = codec.NewSalt();
        byte[] otherSalt = codec.NewSalt();
        byte[] ciphertext = await Encrypt(codec, Payload(20), salt, Aad);

        Action act = () => Decrypt(codec, ciphertext, Key, otherSalt, Aad);

        act.Should().Throw<AeadAuthenticationFailedException>();
    }

    [TestMethod]
    public void NewSalt_ReturnsDistinctValuesOfExpectedLength()
    {
        IAeadStreamCodec codec = Codec;

        byte[] first = codec.NewSalt();
        byte[] second = codec.NewSalt();

        first.Should().HaveCount(codec.SaltSizeInBytes);
        first.Should().NotEqual(second);
    }

    [TestMethod]
    public async Task Encrypt_WithWrongSaltLength_ThrowsArgumentException()
    {
        IAeadStreamCodec codec = Codec;
        using var source = new MemoryStream(Payload(10));
        using var destination = new MemoryStream();

        Func<Task> act = () => codec.EncryptAsync(source, destination, Key, new byte[8], Info, Aad);

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [TestMethod]
    public async Task Encrypt_WithNullPlaintext_ThrowsArgumentNullException()
    {
        IAeadStreamCodec codec = Codec;
        using var destination = new MemoryStream();

        Func<Task> act = () => codec.EncryptAsync(null!, destination, Key, codec.NewSalt(), Info, Aad);

        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    private static async Task<byte[]> Encrypt(IAeadStreamCodec codec, byte[] plaintext, byte[] salt, byte[] aad)
    {
        using var source = new MemoryStream(plaintext);
        using var destination = new MemoryStream();
        await codec.EncryptAsync(source, destination, Key, salt, Info, aad, SmallChunks);
        return destination.ToArray();
    }

    private static byte[] Decrypt(IAeadStreamCodec codec, byte[] ciphertext, byte[] key, byte[] salt, byte[] aad)
    {
        using var source = new MemoryStream(ciphertext);
        using Stream plaintext = codec.OpenRead(source, key, salt, Info, aad, SmallChunks);
        using var output = new MemoryStream();
        plaintext.CopyTo(output);
        return output.ToArray();
    }

    private static byte[] Payload(int size)
    {
        byte[] payload = new byte[size];
        for (int i = 0; i < size; i++)
        {
            payload[i] = (byte)(i % 251);
        }

        return payload;
    }
}
