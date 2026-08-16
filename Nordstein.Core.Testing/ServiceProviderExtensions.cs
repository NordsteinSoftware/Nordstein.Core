using Microsoft.Extensions.DependencyInjection;
using Nordstein.Core.Common.Lifecycle;

namespace Nordstein.Core.Testing;

/// <summary>
/// Extension methods for <see cref="IServiceProvider"/> in test contexts.
/// </summary>
public static class ServiceProviderExtensions
{
    /// <summary>
    /// Resolves an <see cref="ITempDirectory.Create"/> factory from the container and creates a
    /// temporary directory with the optional <paramref name="prefix"/>. The returned
    /// <see cref="ITempDirectory"/> is disposed by the caller.
    /// </summary>
    /// <param name="services">The service provider to resolve the factory from.</param>
    /// <param name="prefix">Optional name prefix for the temporary directory; <see langword="null"/> uses the factory default.</param>
    /// <returns>A new <see cref="ITempDirectory"/> that the caller is responsible for disposing.</returns>
    public static ITempDirectory GetTempDirectory(this IServiceProvider services, string? prefix = null)
    {
        var factory = services.GetRequiredService<ITempDirectory.Create>();
        return factory(prefix: prefix);
    }
}