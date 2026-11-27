namespace Nordstein.Core.Common.Hosting;

/// <summary>
/// A unit of recurring background work. Register it with
/// <see cref="PeriodicJobRegistrationExtensions.RegisterPeriodicJob{TJob}"/>; the host then runs
/// <see cref="RunOnceAsync"/> on the schedule it was registered with.
/// </summary>
/// <remarks>
/// <para>
/// Every pass gets a <b>new instance</b> from its own lifetime scope, which is disposed when the
/// pass ends: scoped dependencies such as a database context or a unit of work are safe to inject,
/// and an implementation that also implements <see cref="IDisposable"/> or
/// <see cref="IAsyncDisposable"/> is disposed after each pass. An implementation therefore keeps
/// no state between passes; passes of one job never overlap.
/// </para>
/// <para>
/// A pass that throws is logged at <c>Error</c> and does not stop the schedule.
/// </para>
/// </remarks>
public interface IPeriodicJob
{
    /// <summary>
    /// Runs one pass of the job.
    /// </summary>
    /// <param name="cancellationToken">
    /// Cancelled when the host is stopping. Honour it: the host waits for the pass to end.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when the pass found more work than it did (a bounded batch that was
    /// full): the next pass then starts at once, without waiting for the interval.
    /// <see langword="false"/> when nothing is left: the next pass starts after the interval.
    /// A job that always returns <see langword="true"/> runs in a tight loop.
    /// </returns>
    /// <exception cref="OperationCanceledException">
    /// The pass observed <paramref name="cancellationToken"/>. This ends the schedule quietly when
    /// the host is stopping; any other exception is logged and the schedule continues.
    /// </exception>
    Task<bool> RunOnceAsync(CancellationToken cancellationToken);
}
