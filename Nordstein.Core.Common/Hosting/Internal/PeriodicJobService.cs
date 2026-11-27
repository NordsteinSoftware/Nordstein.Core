using Autofac.Features.OwnedInstances;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Nordstein.Core.Common.Hosting.Internal;

/// <summary>
/// Runs one <see cref="IPeriodicJob"/> on a <see cref="PeriodicJobSchedule"/>: a pass in its own
/// lifetime scope, then either another pass at once (the job reported more work) or a wait of one
/// interval. A failing pass is logged and the loop continues; only the stopping token ends it.
/// </summary>
/// <typeparam name="TJob">The job type; its name appears in the log.</typeparam>
internal sealed class PeriodicJobService<TJob> : BackgroundService
    where TJob : class, IPeriodicJob
{
    private readonly Func<Owned<TJob>> jobFactory;
    private readonly PeriodicJobSchedule schedule;
    private readonly TimeProvider timeProvider;
    private readonly ILogger logger;

    public PeriodicJobService(
        Func<Owned<TJob>> jobFactory,
        PeriodicJobSchedule schedule,
        TimeProvider timeProvider,
        ILogger logger)
    {
        this.jobFactory = jobFactory;
        this.schedule = schedule;
        this.timeProvider = timeProvider;
        this.logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Yield first: a job that completes synchronously would otherwise run inside StartAsync
        // and hold up the start of every other hosted service.
        await Task.Yield();

        try
        {
            if (!schedule.RunAtStart)
            {
                await Task.Delay(schedule.Interval, timeProvider, stoppingToken);
            }

            while (!stoppingToken.IsCancellationRequested)
            {
                bool moreWork = await RunPassAsync(stoppingToken);
                if (!moreWork)
                {
                    await Task.Delay(schedule.Interval, timeProvider, stoppingToken);
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // The host is stopping: the normal end of the loop.
        }
    }

    private async Task<bool> RunPassAsync(CancellationToken stoppingToken)
    {
        try
        {
            await using Owned<TJob> owned = jobFactory();
            return await owned.Value.RunOnceAsync(stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            // An exception escaping ExecuteAsync would end the schedule for good: a transient fault
            // (database restart, network blip) must only cost this pass.
            logger.LogError(
                exception,
                "Periodic job {Job} failed; the next pass runs after {Interval}.",
                typeof(TJob).Name,
                schedule.Interval);
            return false;
        }
    }
}
