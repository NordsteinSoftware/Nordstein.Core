using System.Data.Common;
using Autofac;
using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Nordstein.Core.Common.Diagnostics;
using Nordstein.Core.Storage.Diagnostics;
using Nordstein.Core.Storage.Diagnostics.Internal;
using Nordstein.Core.Storage.Tests.Diagnostics.Internal;
using Nordstein.Core.Testing;
using NSubstitute;

namespace Nordstein.Core.Storage.Tests.Diagnostics;

/// <summary>
/// The command timing interceptor. The in-memory provider these tests use is not relational and never calls a
/// command interceptor, so the overrides are called directly with the payloads EF Core would pass; a separate
/// test pins that the in-memory provider indeed records nothing.
/// </summary>
[TestClass]
public sealed class CommandTimingInterceptorTests : BaseTest<Module>
{
    private static TimeSpan Five => TimeSpan.FromMilliseconds(5);

    [TestMethod]
    public void ReaderExecuted_TimedContext_AddsTheDurationAndOneCountAndReturnsTheReader()
    {
        IRequestTimings timings = Substitute.For<IRequestTimings>();
        using TimedTestDbContext context = Timed(timings);
        DbDataReader reader = CommandEvents.Reader();

        DbDataReader returned = Interceptor().ReaderExecuted(CommandEvents.Command(), CommandEvents.Executed(context, Five), reader);

        returned.Should().BeSameAs(reader);
        timings.Received(1).Mark(RequestTimingNames.Database, Five);
        timings.Received(1).Count(RequestTimingNames.Database);
    }

    [TestMethod]
    public async Task ReaderExecutedAsync_TimedContext_AddsTheDurationAndOneCountAndReturnsTheReader()
    {
        IRequestTimings timings = Substitute.For<IRequestTimings>();
        using TimedTestDbContext context = Timed(timings);
        DbDataReader reader = CommandEvents.Reader();

        DbDataReader returned = await Interceptor().ReaderExecutedAsync(
            CommandEvents.Command(), CommandEvents.Executed(context, Five), reader, CancellationToken);

        returned.Should().BeSameAs(reader);
        timings.Received(1).Mark(RequestTimingNames.Database, Five);
        timings.Received(1).Count(RequestTimingNames.Database);
    }

    [TestMethod]
    public async Task NonQueryExecuted_SyncAndAsync_RecordEachAndReturnTheRowCount()
    {
        IRequestTimings timings = Substitute.For<IRequestTimings>();
        using TimedTestDbContext context = Timed(timings);
        CommandTimingInterceptor interceptor = Interceptor();

        int sync = interceptor.NonQueryExecuted(CommandEvents.Command(), CommandEvents.Executed(context, Five), 3);
        int async = await interceptor.NonQueryExecutedAsync(CommandEvents.Command(), CommandEvents.Executed(context, Five), 4, CancellationToken);

        sync.Should().Be(3);
        async.Should().Be(4);
        timings.Received(2).Mark(RequestTimingNames.Database, Five);
        timings.Received(2).Count(RequestTimingNames.Database);
    }

    [TestMethod]
    public async Task ScalarExecuted_SyncAndAsync_RecordEachAndReturnTheValue()
    {
        IRequestTimings timings = Substitute.For<IRequestTimings>();
        using TimedTestDbContext context = Timed(timings);
        CommandTimingInterceptor interceptor = Interceptor();

        object? sync = interceptor.ScalarExecuted(CommandEvents.Command(), CommandEvents.Executed(context, Five), 42);
        object? async = await interceptor.ScalarExecutedAsync(CommandEvents.Command(), CommandEvents.Executed(context, Five), null, CancellationToken);

        sync.Should().Be(42);
        async.Should().BeNull();
        timings.Received(2).Mark(RequestTimingNames.Database, Five);
        timings.Received(2).Count(RequestTimingNames.Database);
    }

