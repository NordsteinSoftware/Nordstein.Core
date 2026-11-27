using Autofac;
using Autofac.Features.OwnedInstances;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Nordstein.Core.Common.Hosting.Internal;

namespace Nordstein.Core.Common.Hosting;

/// <summary>
/// Registers <see cref="IPeriodicJob"/> implementations to run on a schedule.
/// </summary>
public static class PeriodicJobRegistrationExtensions
{
    /// <summary>
    /// Registers <typeparamref name="TJob"/> and a hosted service that runs it on the schedule
    /// <paramref name="schedule"/> returns.
    /// </summary>
    /// <typeparam name="TJob">The job. A new instance is resolved for every pass.</typeparam>
    /// <param name="builder">The container builder to register into.</param>
    /// <param name="schedule">
    /// Builds the schedule once, when the hosted service is first resolved, so it can read
    /// configuration from the container. The context must not be used after the call returns.
    /// </param>
    /// <remarks>
    /// <para>
    /// <typeparamref name="TJob"/> is registered as itself with the default (per-dependency)
    /// lifetime, unless the container already holds a registration for it. Register the job's own
    /// dependencies as usual.
    /// </para>
    /// <para>
    /// The hosted service is a <see cref="BackgroundService"/>: a pass that throws is logged at
    /// <c>Error</c> and the schedule continues, so the job keeps running through transient faults
    /// (see <see cref="HostingServiceCollectionExtensions.AddResilientBackgroundServices"/>). It
    /// uses the container's <see cref="TimeProvider"/>, which
    /// <see cref="Module"/> defaults to <see cref="TimeProvider.System"/>. Registering the same
    /// job type twice runs it twice.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="builder"/> or <paramref name="schedule"/> is <see langword="null"/>.
    /// </exception>
    public static void RegisterPeriodicJob<TJob>(
        this ContainerBuilder builder,
        Func<IComponentContext, PeriodicJobSchedule> schedule)
        where TJob : class, IPeriodicJob
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(schedule);

        builder.RegisterType<TJob>().AsSelf().PreserveExistingDefaults();
        builder
            .Register(context => new PeriodicJobService<TJob>(
                context.Resolve<Func<Owned<TJob>>>(),
                schedule(context),
                context.Resolve<TimeProvider>(),
                context.Resolve<ILoggerFactory>().CreateLogger(typeof(TJob))))
            .As<IHostedService>()
            .SingleInstance();
    }
}
