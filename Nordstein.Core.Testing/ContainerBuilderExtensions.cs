using Autofac;
using NSubstitute;

namespace Nordstein.Core.Testing;

/// <summary>
/// Extension methods for <see cref="ContainerBuilder"/> in test contexts.
/// </summary>
public static class ContainerBuilderExtensions
{
    /// <summary>
    /// Registers an NSubstitute mock for <typeparamref name="TService"/>, optionally
    /// configured via <paramref name="config"/>. The stub is registered
    /// <c>InstancePerDependency</c> so each resolve returns a fresh mock unless the
    /// caller changes the lifetime.
    /// </summary>
    /// <typeparam name="TService">The service type to stub; must be a reference type.</typeparam>
    /// <param name="builder">The Autofac container builder to register the stub on.</param>
    /// <param name="config">Optional action invoked on the newly created mock to set up behavior.</param>
    public static void RegisterStub<TService>(this ContainerBuilder builder, Action<TService>? config = null)
        where TService : class
    {
        builder.Register(_ =>
            {
                var fake = Substitute.For<TService>();
                config?.Invoke(fake);
                return fake;
            })
            .As<TService>()
            .InstancePerDependency();
    }
}