using Autofac;
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Nordstein.Core.Common.Security;
using Nordstein.Core.Testing;
using NSubstitute;

namespace Nordstein.Core.Common.Tests.Security;

[TestClass]
public sealed class DummyPasswordVerifierTests : BaseTest<Module>
{
    private const string DummyHash = "$pbkdf2-sha256$i=600000$dummy$dummy";

    [TestMethod]
    public void Resolve_HashesOneUnknownPassword()
    {
        IPasswordHasher hasher = Hasher();

        _ = Verifier(hasher);

        hasher.Received(1).Hash(Arg.Is<string>(password => password.Length == 64 && password.All(char.IsAsciiHexDigitUpper)));
        hasher.DidNotReceive().Verify(Arg.Any<string>(), Arg.Any<string>());
    }

    [TestMethod]
    public void Resolve_TwoContainers_DrawDifferentUnknownPasswords()
    {
        IPasswordHasher first = Hasher();
        IPasswordHasher second = Hasher();

        _ = Verifier(first);
        _ = Verifier(second);

        string firstPassword = first.ReceivedCalls().Single().GetArguments()[0] as string ?? throw new InvalidOperationException("no password");
        string secondPassword = second.ReceivedCalls().Single().GetArguments()[0] as string ?? throw new InvalidOperationException("no password");
        firstPassword.Should().NotBe(secondPassword);
    }

    [TestMethod]
    [DataRow("a password")]
    [DataRow("")]
    [DataRow("\U0001F600 paired surrogates")]
    public void Verify_Password_VerifiesItAgainstTheDummyHashOnce(string password)
    {
        IPasswordHasher hasher = Hasher();
        IDummyPasswordVerifier verifier = Verifier(hasher);

        verifier.Verify(password);

        hasher.Received(1).Verify(password, DummyHash);
        hasher.Received(1).Verify(Arg.Any<string>(), Arg.Any<string>());
    }

    [TestMethod]
    public void Verify_Null_VerifiesTheEmptyString()
    {
        IPasswordHasher hasher = Hasher();
        IDummyPasswordVerifier verifier = Verifier(hasher);

        verifier.Verify(null);

        hasher.Received(1).Verify(string.Empty, DummyHash);
    }

    [TestMethod]
    [DataRow(0)]
    [DataRow(1)]
    [DataRow(2)]
    [DataRow(3)]
    public void Verify_UnpairedSurrogate_VerifiesTheEmptyStringSoTheDerivationRuns(int sample)
    {
        // Attribute arguments are stored as UTF-8, which cannot carry an unpaired surrogate: build them here.
        string password = sample switch
        {
            0 => "a\uD800b",
            1 => "\uDC00",
            2 => "trailing\uD83D",
            _ => "\uDE00\uD83D",
        };
        IPasswordHasher hasher = Hasher();
        IDummyPasswordVerifier verifier = Verifier(hasher);

        verifier.Verify(password);

        hasher.Received(1).Verify(string.Empty, DummyHash);
        hasher.Received(1).Verify(Arg.Any<string>(), Arg.Any<string>());
    }

    [TestMethod]
    public void Verify_HasherReportsSuccess_StillReturnsNothingToActOn()
    {
        IPasswordHasher hasher = Hasher();
        hasher.Verify(Arg.Any<string>(), Arg.Any<string>()).Returns(PasswordVerification.Succeeded);
        IDummyPasswordVerifier verifier = Verifier(hasher);

        Action act = () => verifier.Verify("anything");

        act.Should().NotThrow();
    }

    [TestMethod]
    public void Verify_RealHasher_RunsTheDerivationWithoutThrowing()
    {
        IServiceProvider services = GetServices();
        var verifier = services.GetRequiredService<IDummyPasswordVerifier>();

        Action act = () =>
        {
            verifier.Verify("a wrong password");
            verifier.Verify(null);
        };

        act.Should().NotThrow();
        services.GetRequiredService<IDummyPasswordVerifier>().Should().BeSameAs(verifier);
    }

    private static IPasswordHasher Hasher()
    {
        IPasswordHasher hasher = Substitute.For<IPasswordHasher>();
        hasher.Hash(Arg.Any<string>()).Returns(DummyHash);
        return hasher;
    }

    private IDummyPasswordVerifier Verifier(IPasswordHasher hasher)
        => GetServices(builder => builder.RegisterInstance(hasher).As<IPasswordHasher>())
            .GetRequiredService<IDummyPasswordVerifier>();
}
