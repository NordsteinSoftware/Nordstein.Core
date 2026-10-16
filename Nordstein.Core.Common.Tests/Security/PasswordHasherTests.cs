using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Nordstein.Core.Common.Security;
using Nordstein.Core.Testing;

namespace Nordstein.Core.Common.Tests.Security;

[TestClass]
public sealed class PasswordHasherTests : BaseTest<Module>
{
    private const string Password = "correct horse battery staple";

    // bytes(range(16)) and its PBKDF2-HMAC-SHA256 output at 1,000 iterations for a non-ASCII
    // password; both are computed outside .NET (see the vector tests) and reused by the
    // malformed-input rows, which never reach the derivation.
    private const string Salt16 = "AAECAwQFBgcICQoLDA0ODw";
    private const string Hash32 = "3NkHSPhtBEJ7V71tycgH0BEWOfIITqysFEFL5iBfsgk";
    private const string Valid = "$pbkdf2-sha256$i=1000$" + Salt16 + "$" + Hash32;

    private IPasswordHasher Hasher => GetServices().GetRequiredService<IPasswordHasher>();

    [TestMethod]
    public void PasswordHasher_Verify_RoundTrips()
    {
        IPasswordHasher hasher = Hasher;

        string encoded = hasher.Hash(Password);

        hasher.Verify(Password, encoded).Should().Be(PasswordVerification.Succeeded);
    }

    [TestMethod]
    public void PasswordHasher_Verify_WrongPassword_Fails()
    {
        IPasswordHasher hasher = Hasher;
        string encoded = hasher.Hash(Password);

        hasher.Verify(Password + "!", encoded).Should().Be(PasswordVerification.Failed);
    }

    [TestMethod]
    public void PasswordHasher_Hash_IsPhcFormatted()
    {
        string encoded = Hasher.Hash(Password);

        encoded.Should().MatchRegex(@"^\$pbkdf2-sha256\$i=600000\$[A-Za-z0-9+/]{22}\$[A-Za-z0-9+/]{43}$");
    }

    [TestMethod]
    public void PasswordHasher_Hash_SaltsEachCall()
    {
        IPasswordHasher hasher = Hasher;

        string first = hasher.Hash(Password);
        string second = hasher.Hash(Password);

        first.Should().NotBe(second);
        first.Split('$')[3].Should().NotBe(second.Split('$')[3], "each hash carries a fresh salt");
    }

    [TestMethod]
    public void PasswordHasher_LowerIterations_NeedsRehash()
    {
        string encoded = Phc(Password, iterations: 1000);

        Hasher.Verify(Password, encoded).Should().Be(PasswordVerification.SucceededRehashNeeded);
    }

    [TestMethod]
    public void PasswordHasher_LowerIterations_WrongPassword_Fails()
    {
        string encoded = Phc(Password, iterations: 1000);

        Hasher.Verify("wrong", encoded).Should().Be(PasswordVerification.Failed);
    }

    [TestMethod]
    public void PasswordHasher_IterationsJustBelowDefault_NeedsRehash()
    {
        string encoded = Phc(Password, iterations: 599_999);

        Hasher.Verify(Password, encoded).Should().Be(PasswordVerification.SucceededRehashNeeded);
    }

    [TestMethod]
    public void PasswordHasher_IterationsAboveDefault_Succeeds()
    {
        // A hash stronger than the default is never downgraded by a rehash.
        string encoded = Phc(Password, iterations: 600_001);

        Hasher.Verify(Password, encoded).Should().Be(PasswordVerification.Succeeded);
    }

    [TestMethod]
    public void PasswordHasher_ShorterSalt_NeedsRehash()
    {
        string encoded = Phc(Password, iterations: 600_000, saltSize: 8);

        Hasher.Verify(Password, encoded).Should().Be(PasswordVerification.SucceededRehashNeeded);
    }

    [TestMethod]
    public void PasswordHasher_ShorterHash_NeedsRehash()
    {
        string encoded = Phc(Password, iterations: 600_000, hashSize: 16);

        Hasher.Verify(Password, encoded).Should().Be(PasswordVerification.SucceededRehashNeeded);
    }

