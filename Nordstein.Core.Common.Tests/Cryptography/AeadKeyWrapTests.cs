using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Nordstein.Core.Common.Cryptography;
using Nordstein.Core.Testing;

namespace Nordstein.Core.Common.Tests.Cryptography;

[TestClass]
public sealed class AeadKeyWrapTests : BaseTest<Module>
{
    private static readonly byte[] Kek = "0123456789abcdef0123456789abcdef"u8.ToArray();
    private static readonly byte[] DataKey = "a-32-byte-data-key-to-protect!!!"u8.ToArray();
    private static readonly byte[] Aad = "audience=company;version=1"u8.ToArray();

    private IAeadKeyWrap KeyWrap => GetServices().GetRequiredService<IAeadKeyWrap>();

    [TestMethod]
    public void WrapThenUnwrap_RoundTrips()
    {
        IAeadKeyWrap keyWrap = KeyWrap;

        WrappedKey wrapped = keyWrap.Wrap(Kek, DataKey, Aad);
        byte[] unwrapped = keyWrap.Unwrap(Kek, wrapped, Aad);

        unwrapped.Should().Equal(DataKey);
    }

    [TestMethod]
    public void Wrap_ProducesDistinctNoncesPerCall()
    {
        IAeadKeyWrap keyWrap = KeyWrap;

        WrappedKey first = keyWrap.Wrap(Kek, DataKey, Aad);
        WrappedKey second = keyWrap.Wrap(Kek, DataKey, Aad);

        first.Nonce.ToArray().Should().NotEqual(second.Nonce.ToArray());
        first.Ciphertext.ToArray().Should().NotEqual(second.Ciphertext.ToArray());
    }

    [TestMethod]
    public void Unwrap_WithWrongKek_ThrowsAuthenticationFailed()
    {
        IAeadKeyWrap keyWrap = KeyWrap;
        WrappedKey wrapped = keyWrap.Wrap(Kek, DataKey, Aad);
        byte[] wrongKek = "fedcba9876543210fedcba9876543210"u8.ToArray();

        Action act = () => keyWrap.Unwrap(wrongKek, wrapped, Aad);

        act.Should().Throw<AeadAuthenticationFailedException>();
    }

    [TestMethod]
    public void Unwrap_WithDifferentAssociatedData_ThrowsAuthenticationFailed()
    {
        IAeadKeyWrap keyWrap = KeyWrap;
        WrappedKey wrapped = keyWrap.Wrap(Kek, DataKey, Aad);

        Action act = () => keyWrap.Unwrap(Kek, wrapped, "audience=company;version=2"u8.ToArray());

        act.Should().Throw<AeadAuthenticationFailedException>();
    }

    [TestMethod]
    public void Unwrap_WithTamperedCiphertext_ThrowsAuthenticationFailed()
    {
        IAeadKeyWrap keyWrap = KeyWrap;
        WrappedKey wrapped = keyWrap.Wrap(Kek, DataKey, Aad);
        byte[] tampered = wrapped.Ciphertext.ToArray();
        tampered[0] ^= 0xFF;

        Action act = () => keyWrap.Unwrap(Kek, wrapped with { Ciphertext = tampered }, Aad);

        act.Should().Throw<AeadAuthenticationFailedException>();
    }

    [TestMethod]
    public void Wrap_WithWrongKekLength_ThrowsArgumentException()
    {
        IAeadKeyWrap keyWrap = KeyWrap;

        Action act = () => keyWrap.Wrap(new byte[16], DataKey, Aad);

        act.Should().Throw<ArgumentException>();
    }

    [TestMethod]
    public void Unwrap_WithNullWrappedKey_ThrowsArgumentNullException()
    {
        IAeadKeyWrap keyWrap = KeyWrap;

        Action act = () => keyWrap.Unwrap(Kek, null!, Aad);

        act.Should().Throw<ArgumentNullException>();
    }

    [TestMethod]
    public void WrapThenUnwrap_WithEmptyAssociatedData_RoundTrips()
    {
        IAeadKeyWrap keyWrap = KeyWrap;

        WrappedKey wrapped = keyWrap.Wrap(Kek, DataKey, ReadOnlySpan<byte>.Empty);
        byte[] unwrapped = keyWrap.Unwrap(Kek, wrapped, ReadOnlySpan<byte>.Empty);

        unwrapped.Should().Equal(DataKey);
    }
}
