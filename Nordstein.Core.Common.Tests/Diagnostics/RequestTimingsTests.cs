using Autofac;
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Nordstein.Core.Common.Diagnostics;
using Nordstein.Core.Testing;

namespace Nordstein.Core.Common.Tests.Diagnostics;

[TestClass]
public sealed class RequestTimingsTests : BaseTest<Module>
{
    [TestMethod]
    public void Mark_SameName_SumsTheDurations()
    {
        IRequestTimings timings = Timings();

        timings.Mark("work", TimeSpan.FromMilliseconds(5));
        timings.Mark("work", TimeSpan.FromMilliseconds(7));
        timings.Mark("other", TimeSpan.Zero);

        RequestTimingSnapshot snapshot = timings.Snapshot();
        snapshot.Durations.Should().BeEquivalentTo(new Dictionary<string, TimeSpan>
        {
            ["work"] = TimeSpan.FromMilliseconds(12),
            ["other"] = TimeSpan.Zero,
        });
        snapshot.Counts.Should().BeEmpty();
        snapshot.Values.Should().BeEmpty();
    }

    [TestMethod]
    public void Count_SameName_SumsTheCountsWithOneByDefault()
    {
        IRequestTimings timings = Timings();

        timings.Count("rows");
        timings.Count("rows", 41);
        timings.Count("empty", 0);

        timings.Snapshot().Counts.Should().BeEquivalentTo(new Dictionary<string, long> { ["rows"] = 42, ["empty"] = 0 });
    }

    [TestMethod]
    public void Set_SameName_KeepsTheLastValue()
    {
        IRequestTimings timings = Timings();

        timings.Set("operation", "first");
        timings.Set("operation", "second");
        timings.Set("blank", string.Empty);

        timings.Snapshot().Values.Should().BeEquivalentTo(new Dictionary<string, string> { ["operation"] = "second", ["blank"] = string.Empty });
    }

    [TestMethod]
    public void Names_AreOrdinal()
    {
        IRequestTimings timings = Timings();

        timings.Count("Work");
        timings.Count("work");

        RequestTimingSnapshot snapshot = timings.Snapshot();
        snapshot.Counts.Should().HaveCount(2);
        snapshot.Counts.ContainsKey("WORK").Should().BeFalse();
    }

    [TestMethod]
    public void Mark_SumBeyondTheLargestSpan_Saturates()
    {
        IRequestTimings timings = Timings();

        timings.Mark("long", TimeSpan.MaxValue - TimeSpan.FromTicks(1));
        timings.Mark("long", TimeSpan.FromDays(1));
        timings.Mark("exact", TimeSpan.MaxValue - TimeSpan.FromTicks(1));
        timings.Mark("exact", TimeSpan.FromTicks(1));

        timings.Snapshot().Durations["long"].Should().Be(TimeSpan.MaxValue);
        timings.Snapshot().Durations["exact"].Should().Be(TimeSpan.MaxValue);
    }

    [TestMethod]
    public void Count_SumBeyondTheLargestLong_Saturates()
    {
        IRequestTimings timings = Timings();

        timings.Count("many", long.MaxValue - 1);
        timings.Count("many", 5);
        timings.Count("many");
        timings.Count("exact", long.MaxValue - 1);
        timings.Count("exact");

        timings.Snapshot().Counts["many"].Should().Be(long.MaxValue);
        timings.Snapshot().Counts["exact"].Should().Be(long.MaxValue);
    }

    [TestMethod]
    public void Snapshot_IsACopyThatLaterRecordsLeaveUnchanged()
    {
        IRequestTimings timings = Timings();
        timings.Mark("work", TimeSpan.FromSeconds(1));
        timings.Count("work");
        timings.Set("label", "a");

        RequestTimingSnapshot before = timings.Snapshot();
        timings.Mark("work", TimeSpan.FromSeconds(1));
        timings.Count("work");
        timings.Set("label", "b");
        timings.Mark("new", TimeSpan.FromSeconds(1));

        before.Durations.Should().BeEquivalentTo(new Dictionary<string, TimeSpan> { ["work"] = TimeSpan.FromSeconds(1) });
        before.Counts.Should().BeEquivalentTo(new Dictionary<string, long> { ["work"] = 1 });
        before.Values.Should().BeEquivalentTo(new Dictionary<string, string> { ["label"] = "a" });
        timings.Snapshot().Durations["work"].Should().Be(TimeSpan.FromSeconds(2));
    }