    [TestMethod]
    [DataRow(1, 8, 16, DisplayName = "lowest accepted iterations, salt and hash sizes")]
    [DataRow(1000, 64, 64, DisplayName = "largest accepted salt and hash sizes")]
    public void PasswordHasher_Verify_BoundaryParameters_Accepted(int iterations, int saltSize, int hashSize)
    {
        string encoded = Phc(Password, iterations, saltSize, hashSize);

        IPasswordHasher hasher = Hasher;

        hasher.Verify(Password, encoded).Should().Be(PasswordVerification.SucceededRehashNeeded);
        hasher.Verify("wrong", encoded).Should().Be(PasswordVerification.Failed);
    }

    [TestMethod]
    public void PasswordHasher_Verify_IndependentVector_Succeeds()
    {
        // Computed outside .NET, so a symmetric bug in Hash and Verify cannot hide:
        // python3 -c "import hashlib,base64; s=bytes(range(16)); h=hashlib.pbkdf2_hmac('sha256',
        //   b'correct horse battery staple', s, 600000); b=lambda x: base64.b64encode(x).decode().rstrip('=');
        //   print('\$pbkdf2-sha256\$i=600000\$'+b(s)+'\$'+b(h))"
        const string encoded = "$pbkdf2-sha256$i=600000$AAECAwQFBgcICQoLDA0ODw$7xdxRO7JQgy8EJPSqLNEqSvFBtDU7JwCjdGfgyTYweY";

        IPasswordHasher hasher = Hasher;

        hasher.Verify(Password, encoded).Should().Be(PasswordVerification.Succeeded);
    }

    [TestMethod]
    public void PasswordHasher_Verify_IndependentNonAsciiVector_EncodesUtf8()
    {
        // The password is UTF-8 encoded without normalization (bytes 70c3a4737377c3b6726420f09f9491):
        // python3 -c "import hashlib,base64; s=bytes(range(16)); pw='pässwörd \U0001F511';
        //   h=hashlib.pbkdf2_hmac('sha256', pw.encode('utf-8'), s, 1000);
        //   b=lambda x: base64.b64encode(x).decode().rstrip('='); print('\$pbkdf2-sha256\$i=1000\$'+b(s)+'\$'+b(h))"
        Hasher.Verify("pässwörd \U0001F511", Valid)
            .Should().Be(PasswordVerification.SucceededRehashNeeded);
    }

    [TestMethod]
    public void PasswordHasher_EmptyPassword_RoundTrips()
    {
        // The hasher enforces no password policy; rejecting an empty password is the caller's job.
        IPasswordHasher hasher = Hasher;

        string encoded = hasher.Hash(string.Empty);

        hasher.Verify(string.Empty, encoded).Should().Be(PasswordVerification.Succeeded);
        hasher.Verify(" ", encoded).Should().Be(PasswordVerification.Failed);
    }

    [TestMethod]
    public void PasswordHasher_Verify_DoesNotNormalize()
    {
        // Precomposed U+00E9 versus "e" + combining acute U+0301: normalizing is the caller's choice.
        string encoded = Phc("café", iterations: 1000);

        IPasswordHasher hasher = Hasher;

        hasher.Verify("café", encoded).Should().Be(PasswordVerification.SucceededRehashNeeded);
        hasher.Verify("café", encoded).Should().Be(PasswordVerification.Failed);
    }

    [TestMethod]
    public void PasswordHasher_Verify_LongPassword_IsNotTruncated()
    {
        string password = new('a', 1000);
        string encoded = Phc(password, iterations: 1000);

        IPasswordHasher hasher = Hasher;

        hasher.Verify(password, encoded).Should().Be(PasswordVerification.SucceededRehashNeeded);
        hasher.Verify(new string('a', 999) + "b", encoded).Should().Be(PasswordVerification.Failed);
    }

    [TestMethod]
    public void PasswordHasher_Verify_EmbeddedNul_IsNotTruncated()
    {
        // A C-string boundary anywhere in the chain would cut the password at the NUL.
        string encoded = Phc("abc\0def", iterations: 1000);

        IPasswordHasher hasher = Hasher;

        hasher.Verify("abc\0def", encoded).Should().Be(PasswordVerification.SucceededRehashNeeded);
        hasher.Verify("abc", encoded).Should().Be(PasswordVerification.Failed);
        hasher.Verify("abc\0xyz", encoded).Should().Be(PasswordVerification.Failed);
    }

