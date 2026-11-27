using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Nordstein.Core.Common.Diagnostics;

namespace Nordstein.Core.Storage.Diagnostics.Internal;

/// <summary>
/// Adds each executed, failed or cancelled command's duration and one count, and each data reader's reading
/// time (from its creation until it is disposed, so a large result counts in full), under
/// <see cref="RequestTimingNames.Database"/> to the accumulator of the context that ran it. See
/// <see cref="RequestTimingOptionsExtensions"/>.
/// </summary>
/// <remarks>
/// Stateless, so one instance serves every context and thread. It never changes a result and never throws for
/// a context without an accumulator; a negative duration (a clock anomaly) counts as zero, so timing can never
/// fail a command.
/// </remarks>
internal sealed class CommandTimingInterceptor : DbCommandInterceptor
{
    public override DbDataReader ReaderExecuted(DbCommand command, CommandExecutedEventData eventData, DbDataReader result)
    {
        Record(eventData);
        return base.ReaderExecuted(command, eventData, result);
    }

    public override ValueTask<DbDataReader> ReaderExecutedAsync(
        DbCommand command, CommandExecutedEventData eventData, DbDataReader result, CancellationToken cancellationToken = default)
    {
        Record(eventData);
        return base.ReaderExecutedAsync(command, eventData, result, cancellationToken);
    }

    public override int NonQueryExecuted(DbCommand command, CommandExecutedEventData eventData, int result)
    {
        Record(eventData);
        return base.NonQueryExecuted(command, eventData, result);
    }

    public override ValueTask<int> NonQueryExecutedAsync(
        DbCommand command, CommandExecutedEventData eventData, int result, CancellationToken cancellationToken = default)
    {
        Record(eventData);
        return base.NonQueryExecutedAsync(command, eventData, result, cancellationToken);
    }

    public override object? ScalarExecuted(DbCommand command, CommandExecutedEventData eventData, object? result)
    {
        Record(eventData);
        return base.ScalarExecuted(command, eventData, result);
    }

    public override ValueTask<object?> ScalarExecutedAsync(
        DbCommand command, CommandExecutedEventData eventData, object? result, CancellationToken cancellationToken = default)
    {
        Record(eventData);
        return base.ScalarExecutedAsync(command, eventData, result, cancellationToken);
    }

    public override void CommandFailed(DbCommand command, CommandErrorEventData eventData)
    {
        Record(eventData);
        base.CommandFailed(command, eventData);
    }

    public override Task CommandFailedAsync(
        DbCommand command, CommandErrorEventData eventData, CancellationToken cancellationToken = default)
    {
        Record(eventData);
        return base.CommandFailedAsync(command, eventData, cancellationToken);
    }

    public override void CommandCanceled(DbCommand command, CommandEndEventData eventData)
    {
        Record(eventData);
        base.CommandCanceled(command, eventData);
    }

    public override Task CommandCanceledAsync(
        DbCommand command, CommandEndEventData eventData, CancellationToken cancellationToken = default)
    {
        Record(eventData);
        return base.CommandCanceledAsync(command, eventData, cancellationToken);
    }

    public override InterceptionResult DataReaderDisposing(
        DbCommand command, DataReaderDisposingEventData eventData, InterceptionResult result)
    {
        if (TimingsOf(eventData) is { } timings)
        {
            timings.Mark(RequestTimingNames.Database, NotNegative(eventData.Duration));
        }

        return base.DataReaderDisposing(command, eventData, result);
    }

    private static void Record(CommandEndEventData eventData)
    {
        if (TimingsOf(eventData) is { } timings)
        {
            timings.Mark(RequestTimingNames.Database, NotNegative(eventData.Duration));
            timings.Count(RequestTimingNames.Database);
        }
    }

    private static IRequestTimings? TimingsOf(DbContextEventData eventData)
        => (eventData.Context as IRequestTimingSource)?.RequestTimings;

    private static TimeSpan NotNegative(TimeSpan duration) => duration < TimeSpan.Zero ? TimeSpan.Zero : duration;
}
