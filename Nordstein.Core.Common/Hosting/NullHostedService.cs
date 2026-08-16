using Microsoft.Extensions.Hosting;

namespace Nordstein.Core.Common.Hosting;

/// <summary>
/// An <see cref="IHostedService"/> implementation that performs no work.
/// </summary>
/// <remarks>
/// Use this as a placeholder or default when the DI graph requires an <see cref="IHostedService"/>
/// registration but no real background work is needed. Both lifecycle callbacks complete immediately.
/// </remarks>
public class NullHostedService : IHostedService
{
    /// <summary>
    /// No-op; completes immediately without starting any background work.
    /// </summary>
    /// <param name="cancellationToken">Not used.</param>
    /// <returns>A completed task.</returns>
    public Task StartAsync(CancellationToken cancellationToken)
        => Task.CompletedTask;

    /// <summary>
    /// No-op; completes immediately without stopping any background work.
    /// </summary>
    /// <param name="cancellationToken">Not used.</param>
    /// <returns>A completed task.</returns>
    public Task StopAsync(CancellationToken cancellationToken)
        => Task.CompletedTask;
}