    [TestMethod]
    public void PasswordHasher_Verify_TamperedParameters_Fail()
    {
        // Each stored value takes part in the check: the last output byte (no prefix-only compare),
        // the salt, and the iteration count.
        byte[] salt = RandomNumberGenerator.GetBytes(16);
        byte[] hash = Derive(Password, salt, 1000, 32);
        byte[] lastByteFlipped = [.. hash];
        lastByteFlipped[^1] ^= 0x01;
        byte[] otherSalt = [.. salt];
        otherSalt[0] ^= 0x01;

        IPasswordHasher hasher = Hasher;

        hasher.Verify(Password, Encode(1000, salt, hash)).Should().Be(PasswordVerification.SucceededRehashNeeded);
        hasher.Verify(Password, Encode(1000, salt, lastByteFlipped)).Should().Be(PasswordVerification.Failed);
        hasher.Verify(Password, Encode(1000, otherSalt, hash)).Should().Be(PasswordVerification.Failed);
        hasher.Verify(Password, Encode(1001, salt, hash)).Should().Be(PasswordVerification.Failed);
    }

    [TestMethod]
    public void PasswordHasher_Hash_IllFormedPassword_Throws()
    {
        // A lone surrogate has no UTF-8 encoding; a lenient encoder would map every one of them
        // to U+FFFD and so make distinct passwords collide.
        Action act = () => Hasher.Hash("pass\uD800word");

        act.Should().Throw<ArgumentException>().Which.ParamName.Should().Be("password");
    }

    [TestMethod]
    public void PasswordHasher_Verify_IllFormedPassword_Fails()
    {
        // The stored hash is of U+FFFD (EF BF BD), the bytes a lenient encoder emits for a lone surrogate.
        string encoded = Phc("�", iterations: 1000);

        IPasswordHasher hasher = Hasher;

        hasher.Verify("�", encoded).Should().Be(PasswordVerification.SucceededRehashNeeded);
        hasher.Verify("\uD800", encoded).Should().Be(PasswordVerification.Failed);
        hasher.Verify("\uDC00", encoded).Should().Be(PasswordVerification.Failed);
    }

