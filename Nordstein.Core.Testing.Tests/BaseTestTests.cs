using Autofac;
using AwesomeAssertions;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Nordstein.Core.Common.Lifecycle;
using Nordstein.Core.Testing.Tests.Internal;
using NSubstitute;

namespace Nordstein.Core.Testing.Tests;

/// <summary>
/// <see cref="BaseTest{TModule}"/> and the harness module it stacks under every container: a fresh
/// container per call, disposed at cleanup; the harness registrations every test can rely on; the
/// composition order; and a loud failure when cleanup runs without its initializer.
/// </summary>
[TestClass]
public sealed class BaseTestTests : BaseTest<Module>
{
    [TestMethod]
    public void GetServices_BuildsAFreshContainerEveryCall()
    {
        IServiceProvider first = GetServices(builder => builder.RegisterType<Marker>().SingleInstance());
        IServiceProvider second = GetServices(builder => builder.RegisterType<Marker>().SingleInstance());

        first.GetRequiredService<Marker>().Should().NotBeSameAs(second.GetRequiredService<Marker>());
    }

    [TestMethod]
    public void GetServices_ResolvesTheServiceProviderOverItsOwnContainer()
    {
        IServiceProvider services = GetServices(builder => builder.RegisterType<Marker>().SingleInstance());

        IServiceProvider resolved = services.GetRequiredService<IServiceProvider>();

        resolved.GetRequiredService<Marker>().Should().BeSameAs(services.GetRequiredService<Marker>());
    }

    [TestMethod]
    public void HarnessModule_RegistersAHostEnvironmentRootedInAnExistingTemporaryDirectory()
    {
        IServiceProvider services = GetServices();

        IHostEnvironment environment = services.GetRequiredService<IHostEnvironment>();

        Directory.Exists(environment.ContentRootPath).Should().BeTrue();
        services.GetRequiredService<IHostEnvironment>().Should().BeSameAs(environment, "one environment per container");
    }

    [TestMethod]
    public void HarnessModule_RegistersLoggingAndAnEphemeralDataProtectionKeyRing()
    {
        IServiceProvider services = GetServices();

        services.GetRequiredService<ILogger<BaseTestTests>>().Should().NotBeNull();
        IDataProtector protector = services.GetRequiredService<IDataProtectionProvider>().CreateProtector("example-purpose");
        protector.Unprotect(protector.Protect("secret")).Should().Be("secret");
    }

    [TestMethod]
    public void GetServices_AppliesTheClassOverrideBeforeThePerCallAction()
    {
        var test = new OrderedRegistrations { TestContext = TestContext };
        test.Initialize();
        try
        {
            IServiceProvider services = test.Services(builder => builder.RegisterInstance("per-call"));

            services.GetRequiredService<string>().Should().Be("per-call", "the per-call action runs last, so it wins");
            services.GetRequiredService<IEnumerable<string>>().Should().Equal("class", "per-call");
        }
        finally
        {
            test.Cleanup();
        }
    }

    [TestMethod]
    public void Cleanup_DisposesEveryContainerTheTestBuilt()
    {
        var test = new OrderedRegistrations { TestContext = Context() };
        test.Initialize();
        IServiceProvider first = test.Services();
        IServiceProvider second = test.Services();
        ITempDirectory directory = first.GetTempDirectory("cleanup");

        test.Cleanup();

        Action resolveFirst = () => first.GetRequiredService<string>();
        Action resolveSecond = () => second.GetRequiredService<string>();
        resolveFirst.Should().Throw<ObjectDisposedException>();
        resolveSecond.Should().Throw<ObjectDisposedException>();
        Directory.Exists(directory.Path).Should().BeFalse("the container deletes the temporary directories it created");
    }

    [TestMethod]
    public void BuildContainer_IsNotTrackedForCleanup()
    {
        var test = new OrderedRegistrations { TestContext = Context() };
        test.Initialize();
        using IContainer container = test.Untracked();

        test.Cleanup();

        container.Resolve<IServiceProvider>().Should().NotBeNull("the caller owns an untracked container");
    }

    [TestMethod]
    public void GetServices_WithoutTheInitializer_FailsLoudly()
    {
        var test = new OrderedRegistrations { TestContext = Context() };

        Action services = () => test.Services();
        Action cleanup = test.Cleanup;

        services.Should().Throw<InvalidOperationException>().WithMessage("*TestInitialize*");
        cleanup.Should().Throw<InvalidOperationException>().WithMessage("*TestInitialize*");
    }

    [TestMethod]
    public void Cleanup_WhenTheContainerListWasReplaced_FailsLoudly()
    {
        TestContext context = Context();
        var test = new OrderedRegistrations { TestContext = context };
        test.Initialize();
        context.Properties["Containers"] = "not a list";

        Action cleanup = test.Cleanup;

        cleanup.Should().Throw<InvalidOperationException>().WithMessage("*does not contain a list of containers*");
    }

    [TestMethod]
    public void CancellationToken_IsTheTestContextsToken()
    {
        var test = new OrderedRegistrations { TestContext = TestContext };

        test.Token.Should().Be(TestContext.CancellationToken);
    }

    private static TestContext Context()
    {
        TestContext context = Substitute.For<TestContext>();
        context.Properties.Returns(new Dictionary<string, object?>());
        return context;
    }
}
