using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace Nordstein.Core.Storage.Tests.Diagnostics.Internal;

/// <summary>
/// Builds the event payloads EF Core hands a command interceptor, so the interceptor's overrides can be called
/// directly: the in-memory provider of these tests is not relational and never calls a command interceptor.
/// </summary>
internal static class CommandEvents
{
    public static DbCommand Command() => Substitute.For<DbCommand>();

    public static DbDataReader Reader() => Substitute.For<DbDataReader>();

    public static CommandExecutedEventData Executed(DbContext? context, TimeSpan duration, object? result = null)
        => new(
            new TestEventDefinition(),
            (_, _) => string.Empty,
            Substitute.For<DbConnection>(),
            Command(),
            "SELECT 1",
            context,
            DbCommandMethod.ExecuteReader,
            Guid.NewGuid(),
            Guid.NewGuid(),
            result,
            async: false,
            logParameterValues: false,
            DateTimeOffset.UnixEpoch,
            duration,
            CommandSource.LinqQuery);

    public static CommandErrorEventData Failed(DbContext? context, TimeSpan duration)
        => new(
            new TestEventDefinition(),
            (_, _) => string.Empty,
            Substitute.For<DbConnection>(),
            Command(),
            "SELECT 1",
            context,
            DbCommandMethod.ExecuteNonQuery,
            Guid.NewGuid(),
            Guid.NewGuid(),
            new InvalidOperationException("the command failed"),
            async: true,
            logParameterValues: false,
            DateTimeOffset.UnixEpoch,
            duration,
            CommandSource.SaveChanges);

    public static CommandEndEventData Canceled(DbContext? context, TimeSpan duration)
        => new(
            new TestEventDefinition(),
            (_, _) => string.Empty,
            Substitute.For<DbConnection>(),
            Command(),
            "SELECT 1",
            context,
            DbCommandMethod.ExecuteScalar,
            Guid.NewGuid(),
            Guid.NewGuid(),
            async: true,
            logParameterValues: false,
            DateTimeOffset.UnixEpoch,
            duration,
            CommandSource.LinqQuery);

    public static DataReaderDisposingEventData Disposing(DbContext? context, TimeSpan duration)
        => new(
            new TestEventDefinition(),
            (_, _) => string.Empty,
            Command(),
            Reader(),
            context,
            Guid.NewGuid(),
            Guid.NewGuid(),
            recordsAffected: 0,
            readCount: 3,
            DateTimeOffset.UnixEpoch,
            duration);

    private sealed class TestEventDefinition : EventDefinitionBase
    {
        public TestEventDefinition()
            : base(Substitute.For<ILoggingOptions>(), new EventId(1), LogLevel.Debug, "Test")
        {
        }
    }
}