    [TestMethod]
    [DataRow("", DisplayName = "empty")]
    [DataRow("$argon2id$v=19$m=65536,t=3,p=4$" + Salt16 + "$" + Hash32, DisplayName = "another algorithm")]
    [DataRow("$PBKDF2-SHA256$i=1000$" + Salt16 + "$" + Hash32, DisplayName = "identifier in upper case")]
    [DataRow("pbkdf2-sha256$i=1000$" + Salt16 + "$" + Hash32, DisplayName = "no leading dollar")]
    [DataRow(" " + Valid, DisplayName = "leading whitespace")]
    [DataRow(Valid + " ", DisplayName = "trailing whitespace")]
    [DataRow(Valid + "\n", DisplayName = "trailing newline")]
    [DataRow("$pbkdf2-sha256$i=1000$" + Salt16, DisplayName = "three segments")]
    [DataRow(Valid + "$", DisplayName = "trailing dollar")]
    [DataRow(Valid + "$" + Hash32, DisplayName = "trailing segment")]
    [DataRow("$pbkdf2-sha256$v=1$i=1000$" + Salt16 + "$" + Hash32, DisplayName = "version segment")]
    [DataRow("$pbkdf2-sha256$1000$" + Salt16 + "$" + Hash32, DisplayName = "bare rounds without i=")]
    [DataRow("$pbkdf2-sha256$rounds=1000$" + Salt16 + "$" + Hash32, DisplayName = "unknown parameter name")]
    [DataRow("$pbkdf2-sha256$i=1000,l=32$" + Salt16 + "$" + Hash32, DisplayName = "extra parameter")]
    [DataRow("$pbkdf2-sha256$i=abc$" + Salt16 + "$" + Hash32, DisplayName = "i=abc")]
    [DataRow("$pbkdf2-sha256$i=$" + Salt16 + "$" + Hash32, DisplayName = "i= without digits")]
    [DataRow("$pbkdf2-sha256$i=0$" + Salt16 + "$" + Hash32, DisplayName = "i=0")]
    [DataRow("$pbkdf2-sha256$i=-1$" + Salt16 + "$" + Hash32, DisplayName = "i=-1")]
    [DataRow("$pbkdf2-sha256$i=+1000$" + Salt16 + "$" + Hash32, DisplayName = "i with a plus sign")]
    [DataRow("$pbkdf2-sha256$i= 1000$" + Salt16 + "$" + Hash32, DisplayName = "i with a space")]
    [DataRow("$pbkdf2-sha256$i=01000$" + Salt16 + "$" + Hash32, DisplayName = "i with a leading zero")]
    [DataRow("$pbkdf2-sha256$i=1e3$" + Salt16 + "$" + Hash32, DisplayName = "i in exponent notation")]
    [DataRow("$pbkdf2-sha256$i=١٠٠٠$" + Salt16 + "$" + Hash32, DisplayName = "i in Arabic-Indic digits")]
    [DataRow("$pbkdf2-sha256$i=1000$not*base64!!!!!!!!!!$" + Hash32, DisplayName = "salt not base64")]
    [DataRow("$pbkdf2-sha256$i=1000$" + Salt16 + "==$" + Hash32, DisplayName = "salt with padding")]
    [DataRow("$pbkdf2-sha256$i=1000$AAECAwQFBgcICQoLDA0ODx$" + Hash32, DisplayName = "salt with non-zero trailing bits")]
    [DataRow("$pbkdf2-sha256$i=1000$AAECAwQFBgcICQoLDA0O_w$" + Hash32, DisplayName = "salt in the URL-safe alphabet")]
    [DataRow("$pbkdf2-sha256$i=1000$AAECAwQFBgcI CQoLDA0ODw$" + Hash32, DisplayName = "salt with inner whitespace")]
    [DataRow("$pbkdf2-sha256$i=1000$AAECAwQFBgcICQoLDA0OD$" + Hash32, DisplayName = "salt of impossible base64 length")]
    [DataRow("$pbkdf2-sha256$i=1000$$" + Hash32, DisplayName = "empty salt")]
    [DataRow("$pbkdf2-sha256$i=1000$" + Salt16 + "$", DisplayName = "empty hash")]
    [DataRow("$pbkdf2-sha256$i=1000$" + Salt16 + "$" + Hash32 + "=", DisplayName = "hash with padding")]
    public void PasswordHasher_Verify_MalformedHash_Throws(string encodedHash)
    {
        Action act = () => Hasher.Verify(Password, encodedHash);

        act.Should().Throw<FormatException>();
    }

    [TestMethod]
    [DataRow("$pbkdf2-sha256$i=10000001$" + Salt16 + "$" + Hash32, DisplayName = "iterations above the cap")]
    [DataRow("$pbkdf2-sha256$i=2147483647$" + Salt16 + "$" + Hash32, DisplayName = "iterations at int.MaxValue")]
    [DataRow("$pbkdf2-sha256$i=99999999999999999999$" + Salt16 + "$" + Hash32, DisplayName = "iterations overflowing int")]
    [DataRow("$pbkdf2-sha256$i=1000$AAAAAAAAAA$" + Hash32, DisplayName = "salt of 7 bytes")]
    [DataRow("$pbkdf2-sha256$i=1000$AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA$" + Hash32, DisplayName = "salt of 65 bytes")]
    [DataRow("$pbkdf2-sha256$i=1000$" + Salt16 + "$AAAAAAAAAAAAAAAAAAAA", DisplayName = "hash of 15 bytes")]
    [DataRow("$pbkdf2-sha256$i=1000$" + Salt16 + "$AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA", DisplayName = "hash of 65 bytes")]
    public void PasswordHasher_Verify_OutOfRangeParameters_Throws(string encodedHash)
    {
        // Out-of-range parameters are refused before any derivation runs: an unbounded iteration
        // count or output length would let one stored row burn minutes of CPU per login, and an
        // output shorter than 16 bytes would let a wrong password match by chance.
        Action act = () => Hasher.Verify(Password, encodedHash);

        act.Should().Throw<FormatException>();
    }

