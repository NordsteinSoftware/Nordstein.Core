using System.Collections.Concurrent;
using System.Threading.Channels;
using Autofac;
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Nordstein.Core.Common.DependencyInjection;
using Nordstein.Core.Common.Hosting;
using Nordstein.Core.Common.Hosting.Internal;
using Nordstein.Core.Testing;
using Nordstein.Core.Testing.Time;

namespace Nordstein.Core.Common.Tests.Hosting;

/// <summary>
/// Covers <see cref="IPeriodicJob"/> scheduling end to end through the container: every test
/// drives a <see cref="ManualClock"/>, so no test waits for real time. Waits on the code
/// under test use the manual clock's "a timer is armed" signal, never a sleep.
/// </summary>
[TestClass]
public sealed class PeriodicJobServiceTests : BaseTest<Module>
{
    private const int IntervalMinutes = 5;

    // Failure guard only: a hung wait fails the test instead of the run. Passing tests never reach it.
    private const int GuardMilliseconds = 30_000;

    [TestMethod]
    [Timeout(GuardMilliseconds)]
    public async Task RunAtStart_True_RunsBeforeTheFirstInterval()
    {
        var time = new ManualClock();
        var passes = new PassLog();
        IServiceProvider services = GetServices(builder =>
            Register(builder, time, passes, new CapturedLogs(), Schedule(runAtStart: true)));
        IHostedService service = SingleHostedService(services);

        await service.StartAsync(CancellationToken);
        await passes.WaitForPassesAsync(1, CancellationToken);
        await time.WaitForPendingTimerAsync(CancellationToken);

        // No Advance happened, yet the pass ran: it did not wait for an interval.
        passes.Started.Should().Be(1);

        await service.StopAsync(CancellationToken);
    }

    [TestMethod]
    [Timeout(GuardMilliseconds)]
    public async Task RunAtStart_False_WaitsOneIntervalFirst()
    {
        var time = new ManualClock();
        var passes = new PassLog();
        IServiceProvider services = GetServices(builder =>
            Register(builder, time, passes, new CapturedLogs(), Schedule(runAtStart: false)));
        IHostedService service = SingleHostedService(services);

        await service.StartAsync(CancellationToken);
        await time.WaitForPendingTimerAsync(CancellationToken);
        passes.Started.Should().Be(0);

        time.Advance(Interval() - TimeSpan.FromSeconds(1));
        passes.Started.Should().Be(0);

        time.Advance(TimeSpan.FromSeconds(1));
        await passes.WaitForPassesAsync(1, CancellationToken);
        passes.Started.Should().Be(1);

        await service.StopAsync(CancellationToken);
    }

    [TestMethod]
    [Timeout(GuardMilliseconds)]
    public async Task JobReportsMoreWork_RunsAgainWithoutWaiting()
    {
        var time = new ManualClock();
        var passes = new PassLog();
        passes.Script(_ => Task.FromResult(true));
        passes.Script(_ => Task.FromResult(false));
        IServiceProvider services = GetServices(builder =>
            Register(builder, time, passes, new CapturedLogs(), Schedule(runAtStart: true)));
        IHostedService service = SingleHostedService(services);

        await service.StartAsync(CancellationToken);
        await passes.WaitForPassesAsync(2, CancellationToken);
        await time.WaitForPendingTimerAsync(CancellationToken);

        // Two passes with the clock never advanced; once the job reports "done" the loop waits.
        passes.Started.Should().Be(2);

        await service.StopAsync(CancellationToken);
    }

    [TestMethod]
    [Timeout(GuardMilliseconds)]
    public async Task JobThrows_LogsErrorAndRunsAfterTheInterval()
    {
        var time = new ManualClock();
        var passes = new PassLog();
        var logs = new CapturedLogs();
        var failure = new InvalidOperationException("boom");
        passes.Script(_ => throw failure);
        IServiceProvider services = GetServices(builder =>
            Register(builder, time, passes, logs, Schedule(runAtStart: true)));
        IHostedService service = SingleHostedService(services);

        await service.StartAsync(CancellationToken);
        await passes.WaitForPassesAsync(1, CancellationToken);
        await time.WaitForPendingTimerAsync(CancellationToken);

        LogEntry error = logs.Entries.Should().ContainSingle(entry => entry.Level == LogLevel.Error).Subject;
        error.Exception.Should().BeSameAs(failure);
        error.Message.Should().Contain(nameof(RecordingJob));
        passes.Started.Should().Be(1);

        time.Advance(Interval());
        await passes.WaitForPassesAsync(1, CancellationToken);
        passes.Started.Should().Be(2);

        await service.StopAsync(CancellationToken);
    }

