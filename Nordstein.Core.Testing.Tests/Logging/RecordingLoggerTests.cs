using Autofac;
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Nordstein.Core.Testing.Logging;
using Nordstein.Core.Testing.Tests.Logging.Internal;

namespace Nordstein.Core.Testing.Tests.Logging;

/// <summary>
/// <see cref="RecordingLoggerProvider"/> and <see cref="RecordingLogger{T}"/>: every enabled entry
/// is kept with its category, level, event id, rendered message, exception and structured
/// properties, in write order, safely across threads.
/// </summary>
[TestClass]
public sealed class RecordingLoggerTests : BaseTest<Module>
{
    [TestMethod]
    public void Provider_RecordsEveryCategoryInWriteOrder()
    {
        using var provider = new RecordingLoggerProvider();
        ILogger first = provider.CreateLogger("First");
        ILogger second = provider.CreateLogger("Second");
        var failure = new InvalidOperationException("broken");

        first.LogInformation(new EventId(7, "Started"), "Started {Name} at {Count}", "job", 3);
        second.LogError(failure, "Failed");

        RecordedLogEntry[] entries = [.. provider.Entries];
        entries.Should().HaveCount(2);
        entries[0].Category.Should().Be("First");
        entries[0].Level.Should().Be(LogLevel.Information);
        entries[0].EventId.Should().Be(new EventId(7, "Started"));
        entries[0].Message.Should().Be("Started job at 3");
        entries[0].Exception.Should().BeNull();
        entries[0].Properties.Should().Equal(
            new KeyValuePair<string, object?>("Name", "job"),
            new KeyValuePair<string, object?>("Count", 3),
            new KeyValuePair<string, object?>("{OriginalFormat}", "Started {Name} at {Count}"));
        entries[1].Category.Should().Be("Second");
        entries[1].Level.Should().Be(LogLevel.Error);
        entries[1].Exception.Should().BeSameAs(failure);
        entries[1].ToString().Should().Be("Error Second: Failed");
    }

    [TestMethod]
    public void Entries_IsASnapshot()
    {
        using var provider = new RecordingLoggerProvider();
        ILogger logger = provider.CreateLogger("Snapshot");
        logger.LogWarning("one");

        IReadOnlyList<RecordedLogEntry> before = provider.Entries;
        logger.LogWarning("two");

        before.Should().ContainSingle();
        provider.Entries.Should().HaveCount(2);
    }

    [TestMethod]
    public void Log_WithStateThatIsNotStructured_RecordsTheRenderedMessageAndNoProperties()
    {
        using var provider = new RecordingLoggerProvider();
        ILogger logger = provider.CreateLogger("Plain");

        logger.Log(LogLevel.Debug, new EventId(1), 42, null, (state, _) => $"value {state}");

        RecordedLogEntry entry = provider.Entries.Should().ContainSingle().Subject;
        entry.Message.Should().Be("value 42");
        entry.Properties.Should().BeEmpty();
    }

    [TestMethod]
    public void Log_WithAnEnumerableOfPairsThatIsNotAList_CopiesTheProperties()
    {
        using var provider = new RecordingLoggerProvider();
        ILogger logger = provider.CreateLogger("Pairs");
        IEnumerable<KeyValuePair<string, object?>> state = Pairs();

        logger.Log(LogLevel.Information, default, state, null, (_, _) => "pairs");

        provider.Entries[0].Properties.Should().Equal([new KeyValuePair<string, object?>("Key", "value")]);
    }

    [TestMethod]
    public void Log_WithAStructuredStateThatChangesLater_KeepsTheValuesAsTheyWereLogged()
    {
        using var provider = new RecordingLoggerProvider();
        ILogger logger = provider.CreateLogger("Copy");
        var state = new List<KeyValuePair<string, object?>> { new("Key", "before") };

        logger.Log(LogLevel.Information, default, state, null, (_, _) => "copied");
        state[0] = new KeyValuePair<string, object?>("Key", "after");

        provider.Entries[0].Properties.Should().Equal([new KeyValuePair<string, object?>("Key", "before")]);
    }

    [TestMethod]
    public void MinimumLevel_GatesIsEnabledAndRecording()
    {
        using var provider = new RecordingLoggerProvider(LogLevel.Warning);
        ILogger logger = provider.CreateLogger("Gated");
        int formatted = 0;

        logger.IsEnabled(LogLevel.Information).Should().BeFalse();
        logger.IsEnabled(LogLevel.Warning).Should().BeTrue();
        logger.Log(LogLevel.Information, default, "state", null, (_, _) => $"{++formatted}");
        logger.LogWarning("kept");

        provider.MinimumLevel.Should().Be(LogLevel.Warning);
        provider.Entries.Should().ContainSingle().Which.Message.Should().Be("kept");
        formatted.Should().Be(0, "a disabled entry is never formatted");
    }

    [TestMethod]
    public void LevelNone_IsNeverEnabledNorRecorded()
    {
        using var provider = new RecordingLoggerProvider();
        ILogger logger = provider.CreateLogger("None");

        logger.IsEnabled(LogLevel.None).Should().BeFalse();
        logger.Log(LogLevel.None, default, "state", null, (_, _) => "never");

        provider.Entries.Should().BeEmpty();
        provider.MinimumLevel.Should().Be(LogLevel.Trace);
    }

    [TestMethod]
    public void MinimumLevelNone_RecordsNothing()
    {
        using var provider = new RecordingLoggerProvider(LogLevel.None);

        provider.CreateLogger("Silent").LogCritical("dropped");

        provider.Entries.Should().BeEmpty();
    }