    [TestMethod]
    public void PasswordHasher_Verify_MalformedHash_MessageOmitsInput()
    {
        const string marker = "SensitiveMarkerValue";
        string encodedHash = "$pbkdf2-sha256$i=1000$" + Salt16 + "$" + marker + "$" + marker;

        Action act = () => Hasher.Verify(marker, encodedHash);

        act.Should().Throw<FormatException>()
            .Which.Message.Should().NotContain(marker).And.NotContain(Salt16);
    }

    [TestMethod]
    public void PasswordHasher_Verify_MalformedHashWithIllFormedPassword_Throws()
    {
        // The stored value is checked first: a corrupt row surfaces whatever the password is.
        Action act = () => Hasher.Verify("\uD800", "$pbkdf2-sha256$i=0$" + Salt16 + "$" + Hash32);

        act.Should().Throw<FormatException>();
    }

    [TestMethod]
    public void PasswordHasher_NullArguments_Throw()
    {
        IPasswordHasher hasher = Hasher;

        // WHY: the null-forgiving operator is banned, tests included. A default array element is a
        // genuine null the compiler types as non-null — exactly what a mis-wired caller would pass.
        string missing = (new string[1])[0];

        Action hash = () => hasher.Hash(missing);
        Action verifyPassword = () => hasher.Verify(missing, Valid);
        Action verifyHash = () => hasher.Verify(Password, missing);

        hash.Should().Throw<ArgumentNullException>().Which.ParamName.Should().Be("password");
        verifyPassword.Should().Throw<ArgumentNullException>().Which.ParamName.Should().Be("password");
        verifyHash.Should().Throw<ArgumentNullException>().Which.ParamName.Should().Be("encodedHash");
    }

    [TestMethod]
    public void PasswordHasher_ConcurrentVerify_IsDeterministic()
    {
        IPasswordHasher hasher = Hasher;
        string encoded = Phc(Password, iterations: 1000);
        var results = new PasswordVerification[64];

        Parallel.For(
            0,
            results.Length,
            new ParallelOptions { CancellationToken = CancellationToken },
            i => results[i] = hasher.Verify(i % 2 == 0 ? Password : "wrong", encoded));

        results.Where((_, i) => i % 2 == 0).Should().AllBeEquivalentTo(PasswordVerification.SucceededRehashNeeded);
        results.Where((_, i) => i % 2 == 1).Should().AllBeEquivalentTo(PasswordVerification.Failed);
    }

    [TestMethod]
    public void PasswordHasher_Module_ResolvesOneInstance()
    {
        IServiceProvider services = GetServices();

        var first = services.GetRequiredService<IPasswordHasher>();
        var second = services.GetRequiredService<IPasswordHasher>();

        first.Should().BeSameAs(second);
    }

    [TestMethod]
    public void PasswordVerification_Values_ArePinned()
    {
        // The default value must be the failing one, so an unset result never reads as success.
        default(PasswordVerification).Should().Be(PasswordVerification.Failed);
        ((int)PasswordVerification.Failed).Should().Be(0);
        ((int)PasswordVerification.Succeeded).Should().Be(1);
        ((int)PasswordVerification.SucceededRehashNeeded).Should().Be(2);
    }

    /// <summary>
    /// Builds a PHC string with the BCL directly — independent of the hasher's own encoder — so a
    /// test can choose parameters the hasher would never produce.
    /// </summary>
    private static string Phc(string password, int iterations, int saltSize = 16, int hashSize = 32)
    {
        byte[] salt = RandomNumberGenerator.GetBytes(saltSize);
        return Encode(iterations, salt, Derive(password, salt, iterations, hashSize));
    }

    private static byte[] Derive(string password, byte[] salt, int iterations, int hashSize)
        => Rfc2898DeriveBytes.Pbkdf2(
            Encoding.UTF8.GetBytes(password), salt, iterations, HashAlgorithmName.SHA256, hashSize);

    private static string Encode(int iterations, byte[] salt, byte[] hash)
        => string.Create(
            CultureInfo.InvariantCulture,
            $"$pbkdf2-sha256$i={iterations}${Unpadded(salt)}${Unpadded(hash)}");

    private static string Unpadded(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=');
}
