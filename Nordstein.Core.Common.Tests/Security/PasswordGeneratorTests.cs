using System.ComponentModel.DataAnnotations;
using Autofac;
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Nordstein.Core.Common.DependencyInjection;
using Nordstein.Core.Common.Security;
using Nordstein.Core.Testing;

namespace Nordstein.Core.Common.Tests.Security;

[TestClass]
public sealed class PasswordGeneratorTests : BaseTest<Module>
{
    [TestMethod]
    public void Generate_DefaultOptions_IsTwentyBase32Characters()
        => Generator().Generate().Should().MatchRegex("^[a-z2-7]{20}$");

    [TestMethod]
    public void Generate_EachCall_DrawsAFreshPassword()
    {
        IPasswordGenerator generator = Generator();

        generator.Generate().Should().NotBe(generator.Generate());
    }

    [TestMethod]
    public void Generate_ManyPasswords_UseTheWholeAlphabet()
    {
        IPasswordGenerator generator = Generator();

        HashSet<char> seen = [.. Enumerable.Range(0, 200).SelectMany(_ => generator.Generate())];

        seen.Should().HaveCount(32);
    }

    [TestMethod]
    [DataRow(0, 20)]
    [DataRow(12, 20)]
    [DataRow(20, 20)]
    [DataRow(21, 21)]
    [DataRow(64, 64)]
    [DataRow(256, 256)]
    public void Generate_PolicyMinimum_TheLengthIsTheLongerOfItAndTwenty(int minLength, int expected)
        => Generator(minLength).Generate().Should().HaveLength(expected).And.MatchRegex("^[a-z2-7]+$");

    [TestMethod]
    [DataRow(0)]
    [DataRow(12)]
    [DataRow(256)]
    public void Generate_AnyMinimum_PassesThePolicyUnchangedByNormalization(int minLength)
    {
        IServiceProvider services = GetServices(builder => builder.RegisterInstance(new PasswordPolicyOptions { MinLength = minLength }));
        var policy = services.GetRequiredService<IPasswordPolicy>();
        string password = services.GetRequiredService<IPasswordGenerator>().Generate();

        string normalized = policy.Normalize(password);
        Action act = () => policy.Check(normalized, ["jordan.doe@example.com"]);

        normalized.Should().Be(password);
        act.Should().NotThrow();
    }

    [TestMethod]
    public void Resolve_InvalidOptions_FailsWithValidation()
    {
        IServiceProvider services = GetServices(builder => builder.RegisterInstance(new PasswordPolicyOptions { MinLength = 257 }));

        Action act = () => services.GetRequiredService<IPasswordGenerator>();

        CauseOf(act.Should().Throw<InvalidOperationException>().Which).Should().BeOfType<ValidationException>();
    }

    [TestMethod]
    public void Container_ResolvesOneInstance()
    {
        IServiceProvider services = GetServices();

        services.GetRequiredService<IPasswordGenerator>().Should().BeSameAs(services.GetRequiredService<IPasswordGenerator>());
        IPasswordGenerator.DefaultLength.Should().Be(20);
    }

    private IPasswordGenerator Generator(int? minLength = null)
        => GetServices(builder =>
            {
                if (minLength is { } min)
                {
                    builder.RegisterInstance(new PasswordPolicyOptions { MinLength = min });
                }
            })
            .GetRequiredService<IPasswordGenerator>();

    /// <summary>
    /// The cause of a failed <c>GetRequiredService</c>: Microsoft's service-provider contract wraps Autofac's
    /// resolution failure in an <see cref="InvalidOperationException"/>.
    /// </summary>
    private static Exception CauseOf(InvalidOperationException thrown)
        => (thrown.InnerException ?? thrown).UnwrapResolutionFailures();
}