    [TestMethod]
    [Timeout(GuardMilliseconds)]
    public async Task JobThrowsOperationCanceled_WithoutStopping_IsLoggedAndTheLoopContinues()
    {
        var time = new ManualClock();
        var passes = new PassLog();
        var logs = new CapturedLogs();
        passes.Script(_ => throw new OperationCanceledException("the job's own timeout"));
        IServiceProvider services = GetServices(builder =>
            Register(builder, time, passes, logs, Schedule(runAtStart: true)));
        IHostedService service = SingleHostedService(services);

        await service.StartAsync(CancellationToken);
        await passes.WaitForPassesAsync(1, CancellationToken);
        await time.WaitForPendingTimerAsync(CancellationToken);

        logs.Entries.Should().ContainSingle(entry => entry.Level == LogLevel.Error);

        time.Advance(Interval());
        await passes.WaitForPassesAsync(1, CancellationToken);
        passes.Started.Should().Be(2);

        await service.StopAsync(CancellationToken);
    }

    [TestMethod]
    [Timeout(GuardMilliseconds)]
    public async Task EachPass_GetsItsOwnLifetimeScope()
    {
        var time = new ManualClock();
        var passes = new PassLog();
        IServiceProvider services = GetServices(builder =>
            Register(builder, time, passes, new CapturedLogs(), Schedule(runAtStart: true)));
        IHostedService service = SingleHostedService(services);

        await service.StartAsync(CancellationToken);
        await passes.WaitForPassesAsync(1, CancellationToken);
        await time.WaitForPendingTimerAsync(CancellationToken);
        passes.Disposed.Should().Be(1);

        time.Advance(Interval());
        await passes.WaitForPassesAsync(1, CancellationToken);
        await time.WaitForPendingTimerAsync(CancellationToken);

        passes.Disposed.Should().Be(2);
        passes.DistinctJobInstances.Should().Be(2);

        await service.StopAsync(CancellationToken);
    }

    [TestMethod]
    [Timeout(GuardMilliseconds)]
    public async Task StoppingToken_EndsTheLoop()
    {
        var time = new ManualClock();
        var passes = new PassLog();
        IServiceProvider services = GetServices(builder =>
            Register(builder, time, passes, new CapturedLogs(), Schedule(runAtStart: true)));
        IHostedService service = SingleHostedService(services);
        await service.StartAsync(CancellationToken);
        await passes.WaitForPassesAsync(1, CancellationToken);
        await time.WaitForPendingTimerAsync(CancellationToken);

        // StopAsync awaits the loop, so returning proves the wait for the next interval was abandoned.
        await service.StopAsync(CancellationToken);

        time.HasPendingTimer.Should().BeFalse();
        time.Advance(Interval());
        passes.Started.Should().Be(1);
    }

    [TestMethod]
    [Timeout(GuardMilliseconds)]
    public async Task StoppingToken_CancelsARunningPass_EndsTheLoopWithoutLoggingAnError()
    {
        var time = new ManualClock();
        var passes = new PassLog();
        var logs = new CapturedLogs();
        var running = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        passes.Script(async stopping =>
        {
            running.SetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, stopping);
            return true;
        });
        IServiceProvider services = GetServices(builder =>
            Register(builder, time, passes, logs, Schedule(runAtStart: true)));
        IHostedService service = SingleHostedService(services);
        await service.StartAsync(CancellationToken);
        await running.Task.WaitAsync(CancellationToken);

        await service.StopAsync(CancellationToken);