    [TestMethod]
    public async Task CommandFailed_SyncAndAsync_RecordEach()
    {
        IRequestTimings timings = Substitute.For<IRequestTimings>();
        using TimedTestDbContext context = Timed(timings);
        CommandTimingInterceptor interceptor = Interceptor();

        interceptor.CommandFailed(CommandEvents.Command(), CommandEvents.Failed(context, Five));
        await interceptor.CommandFailedAsync(CommandEvents.Command(), CommandEvents.Failed(context, Five), CancellationToken);

        timings.Received(2).Mark(RequestTimingNames.Database, Five);
        timings.Received(2).Count(RequestTimingNames.Database);
    }

    [TestMethod]
    public async Task CommandCanceled_SyncAndAsync_RecordEach()
    {
        IRequestTimings timings = Substitute.For<IRequestTimings>();
        using TimedTestDbContext context = Timed(timings);
        CommandTimingInterceptor interceptor = Interceptor();

        interceptor.CommandCanceled(CommandEvents.Command(), CommandEvents.Canceled(context, Five));
        await interceptor.CommandCanceledAsync(CommandEvents.Command(), CommandEvents.Canceled(context, Five), CancellationToken);

        timings.Received(2).Mark(RequestTimingNames.Database, Five);
        timings.Received(2).Count(RequestTimingNames.Database);
    }

    [TestMethod]
    public void DataReaderDisposing_TimedContext_AddsTheReadingTimeWithoutACount()
    {
        IRequestTimings timings = Substitute.For<IRequestTimings>();
        using TimedTestDbContext context = Timed(timings);
        InterceptionResult result = default;

        InterceptionResult returned = Interceptor().DataReaderDisposing(CommandEvents.Command(), CommandEvents.Disposing(context, Five), result);

        returned.Should().Be(result);
        timings.Received(1).Mark(RequestTimingNames.Database, Five);
        timings.DidNotReceive().Count(Arg.Any<string>(), Arg.Any<long>());
    }

    [TestMethod]
    public void Interceptor_ContextWithoutAnAccumulatorOrWithoutTheSeamOrNone_RecordsNothingAndPassesResultsThrough()
    {
        using TimedTestDbContext withoutTimings = Timed(null);
        using UntimedTestDbContext untimed = Untimed();
        CommandTimingInterceptor interceptor = Interceptor();
        DbDataReader reader = CommandEvents.Reader();

        foreach (DbContext? context in new DbContext?[] { withoutTimings, untimed, null })
        {
            interceptor.ReaderExecuted(CommandEvents.Command(), CommandEvents.Executed(context, Five), reader).Should().BeSameAs(reader);
            interceptor.CommandFailed(CommandEvents.Command(), CommandEvents.Failed(context, Five));
            interceptor.DataReaderDisposing(CommandEvents.Command(), CommandEvents.Disposing(context, Five), default);
        }
    }

    [TestMethod]
    public void Interceptor_NegativeDuration_CountsAsZero()
    {
        IRequestTimings timings = Substitute.For<IRequestTimings>();
        using TimedTestDbContext context = Timed(timings);
        CommandTimingInterceptor interceptor = Interceptor();

        interceptor.ReaderExecuted(CommandEvents.Command(), CommandEvents.Executed(context, TimeSpan.FromTicks(-1)), CommandEvents.Reader());
        interceptor.DataReaderDisposing(CommandEvents.Command(), CommandEvents.Disposing(context, TimeSpan.FromTicks(-1)), default);

        timings.Received(2).Mark(RequestTimingNames.Database, TimeSpan.Zero);
    }

