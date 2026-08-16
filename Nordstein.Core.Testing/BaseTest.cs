using Autofac;
using JetBrains.Annotations;

namespace Nordstein.Core.Testing;

/// <summary>
/// Abstract base class for MSTest tests that need an Autofac DI container. Provides
/// per-test container lifecycle — the container is built in <c>TestInitialize</c> and
/// disposed in <c>TestCleanup</c> — a <see cref="TestContext"/> property, and helpers
/// to register additional services.
/// </summary>
/// <typeparam name="TModule">The Autofac module that wires the system under test.</typeparam>
[TestClass]
public abstract class BaseTest<TModule> where TModule : Autofac.Module, new()
{
    /// <summary>
    /// The MSTest-injected <see cref="Microsoft.VisualStudio.TestTools.UnitTesting.TestContext"/>;
    /// provides access to the test name, run settings, <see cref="CancellationToken"/>, and
    /// properties bag.
    /// </summary>
    public required TestContext TestContext { get; [UsedImplicitly] init; }

    /// <summary>
    /// Cancellation token from the MSTest framework; cancelled when the test times out or is
    /// cancelled by the runner. Pass to every async call in tests.
    /// </summary>
    protected CancellationToken CancellationToken
        => TestContext.CancellationToken;

    [TestInitialize]
    public void Initialize()
    {
        TestContext.Properties["Containers"] = new List<IContainer>();
    }

    [TestCleanup]
    public void Cleanup()
    {
        foreach (IContainer container in GetTestContainers())
        {
            container.Dispose();
        }
    }

    /// <summary>
    /// Override to register additional services for every test in the class. Called by
    /// <see cref="GetServices"/> before building the container.
    /// </summary>
    /// <param name="builder">The Autofac container builder to register services on.</param>
    protected virtual void ConfigureContainer(ContainerBuilder builder)
    {
    }

    /// <summary>
    /// Builds an Autofac container with <typeparamref name="TModule"/> and the core modules,
    /// calls <see cref="ConfigureContainer"/>, then optionally applies additional registrations
    /// from <paramref name="action"/>. The container is automatically disposed during test
    /// cleanup.
    /// </summary>
    /// <param name="action">Optional additional registrations to apply after <see cref="ConfigureContainer"/>.</param>
    /// <returns>The <see cref="IServiceProvider"/> façade over the built container.</returns>
    protected IServiceProvider GetServices(Action<ContainerBuilder>? action = null)
    {
        IContainer container = BuildContainer(builder =>
        {
            ConfigureContainer(builder);
            action?.Invoke(builder);
        });

        // register container in test context so it is disposed in Cleanup
        var containers = GetTestContainers();
        containers.Add(container);

        return container.Resolve<IServiceProvider>();
    }

    /// <summary>
    /// Builds a DI container without registering it for per-test cleanup. Useful from a
    /// static <c>[ClassInitialize]</c> to share an expensive fixture (e.g. seeded data)
    /// across the tests of a class; the caller owns disposal.
    /// </summary>
    protected static IContainer BuildContainer(Action<ContainerBuilder>? action = null)
    {
        ContainerBuilder builder = new ContainerBuilder();
        builder.RegisterModule<Module>();
        builder.RegisterModule<TModule>();
        action?.Invoke(builder);
        return builder.Build();
    }

    /// <summary>
    /// Returns the list of containers registered for disposal in this test. Internal helper
    /// called by <see cref="GetServices"/> and <c>TestCleanup</c>.
    /// </summary>
    private List<IContainer> GetTestContainers()
    {
        if (TestContext.Properties.TryGetValue("Containers", out object? containersObj) &&
            containersObj is List<IContainer> containers)
        {
            return containers;
        }

        throw new InvalidOperationException(
            "TestContext does not contain a list of containers. Ensure that the TestInitialize method is properly setting up the list.");
    }
}