    [TestMethod]
    [DataRow("")]
    [DataRow(" ")]
    [DataRow("\t")]
    public void EveryWriter_BlankName_Throws(string name)
    {
        IRequestTimings timings = Timings();
        Action[] acts = [() => timings.Mark(name, TimeSpan.Zero), () => timings.Count(name), () => timings.Set(name, "x")];

        foreach (Action act in acts)
        {
            act.Should().Throw<ArgumentException>().Which.ParamName.Should().Be("name");
        }

        timings.Snapshot().Durations.Should().BeEmpty();
    }

    [TestMethod]
    public void EveryWriter_NullArguments_ThrowArgumentNull()
    {
        IRequestTimings timings = Timings();
        string missing = (new string[1])[0];

        Action mark = () => timings.Mark(missing, TimeSpan.Zero);
        Action count = () => timings.Count(missing);
        Action setName = () => timings.Set(missing, "x");
        Action setValue = () => timings.Set("label", missing);

        mark.Should().Throw<ArgumentNullException>().Which.ParamName.Should().Be("name");
        count.Should().Throw<ArgumentNullException>().Which.ParamName.Should().Be("name");
        setName.Should().Throw<ArgumentNullException>().Which.ParamName.Should().Be("name");
        setValue.Should().Throw<ArgumentNullException>().Which.ParamName.Should().Be("value");
    }

    [TestMethod]
    public void Mark_NegativeDuration_ThrowsAndRecordsNothing()
    {
        IRequestTimings timings = Timings();

        Action act = () => timings.Mark("work", TimeSpan.FromTicks(-1));

        act.Should().Throw<ArgumentOutOfRangeException>().Which.ParamName.Should().Be("duration");
        timings.Snapshot().Durations.Should().BeEmpty();
    }

    [TestMethod]
    public void Count_NegativeAmount_ThrowsAndRecordsNothing()
    {
        IRequestTimings timings = Timings();

        Action act = () => timings.Count("work", -1);

        act.Should().Throw<ArgumentOutOfRangeException>().Which.ParamName.Should().Be("by");
        timings.Snapshot().Counts.Should().BeEmpty();
    }

    [TestMethod]
    public async Task Writers_Concurrent_LoseNothingAndSnapshotsStayConsistent()
    {
        const int Writers = 8;
        const int PerWriter = 2000;
        IRequestTimings timings = Timings();
        using var start = new ManualResetEventSlim();
        Task[] writers = [.. Enumerable.Range(0, Writers).Select(_ => Task.Run(() =>
        {
            start.Wait(CancellationToken);
            for (int i = 0; i < PerWriter; i++)
            {
                timings.Mark("work", TimeSpan.FromTicks(1));
                timings.Count("work");
                timings.Set("label", "x");
            }
        }, CancellationToken))];
        Task reader = Task.Run(() =>
        {
            start.Wait(CancellationToken);
            for (int i = 0; i < 200; i++)
            {
                RequestTimingSnapshot snapshot = timings.Snapshot();
                long count = snapshot.Counts.GetValueOrDefault("work");
                count.Should().BeInRange(0, Writers * PerWriter);
            }
        }, CancellationToken);

        start.Set();
        await Task.WhenAll([.. writers, reader]).WaitAsync(TimeSpan.FromSeconds(30), CancellationToken);

        RequestTimingSnapshot final = timings.Snapshot();
        final.Durations["work"].Should().Be(TimeSpan.FromTicks(Writers * PerWriter));
        final.Counts["work"].Should().Be(Writers * PerWriter);
    }

    [TestMethod]
    public void Container_OneInstancePerLifetimeScope()
    {
        IServiceProvider services = GetServices();
        using IServiceScope first = services.CreateScope();
        using IServiceScope second = services.CreateScope();

        var a = first.ServiceProvider.GetRequiredService<IRequestTimings>();

        a.Should().BeSameAs(first.ServiceProvider.GetRequiredService<IRequestTimings>());
        a.Should().NotBeSameAs(second.ServiceProvider.GetRequiredService<IRequestTimings>());
        a.Count("work");
        second.ServiceProvider.GetRequiredService<IRequestTimings>().Snapshot().Counts.Should().BeEmpty();
    }

    [TestMethod]
    public void Names_CoreConstant_IsDb()
        => RequestTimingNames.Database.Should().Be("db");

    private IRequestTimings Timings()
        => GetServices().GetRequiredService<ILifetimeScope>().BeginLifetimeScope().Resolve<IRequestTimings>();
}
