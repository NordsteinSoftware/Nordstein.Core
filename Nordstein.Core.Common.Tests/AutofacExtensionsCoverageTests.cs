using Autofac;
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Nordstein.Core.Common.DependencyInjection;

namespace Nordstein.Core.Common.Tests;

/// <summary>
/// Covers the extension-method surface of <see cref="AutofacExtensions"/> that
/// <see cref="AutofacExtensionsTests"/> does not: implementation discovery, the container-dispose
/// hook, and the keyed-descriptor branch of the populate dedupe.
/// </summary>
[TestClass]
public sealed class AutofacExtensionsCoverageTests
{
    private interface IWidget;

    private abstract class WidgetBase : IWidget;

    private sealed class RedWidget : IWidget;

    private sealed class BlueWidget : WidgetBase;

    [TestMethod]
    public void GetImplementations_ReturnsConcreteTypesAssignableToTheInterface()
    {
        IReadOnlyCollection<Type> implementations = typeof(IWidget).GetImplementations();

        implementations.Should().Contain(typeof(RedWidget));
        implementations.Should().Contain(typeof(BlueWidget));
    }

    [TestMethod]
    public void GetImplementations_ExcludesInterfacesAndAbstractTypes()
    {
        IReadOnlyCollection<Type> implementations = typeof(IWidget).GetImplementations();

        implementations.Should().NotContain(typeof(IWidget));
        implementations.Should().NotContain(typeof(WidgetBase));
    }

    [TestMethod]
    public void GetImplementations_WithExplicitAssemblyHavingNoImplementations_ReturnsEmpty()
    {
        // System.Private.CoreLib (object's assembly) contains no IWidget implementations, so passing
        // it explicitly exercises the branch where the assembly argument is supplied rather than
        // defaulted to the interface's own assembly.
        IReadOnlyCollection<Type> implementations =
            typeof(IWidget).GetImplementations(typeof(object).Assembly);

        implementations.Should().BeEmpty();
    }

    [TestMethod]
    public void OnDispose_InvokesTheActionWhenTheContainerIsDisposed()
    {
        var disposed = false;
        var builder = new ContainerBuilder();
        builder.OnDispose(() => disposed = true);
        IContainer container = builder.Build();

        disposed.Should().BeFalse("the action must not run before the container is disposed");
        container.Dispose();

        disposed.Should().BeTrue();
    }

    [TestMethod]
    public void RegisterServiceCollection_WithKeyedDescriptor_PopulatesItUntouched()
    {
        // Keyed descriptors throw on the non-keyed accessors the dedupe compares, so it skips them
        // and populates them exactly as written.
        var builder = new ContainerBuilder();
        builder.RegisterServiceCollection(services =>
            services.AddKeyedSingleton<IWidget, RedWidget>("red"));

        using IContainer container = builder.Build();

        var provider = container.Resolve<IServiceProvider>();
        provider.GetRequiredKeyedService<IWidget>("red").Should().BeOfType<RedWidget>();
    }
}