    [TestMethod]
    public void Interceptor_ManyCommandsIntoTheRealAccumulator_SumAsTheUnitsDatabaseTime()
    {
        IRequestTimings timings = RealTimings();
        using TimedTestDbContext context = Timed(timings);
        CommandTimingInterceptor interceptor = Interceptor();

        interceptor.ReaderExecuted(CommandEvents.Command(), CommandEvents.Executed(context, TimeSpan.FromMilliseconds(2)), CommandEvents.Reader());
        interceptor.DataReaderDisposing(CommandEvents.Command(), CommandEvents.Disposing(context, TimeSpan.FromMilliseconds(3)), default);
        interceptor.NonQueryExecuted(CommandEvents.Command(), CommandEvents.Executed(context, TimeSpan.FromMilliseconds(4)), 1);

        RequestTimingSnapshot snapshot = timings.Snapshot();
        snapshot.Durations[RequestTimingNames.Database].Should().Be(TimeSpan.FromMilliseconds(9));
        snapshot.Counts[RequestTimingNames.Database].Should().Be(2);
    }

    [TestMethod]
    public void AddRequestTimings_AddsTheInterceptorOnceHoweverOftenItIsCalled()
    {
        var builder = new DbContextOptionsBuilder<TimedTestDbContext>();

        DbContextOptionsBuilder<TimedTestDbContext> returned = builder.AddRequestTimings().AddRequestTimings();
        ((DbContextOptionsBuilder)builder).AddRequestTimings();

        returned.Should().BeSameAs(builder);
        InterceptorsOf(builder.Options).OfType<CommandTimingInterceptor>().Should().ContainSingle();
    }

    [TestMethod]
    public void AddRequestTimings_KeepsTheOtherInterceptors()
    {
        IInterceptor other = Substitute.For<IDbCommandInterceptor>();
        var builder = new DbContextOptionsBuilder<TimedTestDbContext>();
        builder.AddInterceptors(other);

        builder.AddRequestTimings();

        InterceptorsOf(builder.Options).Should().HaveCount(2).And.Contain(other);
    }

    [TestMethod]
    public void AddRequestTimings_NullBuilder_ThrowsArgumentNull()
    {
        DbContextOptionsBuilder missing = (new DbContextOptionsBuilder[1])[0];
        DbContextOptionsBuilder<TimedTestDbContext> missingTyped = (new DbContextOptionsBuilder<TimedTestDbContext>[1])[0];

        Action plain = () => missing.AddRequestTimings();
        Action typed = () => missingTyped.AddRequestTimings();

        plain.Should().Throw<ArgumentNullException>().Which.ParamName.Should().Be("builder");
        typed.Should().Throw<ArgumentNullException>().Which.ParamName.Should().Be("builder");
    }

    [TestMethod]
    public async Task InMemoryProvider_WithTheInterceptor_RecordsNothing()
    {
        // The in-memory provider runs no DbCommand, so a product's in-memory tests see no database time.
        IRequestTimings timings = RealTimings();
        var builder = new DbContextOptionsBuilder<TimedTestDbContext>()
            .UseInMemoryDatabase("timings-" + Guid.NewGuid())
            .AddRequestTimings();
        await using var context = new TimedTestDbContext(builder.Options, timings);

        await context.Database.EnsureCreatedAsync(CancellationToken);
        await context.SaveChangesAsync(CancellationToken);

        timings.Snapshot().Durations.Should().BeEmpty();
    }

    private static CommandTimingInterceptor Interceptor() => new();

    /// <summary>
    /// Core's accumulator, from a scope of a container with Common's module (the storage test module has none).
    /// </summary>
    private IRequestTimings RealTimings()
        => GetServices(builder => builder.RegisterModule<Common.Module>())
            .GetRequiredService<ILifetimeScope>().BeginLifetimeScope().Resolve<IRequestTimings>();

    private static TimedTestDbContext Timed(IRequestTimings? timings)
        => new(new DbContextOptionsBuilder<TimedTestDbContext>().UseInMemoryDatabase("timed-" + Guid.NewGuid()).Options, timings);

    private static UntimedTestDbContext Untimed()
        => new(new DbContextOptionsBuilder<UntimedTestDbContext>().UseInMemoryDatabase("untimed-" + Guid.NewGuid()).Options);

    private static IEnumerable<IInterceptor> InterceptorsOf(DbContextOptions options)
        => options.FindExtension<CoreOptionsExtension>()?.Interceptors ?? [];
}
