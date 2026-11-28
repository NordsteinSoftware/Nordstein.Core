using AwesomeAssertions;
using Nordstein.Core.Testing.Postgres.Tests.Internal;

namespace Nordstein.Core.Testing.Postgres.Tests;

[TestClass]
public sealed class PostgresValueTypeTests
{
    [TestMethod]
    public void Options_Defaults_AreTwoHundredConnectionsAndFiveMinutes()
    {
        var options = new PostgresServerOptions { Image = "postgres:17.2", RequireVariable = "EXAMPLE_REQUIRE_DOCKER_TESTS" };

        options.Validate();

        options.MaxConnections.Should().Be(200);
        options.StartTimeout.Should().Be(TimeSpan.FromMinutes(5));
    }

    [TestMethod]
    public void Options_UnusableValue_FailsValidation()
    {
        var valid = new PostgresServerOptions { Image = "postgres:17.2", RequireVariable = "EXAMPLE_REQUIRE_DOCKER_TESTS" };

        ((Action)(valid with { Image = "" }).Validate).Should().Throw<InvalidOperationException>().WithMessage("*Image*");
        ((Action)(valid with { RequireVariable = " " }).Validate).Should().Throw<InvalidOperationException>().WithMessage("*RequireVariable*");
        ((Action)(valid with { MaxConnections = 0 }).Validate).Should().Throw<InvalidOperationException>().WithMessage("*MaxConnections*");
        ((Action)(valid with { StartTimeout = TimeSpan.Zero }).Validate).Should().Throw<InvalidOperationException>().WithMessage("*StartTimeout*");
    }

    [TestMethod]
    public void Template_ValidName_IsKept()
        => new PostgresTemplate("tpl_migrated_2", (_, _) => Task.CompletedTask).Name.Should().Be("tpl_migrated_2");

    [TestMethod]
    [DataRow("")]
    [DataRow("Template")]
    [DataRow("1template")]
    [DataRow("_template")]
    [DataRow("tpl-x")]
    [DataRow("tpl; drop database postgres")]
    [DataRow("a2345678901234567890123456789012")]
    public void Template_InvalidName_Throws(string name)
    {
        Action act = () => _ = new PostgresTemplate(name, (_, _) => Task.CompletedTask);

        act.Should().Throw<ArgumentException>().WithParameterName("name");
    }

    [TestMethod]
    public void Template_NullArgument_Throws()
    {
        Action name = () => _ = new PostgresTemplate(Missing.Reference<string>(), (_, _) => Task.CompletedTask);
        Action initialize = () => _ = new PostgresTemplate("tpl", Missing.Reference<Func<string, CancellationToken, Task>>());

        name.Should().Throw<ArgumentNullException>();
        initialize.Should().Throw<ArgumentNullException>();
    }
}
