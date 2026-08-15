using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Nordstein.Core.Common.Random;
using Nordstein.Core.Testing;
using NSubstitute;

namespace Nordstein.Core.Common.Tests;

[TestClass]
public sealed class ContainerBuilderExtensionsTests : BaseTest<Module>
{
    [TestMethod]
    public void RegisterStub_WithoutConfigAction_ResolvesConfigurableSubstitute()
    {
        IServiceProvider services = GetServices(builder => builder.RegisterStub<IRandom>());

        var random = services.GetRequiredService<IRandom>();

        // Configuring a return value with NSubstitute only succeeds on a substitute; on a real
        // implementation the last-call setup would throw. That the setup is accepted and observed
        // proves the resolved instance is an NSubstitute fake — with no config action supplied.
        random.String().Returns("stubbed");
        random.String().Should().Be("stubbed");
    }

    [TestMethod]
    public void RegisterStub_WithConfigAction_AppliesConfiguredBehavior()
    {
        IServiceProvider services = GetServices(builder =>
            builder.RegisterStub<IRandom>(fake => fake.String().Returns("configured")));

        var random = services.GetRequiredService<IRandom>();

        // The optional config action must run against the freshly created fake before it is handed
        // to the container, so the behaviour it set up is visible on the resolved instance.
        random.String().Should().Be("configured");
    }

    [TestMethod]
    public void RegisterStub_ResolvedTwice_ReturnsDifferentInstances()
    {
        IServiceProvider services = GetServices(builder => builder.RegisterStub<IRandom>());

        var first = services.GetRequiredService<IRandom>();
        var second = services.GetRequiredService<IRandom>();

        // RegisterStub is documented as InstancePerDependency: every resolve produces a brand-new
        // fake.
        first.Should().NotBeSameAs(second);
    }

    [TestMethod]
    public void RegisterStub_ResolvedTwice_DoesNotShareConfiguredBehavior()
    {
        IServiceProvider services = GetServices(builder => builder.RegisterStub<IRandom>());

        var first = services.GetRequiredService<IRandom>();
        first.String().Returns("only-on-first");

        var second = services.GetRequiredService<IRandom>();

        // Because each resolve is a distinct fake (InstancePerDependency), behaviour configured on
        // one instance is invisible to a later resolve — this is the documented scope gotcha that
        // makes Received() assertions against a freshly resolved stub see nothing. The second fake
        // returns NSubstitute's unconfigured default for string (empty), never the first's setup.
        second.String().Should().NotBe("only-on-first");
    }
}