        logs.Entries.Should().NotContain(entry => entry.Level >= LogLevel.Error);
        passes.Disposed.Should().Be(1);
        time.HasPendingTimer.Should().BeFalse();
    }

    [TestMethod]
    [Timeout(GuardMilliseconds)]
    [DataRow(0L)]
    [DataRow(-1L)]
    [DataRow(-10_000L)] // Timeout.InfiniteTimeSpan: "never" is not an interval.
    public void Schedule_NonPositiveInterval_Throws(long ticks)
    {
        Func<PeriodicJobSchedule> create = () => new PeriodicJobSchedule { Interval = TimeSpan.FromTicks(ticks) };

        create.Should().Throw<ArgumentOutOfRangeException>().WithParameterName("Interval");
    }

    [TestMethod]
    [Timeout(GuardMilliseconds)]
    public void Schedule_IntervalAboveTaskDelayMaximum_Throws()
    {
        // Task.Delay(TimeSpan, TimeProvider, CancellationToken) accepts at most uint.MaxValue - 1 ms.
        TimeSpan maximum = TimeSpan.FromMilliseconds(uint.MaxValue - 1);

        var atMaximum = new PeriodicJobSchedule { Interval = maximum };
        Func<PeriodicJobSchedule> aboveMaximum = () =>
            new PeriodicJobSchedule { Interval = maximum + TimeSpan.FromMilliseconds(1) };

        atMaximum.Interval.Should().Be(maximum);
        aboveMaximum.Should().Throw<ArgumentOutOfRangeException>().WithParameterName("Interval");
    }

    [TestMethod]
    [Timeout(GuardMilliseconds)]
    public void Schedule_PositiveInterval_IsKeptAndRunsAtStartByDefault()
    {
        var schedule = new PeriodicJobSchedule { Interval = TimeSpan.FromTicks(1) };

        schedule.Interval.Should().Be(TimeSpan.FromTicks(1));
        schedule.RunAtStart.Should().BeTrue();
    }

    [TestMethod]
    [Timeout(GuardMilliseconds)]
    public async Task RegisterPeriodicJob_ScheduleFactory_ReadsTheContainer()
    {
        var time = new ManualClock();
        var passes = new PassLog();
        IServiceProvider services = GetServices(builder =>
        {
            builder.RegisterInstance(time).As<TimeProvider>();
            builder.RegisterInstance(passes);
            builder.RegisterInstance(new CapturedLogs()).As<ILoggerProvider>();
            builder.RegisterInstance(new ConfiguredInterval(TimeSpan.FromHours(2)));
            builder.RegisterPeriodicJob<RecordingJob>(context => new PeriodicJobSchedule
            {
                Interval = context.Resolve<ConfiguredInterval>().Value,
                RunAtStart = false,
            });
        });
        IHostedService service = SingleHostedService(services);

        await service.StartAsync(CancellationToken);
        await time.WaitForPendingTimerAsync(CancellationToken);
        time.Advance(TimeSpan.FromHours(2) - TimeSpan.FromSeconds(1));
        passes.Started.Should().Be(0);
        time.Advance(TimeSpan.FromSeconds(1));
        await passes.WaitForPassesAsync(1, CancellationToken);

        passes.Started.Should().Be(1);

        await service.StopAsync(CancellationToken);
    }

    [TestMethod]
    [Timeout(GuardMilliseconds)]
    public async Task RegisterPeriodicJob_TwoJobTypes_RunIndependently()
    {
        var time = new ManualClock();
        var passes = new PassLog();
        IServiceProvider services = GetServices(builder =>
        {
            Register(builder, time, passes, new CapturedLogs(), Schedule(runAtStart: true));
            builder.RegisterPeriodicJob<OtherJob>(_ => Schedule(runAtStart: true));
        });
        IHostedService[] hosted = JobServices(services);
        hosted.Should().HaveCount(2);

        foreach (IHostedService service in hosted)
        {
            await service.StartAsync(CancellationToken);
        }

        await passes.WaitForPassesAsync(2, CancellationToken);

        passes.JobTypes.Should().BeEquivalentTo([typeof(RecordingJob), typeof(OtherJob)]);

        foreach (IHostedService service in hosted)
        {
            await service.StopAsync(CancellationToken);
        }
    }

    [TestMethod]
    [Timeout(GuardMilliseconds)]
    public void RegisterPeriodicJob_NullArguments_Throw()
    {
        var builder = new ContainerBuilder();

        ContainerBuilder missingBuilder = (new ContainerBuilder[1])[0];
        Func<IComponentContext, PeriodicJobSchedule> missingSchedule = (new Func<IComponentContext, PeriodicJobSchedule>[1])[0];

        Action nullBuilder = () => PeriodicJobRegistrationExtensions.RegisterPeriodicJob<RecordingJob>(
            missingBuilder, _ => Schedule(runAtStart: true));
        Action nullSchedule = () => builder.RegisterPeriodicJob<RecordingJob>(missingSchedule);

        nullBuilder.Should().Throw<ArgumentNullException>().WithParameterName("builder");
        nullSchedule.Should().Throw<ArgumentNullException>().WithParameterName("schedule");
    }

    [TestMethod]
    [Timeout(GuardMilliseconds)]
    public void CommonModule_RegistersTheSystemTimeProvider()
    {
        IServiceProvider services = GetServices();

        services.GetRequiredService<TimeProvider>().Should().BeSameAs(TimeProvider.System);
    }

    [TestMethod]
    [Timeout(GuardMilliseconds)]
    public void CommonModule_KeepsATimeProviderRegisteredBeforeIt()
    {
        var time = new ManualClock();
        var builder = new ContainerBuilder();
        builder.RegisterInstance(time).As<TimeProvider>();
        builder.RegisterModule<Common.Module>();

        using IContainer container = builder.Build();

        container.Resolve<TimeProvider>().Should().BeSameAs(time);
    }

    private static TimeSpan Interval() => TimeSpan.FromMinutes(IntervalMinutes);

    private static PeriodicJobSchedule Schedule(bool runAtStart) =>
        new() { Interval = Interval(), RunAtStart = runAtStart };

    private static void Register(
        ContainerBuilder builder,
        ManualClock time,
        PassLog passes,
        CapturedLogs logs,
        PeriodicJobSchedule schedule)
    {
        builder.RegisterInstance(time).As<TimeProvider>();
        builder.RegisterInstance(passes);
        builder.RegisterInstance(logs).As<ILoggerProvider>();
        builder.RegisterPeriodicJob<RecordingJob>(_ => schedule);
    }

    private static IHostedService[] JobServices(IServiceProvider services) =>
        services.GetRequiredService<IEnumerable<IHostedService>>()
            .Where(service => service.GetType() is { IsGenericType: true } type
                && type.GetGenericTypeDefinition() == typeof(PeriodicJobService<>))
            .ToArray();

    private static IHostedService SingleHostedService(IServiceProvider services) =>
        JobServices(services).Should().ContainSingle().Subject;

    private sealed record ConfiguredInterval(TimeSpan Value);

    private sealed class RecordingJob : IPeriodicJob, IDisposable
    {
        private readonly PassLog passes;

        public RecordingJob(PassLog passes) => this.passes = passes;

        public Task<bool> RunOnceAsync(CancellationToken cancellationToken) =>
            passes.RunAsync(this, cancellationToken);

        public void Dispose() => passes.RecordDisposal();
    }

    private sealed class OtherJob : IPeriodicJob
    {
        private readonly PassLog passes;

        public OtherJob(PassLog passes) => this.passes = passes;

        public Task<bool> RunOnceAsync(CancellationToken cancellationToken) =>
            passes.RunAsync(this, cancellationToken);
    }

    /// <summary>
    /// What the jobs under test did, plus a script of what the next passes should do. Created inside
    /// each test and handed to the container, so no state is shared between tests.
    /// </summary>
    private sealed class PassLog
    {
        private readonly Channel<int> completed = Channel.CreateUnbounded<int>();
        private readonly ConcurrentQueue<Func<CancellationToken, Task<bool>>> script = new();
        private readonly ConcurrentQueue<IPeriodicJob> jobs = new();
        private int started;
        private int disposed;

        public int Started => Volatile.Read(ref started);

        public int Disposed => Volatile.Read(ref disposed);

        public int DistinctJobInstances => jobs.Distinct().Count();

        public IEnumerable<Type> JobTypes => jobs.Select(job => job.GetType()).Distinct();

        public void Script(Func<CancellationToken, Task<bool>> pass) => script.Enqueue(pass);

        public async Task<bool> RunAsync(IPeriodicJob job, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref started);
            jobs.Enqueue(job);
            try
            {
                return script.TryDequeue(out Func<CancellationToken, Task<bool>>? pass)
                    && await pass(cancellationToken);
            }
            finally
            {
                completed.Writer.TryWrite(0);
            }
        }

        public void RecordDisposal() => Interlocked.Increment(ref disposed);

        public async Task WaitForPassesAsync(int count, CancellationToken cancellationToken)
        {
            for (int i = 0; i < count; i++)
            {
                await completed.Reader.ReadAsync(cancellationToken);
            }
        }
    }

    private sealed record LogEntry(LogLevel Level, string Message, Exception? Exception);

    private sealed class CapturedLogs : ILoggerProvider
    {
        private readonly ConcurrentQueue<LogEntry> entries = new();

        public IReadOnlyCollection<LogEntry> Entries => entries.ToArray();

        public ILogger CreateLogger(string categoryName) => new CapturingLogger(entries);

        public void Dispose()
        {
        }

        private sealed class CapturingLogger : ILogger
        {
            private readonly ConcurrentQueue<LogEntry> entries;

            public CapturingLogger(ConcurrentQueue<LogEntry> entries) => this.entries = entries;

            public IDisposable? BeginScope<TState>(TState state)
                where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(
                LogLevel logLevel,
                EventId eventId,
                TState state,
                Exception? exception,
                Func<TState, Exception?, string> formatter) =>
                entries.Enqueue(new LogEntry(logLevel, formatter(state, exception), exception));
        }
    }
}
