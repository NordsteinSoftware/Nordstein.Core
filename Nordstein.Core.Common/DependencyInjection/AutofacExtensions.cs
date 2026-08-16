using System.Reflection;
using Autofac;
using Autofac.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;
using Nordstein.Core.Common.Lifecycle;

namespace Nordstein.Core.Common.DependencyInjection;

public static class AutofacExtensions
{
    private const string PopulatedDescriptorsKey = "Nordstein.Core.Common.ServiceCollection.PopulatedDescriptors";

    /// <summary>
    /// Populates an <see cref="IServiceCollection"/> configured by <paramref name="config"/> into
    /// the Autofac container, deduplicating type-based descriptors that an earlier call already registered.
    /// </summary>
    /// <param name="builder">The Autofac container builder to register services into.</param>
    /// <param name="config">
    /// A delegate that populates an <see cref="IServiceCollection"/> with the desired service
    /// registrations. Invoked immediately.
    /// </param>
    /// <remarks>
    /// Framework extension methods (e.g. <c>AddHttpClient</c>, <c>AddLogging</c>) add shared
    /// plumbing via <c>TryAdd</c>/<c>TryAddEnumerable</c>, which deduplicates only within a single
    /// <see cref="IServiceCollection"/>. When multiple modules each call such methods, each call
    /// builds a fresh collection and the plumbing is re-added every time, producing duplicate
    /// Autofac registrations. This method tracks previously populated type-based descriptors and
    /// removes exact duplicates before populating, preventing the "four logging handlers" class of
    /// problem.
    /// </remarks>
    public static void RegisterServiceCollection(this ContainerBuilder builder, Action<IServiceCollection> config)
    {
        var services = new ServiceCollection();
        config(services);
        DropAlreadyPopulated(builder, services);
        builder.Populate(services);
    }

    /// <summary>
    /// Removes descriptors that an earlier <see cref="RegisterServiceCollection"/> call already
    /// populated into this container, so registering the same concrete implementation twice does not
    /// leave two copies behind.
    /// </summary>
    /// <remarks>
    /// Framework extension methods share their plumbing through <c>TryAdd</c>/<c>TryAddEnumerable</c>,
    /// which dedupes only within *one* <see cref="IServiceCollection"/>. Every call here builds a
    /// fresh collection, so each one re-adds that plumbing and <c>Populate</c> faithfully registers
    /// all of it. Four modules calling <c>AddHttpClient</c> therefore put four
    /// <c>IHttpMessageHandlerBuilderFilter</c>s in the container, and the logging handler each one
    /// contributes wrapped every outgoing request — so a single upstream LLM call was logged four
    /// times, on the hottest path in the system (#451).
    ///
    /// Only type-based registrations are compared: an identical (service, implementation, lifetime)
    /// triple can never mean two *different* things, whereas instance- and factory-based descriptors
    /// are opaque and are always populated as written. Genuine multi-registrations of one service
    /// (the point of <c>IEnumerable&lt;T&gt;</c> resolution) use distinct implementation types and are
    /// untouched.
    /// </remarks>
    private static void DropAlreadyPopulated(ContainerBuilder builder, IServiceCollection services)
    {
        if (!builder.Properties.TryGetValue(PopulatedDescriptorsKey, out object? stored)
            || stored is not HashSet<(Type Service, Type Implementation, ServiceLifetime Lifetime)> populated)
        {
            populated = [];
            builder.Properties[PopulatedDescriptorsKey] = populated;
        }

        for (int i = services.Count - 1; i >= 0; i--)
        {
            ServiceDescriptor descriptor = services[i];

            // Keyed descriptors throw on the non-keyed accessors; they are rare and always explicit,
            // so leave them alone rather than reaching for their keyed counterparts.
            if (descriptor.IsKeyedService || descriptor.ImplementationType is not { } implementation)
            {
                continue;
            }

            if (!populated.Add((descriptor.ServiceType, implementation, descriptor.Lifetime)))
            {
                services.RemoveAt(i);
            }
        }
    }

    /// <summary>
    /// Returns all concrete, non-abstract types in <paramref name="assembly"/> that implement
    /// or extend <paramref name="type"/>.
    /// </summary>
    /// <param name="type">The interface or base type to search for implementations of.</param>
    /// <param name="assembly">
    /// The assembly to scan. When <c>null</c>, defaults to the assembly that declares
    /// <paramref name="type"/>.
    /// </param>
    /// <returns>
    /// A read-only collection of concrete types assignable to <paramref name="type"/>,
    /// excluding interfaces and abstract classes.
    /// </returns>
    public static IReadOnlyCollection<Type> GetImplementations(
        this Type type,
        Assembly? assembly = null)
    {
        assembly ??= type.Assembly;
        return assembly
            .GetTypes()
            .Where(t => type.IsAssignableFrom(t) && t is { IsInterface: false, IsAbstract: false })
            .ToArray();
    }

    /// <summary>
    /// Registers an <paramref name="action"/> that is invoked when the Autofac container is disposed.
    /// </summary>
    /// <param name="builder">The container builder to register the cleanup action on.</param>
    /// <param name="action">The cleanup delegate to invoke when the container is disposed.</param>
    /// <remarks>
    /// Useful for lifecycle cleanup that is not naturally tied to any single registered service —
    /// for example, releasing unmanaged resources or flushing buffers that span multiple services.
    /// The action is wrapped in a <see cref="Lifecycle.Disposable"/> and registered as a singleton
    /// instance so Autofac calls it during container disposal.
    /// </remarks>
    public static void OnDispose(this ContainerBuilder builder, Action action)
        => builder.RegisterInstance(Disposable.Create(action));
}
