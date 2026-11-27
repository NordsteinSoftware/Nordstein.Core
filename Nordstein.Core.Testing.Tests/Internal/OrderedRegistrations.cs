using Autofac;

namespace Nordstein.Core.Testing.Tests.Internal;

/// <summary>
/// A <see cref="BaseTest{TModule}"/> driven by hand (it is not a test class): its class-level
/// override registers <c>"class"</c>, and its members expose the protected helpers.
/// </summary>
internal sealed class OrderedRegistrations : BaseTest<Module>
{
    public CancellationToken Token => CancellationToken;

    public IServiceProvider Services(Action<ContainerBuilder>? action = null) => GetServices(action);

    public IContainer Untracked() => BuildContainer();

    protected override void ConfigureContainer(ContainerBuilder builder) => builder.RegisterInstance("class");
}
