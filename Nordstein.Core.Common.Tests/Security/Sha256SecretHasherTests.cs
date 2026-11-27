using Autofac;
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Nordstein.Core.Common.Security;
using Nordstein.Core.Common.Security.Internal;
using Nordstein.Core.Testing;

namespace Nordstein.Core.Common.Tests.Security;

/// <summary>
/// Core's default <see cref="ISecretHasher"/>: upper-case hex SHA-256 over the UTF-8 bytes, and the
/// module registration that lets a product's own hasher win.
/// </summary>
[TestClass]
public sealed class Sha256SecretHasherTests : BaseTest<Module>
{
    private ISecretHasher Hasher => GetServices().GetRequiredService<ISecretHasher>();

    [TestMethod]
    public void Hash_Abc_IsUpperHexSha256()
        => Hasher.Hash("abc").Should().Be("BA7816BF8F01CFEA414140DE5DAE2223B00361A396177A9CB410FF61F20015AD");

    [TestMethod]
    public void Hash_EmptyString_IsTheSha256OfNothing()
        => Hasher.Hash(string.Empty).Should().Be("E3B0C44298FC1C149AFBF4C8996FB92427AE41E4649B934CA495991B7852B855");

    [TestMethod]
    public void Hash_NonAscii_HashesTheUtf8Bytes()
        // SHA-256 of the UTF-8 bytes C3 A4 (the single character U+00E4), computed outside .NET.
        => Hasher.Hash("\u00e4").Should().Be("33E6D73FEE82904C8D7AFB78DE1154D1E8DC2A0EDB08120E63DF5B9385C2D9CC");

    [TestMethod]
    public void Hash_SameInput_IsDeterministicSixtyFourUpperHexCharacters()
    {
        ISecretHasher hasher = Hasher;

        string first = hasher.Hash("a high-entropy secret");
        string second = hasher.Hash("a high-entropy secret");

        first.Should().Be(second).And.MatchRegex("^[0-9A-F]{64}$");
        Convert.FromHexString(first).Should().HaveCount(32);
    }

    [TestMethod]
    public void Hash_Null_Throws()
    {
        Func<string> act = () => Hasher.Hash(Unassigned());

        act.Should().Throw<ArgumentNullException>().WithParameterName("value");
    }

    [TestMethod]
    public void Hasher_Resolved_IsOneSharedInstance()
    {
        IServiceProvider services = GetServices();

        services.GetRequiredService<ISecretHasher>().Should().BeSameAs(services.GetRequiredService<ISecretHasher>());
    }

    [TestMethod]
    public void Module_Default_IsSha256()
        => Hasher.Should().BeOfType<Sha256SecretHasher>();

    [TestMethod]
    public void Module_ProductRegisteredFirst_KeepsProductHasher()
    {
        var product = Substitute.For<ISecretHasher>();
        var builder = new ContainerBuilder();
        builder.RegisterInstance(product).As<ISecretHasher>();
        builder.RegisterModule<Common.Module>();

        using IContainer container = builder.Build();

        container.Resolve<ISecretHasher>().Should().BeSameAs(product);
    }

    [TestMethod]
    public void Module_ProductRegisteredAfter_WinsAsTheLastRegistration()
    {
        var product = Substitute.For<ISecretHasher>();
        var builder = new ContainerBuilder();
        builder.RegisterModule<Common.Module>();
        builder.RegisterInstance(product).As<ISecretHasher>();

        using IContainer container = builder.Build();

        container.Resolve<ISecretHasher>().Should().BeSameAs(product);
    }

    // An unassigned element of a string array: a null the nullable analysis does not flag, which
    // the hasher must still reject.
    private static string Unassigned() => (new string[1])[0];
}