    [TestMethod]
    [DataRow(-1)]
    [DataRow(7)]
    public void MinimumLevel_OutsideTheDefinedLevels_Throws(int level)
    {
        Action provider = () => _ = new RecordingLoggerProvider((LogLevel)level);
        Action logger = () => _ = new RecordingLogger<RecordingLoggerTests>((LogLevel)level);

        provider.Should().Throw<ArgumentOutOfRangeException>().WithParameterName("minimumLevel");
        logger.Should().Throw<ArgumentOutOfRangeException>().WithParameterName("minimumLevel");
    }

    [TestMethod]
    public void BeginScope_ReturnsADisposableAndRecordsNothing()
    {
        using var provider = new RecordingLoggerProvider();
        ILogger logger = provider.CreateLogger("Scoped");

        using (IDisposable? scope = logger.BeginScope("scope {Id}", 1))
        {
            scope.Should().NotBeNull();
            logger.LogInformation("inside");
        }

        provider.Entries.Should().ContainSingle();
    }

    [TestMethod]
    public void CreateLogger_WithNullCategory_Throws()
    {
        using var provider = new RecordingLoggerProvider();

#pragma warning disable CS8625 // WHY: the guard under test is reachable only with the null the annotations forbid.
        Action act = () => provider.CreateLogger(null);
#pragma warning restore CS8625

        act.Should().Throw<ArgumentNullException>().WithParameterName("categoryName");
    }

    [TestMethod]
    public void Log_WithNullFormatter_Throws()
    {
        using var provider = new RecordingLoggerProvider();
        ILogger logger = provider.CreateLogger("Formatter");

#pragma warning disable CS8625 // WHY: the guard under test is reachable only with the null the annotations forbid.
        Action act = () => logger.Log(LogLevel.Information, default, "state", null, null);
#pragma warning restore CS8625

        act.Should().Throw<ArgumentNullException>().WithParameterName("formatter");
    }

    [TestMethod]
    public void Provider_AfterDispose_KeepsAndStillRecordsEntries()
    {
        var provider = new RecordingLoggerProvider();
        ILogger logger = provider.CreateLogger("Disposed");
        logger.LogInformation("before");

        provider.Dispose();
        provider.Dispose();
        logger.LogInformation("after");

        provider.Entries.Select(entry => entry.Message).Should().Equal("before", "after");
    }

    [TestMethod]
    public void RegisteredInTheContainer_ReceivesTheContainersLoggers()
    {
        var provider = new RecordingLoggerProvider();
        IServiceProvider services = GetServices(builder => builder.RegisterInstance(provider).As<ILoggerProvider>());

        ILogger<RecordingLoggerTests> logger = services.GetRequiredService<ILogger<RecordingLoggerTests>>();
        logger.LogInformation("through the container");
        logger.LogDebug("below the container's default minimum level");

        RecordedLogEntry entry = provider.Entries.Should().ContainSingle().Subject;
        entry.Category.Should().Be(typeof(RecordingLoggerTests).FullName);
        entry.Message.Should().Be("through the container");
    }

    [TestMethod]
    public void GenericLogger_RecordsUnderTheCategoryMicrosoftsLoggerUses()
    {
        AssertSameCategoryAsTheFrameworks<RecordingLoggerTests>();
        AssertSameCategoryAsTheFrameworks<Outer<int>.Inner>();
        AssertSameCategoryAsTheFrameworks<Dictionary<string, List<int>>>();
        AssertSameCategoryAsTheFrameworks<int>();
        AssertSameCategoryAsTheFrameworks<int[]>();
    }

    [TestMethod]
    public void GenericLogger_RecordsItsOwnEntries()
    {
        var logger = new RecordingLogger<RecordingLoggerTests>();
        var other = new RecordingLogger<RecordingLoggerTests>();

        logger.LogWarning("mine {Value}", 5);

        logger.Entries.Should().ContainSingle().Which.Message.Should().Be("mine 5");
        logger.Entries[0].Category.Should().Be(logger.Category);
        other.Entries.Should().BeEmpty();
        logger.IsEnabled(LogLevel.Trace).Should().BeTrue();
    }

    [TestMethod]
    public void GenericLogger_WithAMinimumLevel_GatesRecording()
    {
        var logger = new RecordingLogger<RecordingLoggerTests>(LogLevel.Error);

        logger.LogWarning("dropped");
        logger.LogError("kept");
        using (logger.BeginScope("scope"))
        {
            logger.LogCritical("also kept");
        }

        logger.MinimumLevel.Should().Be(LogLevel.Error);
        logger.Entries.Select(entry => entry.Message).Should().Equal("kept", "also kept");
    }

    [TestMethod]
    public async Task ConcurrentWriters_LoseNoEntry()
    {
        using var provider = new RecordingLoggerProvider();
        var logger = new RecordingLogger<RecordingLoggerTests>();

        await Task.WhenAll(Enumerable.Range(0, 8).Select(writer => Task.Run(() =>
        {
            ILogger categoryLogger = provider.CreateLogger($"Writer{writer}");
            for (int i = 0; i < 500; i++)
            {
                categoryLogger.LogInformation("entry {Index}", i);
                logger.LogInformation("entry {Index}", i);
                _ = provider.Entries.Count;
            }
        })));

        provider.Entries.Should().HaveCount(4000);
        logger.Entries.Should().HaveCount(4000);
    }

    private static IEnumerable<KeyValuePair<string, object?>> Pairs()
    {
        yield return new KeyValuePair<string, object?>("Key", "value");
    }

    private static void AssertSameCategoryAsTheFrameworks<T>()
    {
        var capture = new CategoryCapture();
        using (var factory = new LoggerFactory([capture]))
        {
            _ = factory.CreateLogger<T>();
        }

        new RecordingLogger<T>().Category.Should().Be(capture.Categories.Should().ContainSingle().Subject);
    }
}
