using System.Globalization;
using AwesomeAssertions;
using Nordstein.Core.Testing.Performance;
using Nordstein.Core.Testing.Time;

namespace Nordstein.Core.Testing.Tests.Performance;

/// <summary>
/// <see cref="Measurement"/>: nearest-rank percentiles over recorded latencies, timing through the
/// given <see cref="TimeProvider"/>, loud on an empty or invalid measurement, safe across threads.
/// </summary>
[TestClass]
public sealed class MeasurementTests
{
    [TestMethod]
    public void Percentiles_AreNearestRank_AlwaysAnObservedSample()
    {
        var measurement = new Measurement();
        foreach (int sample in Enumerable.Range(1, 20).Reverse())
        {
            measurement.Record(sample);
        }

        measurement.Count.Should().Be(20);
        measurement.P50Ms.Should().Be(10);
        measurement.P95Ms.Should().Be(19);
        measurement.MaxMs.Should().Be(20);
        measurement.MeanMs.Should().Be(10.5);
        measurement.Percentile(0.01).Should().Be(1);
    }

    [TestMethod]
    public void Percentile_DoesNotRoundAQuantileUpAcrossAFloatingPointError()
    {
        // 0.07 * 100 is 7.000000000000001 in binary floating point: a naive ceiling picks rank 8.
        var measurement = new Measurement();
        foreach (int sample in Enumerable.Range(1, 100))
        {
            measurement.Record(sample);
        }

        measurement.Percentile(0.07).Should().Be(7);
        measurement.P95Ms.Should().Be(95);
    }

    [TestMethod]
    public void Percentiles_OfOneSample_AreThatSample()
    {
        var measurement = new Measurement();
        measurement.Record(4.25);

        (measurement.P50Ms, measurement.P95Ms, measurement.MaxMs, measurement.MeanMs).Should().Be((4.25, 4.25, 4.25, 4.25));
    }

    [TestMethod]
    public void Statistics_OfAnEmptyMeasurement_Throw()
    {
        // An empty measurement's p95 of zero would pass every budget: a scenario that timed nothing
        // must fail, not pass.
        var measurement = new Measurement();

        Action p95 = () => _ = measurement.P95Ms;
        Action mean = () => _ = measurement.MeanMs;

        p95.Should().Throw<InvalidOperationException>().WithMessage("*no samples*");
        mean.Should().Throw<InvalidOperationException>().WithMessage("*no samples*");
        measurement.Count.Should().Be(0);
    }

    [TestMethod]
    [DataRow(0.0)]
    [DataRow(-0.5)]
    [DataRow(1.0000001)]
    [DataRow(double.NaN)]
    public void Percentile_OutsideZeroToOne_Throws(double quantile)
    {
        var measurement = new Measurement();
        measurement.Record(1);

        Action act = () => measurement.Percentile(quantile);

        act.Should().Throw<ArgumentOutOfRangeException>().WithParameterName("quantile");
    }

    [TestMethod]
    [DataRow(-0.001)]
    [DataRow(double.NaN)]
    [DataRow(double.PositiveInfinity)]
    public void Record_OfANegativeOrNonFiniteLatency_Throws(double elapsed)
    {
        var measurement = new Measurement();

        Action act = () => measurement.Record(elapsed);

        act.Should().Throw<ArgumentOutOfRangeException>().WithParameterName("elapsedMs");
        measurement.Count.Should().Be(0);
    }

    [TestMethod]
    public void Record_OfZero_IsASample()
    {
        var measurement = new Measurement();

        measurement.Record(0);

        measurement.MaxMs.Should().Be(0);
    }

    [TestMethod]
    public async Task TimeAsync_RecordsTheElapsedTimeOfTheClockAndReturnsTheResult()
    {
        var clock = new ManualClock();
        var measurement = new Measurement(clock);

        int result = await measurement.TimeAsync(() =>
        {
            clock.Advance(TimeSpan.FromMilliseconds(12.5));
            return Task.FromResult(42);
        });
        await measurement.TimeAsync(() =>
        {
            clock.Advance(TimeSpan.FromMilliseconds(3));
            return Task.CompletedTask;
        });

        result.Should().Be(42);
        measurement.Count.Should().Be(2);
        measurement.MaxMs.Should().Be(12.5);
        measurement.Percentile(0.5).Should().Be(3);
    }

    [TestMethod]
    public async Task TimeAsync_WhenTheOperationThrows_RecordsNothingAndPropagates()
    {
        var measurement = new Measurement(new ManualClock());

        await FluentActions.Awaiting(() => measurement.TimeAsync(() => Task.FromException<int>(new InvalidOperationException("boom"))))
            .Should().ThrowAsync<InvalidOperationException>().WithMessage("boom");
        await FluentActions.Awaiting(() => measurement.TimeAsync(() => Task.FromException(new TimeoutException())))
            .Should().ThrowAsync<TimeoutException>();

        measurement.Count.Should().Be(0);
    }

    [TestMethod]
    public async Task TimeAsync_OnTheSystemClock_RecordsANonNegativeLatency()
    {
        var measurement = new Measurement();

        await measurement.TimeAsync(() => Task.Delay(1));

        measurement.MaxMs.Should().BeGreaterThanOrEqualTo(0);
    }

    [TestMethod]
    public void Describe_SummarisesInvariantly()
    {
        CultureInfo original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("de-AT");
            var measurement = new Measurement();
            measurement.Record(1.5);
            measurement.Record(2.25);

            measurement.Describe().Should().Be("n=2 p50=1.50ms p95=2.25ms max=2.25ms mean=1.88ms");
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }

    [TestMethod]
    public void Describe_OfAnEmptyMeasurement_SaysSoWithoutThrowing()
        => new Measurement().Describe().Should().Be("n=0 (no samples)");

    [TestMethod]
    public async Task Record_FromManyThreads_LosesNoSample()
    {
        var measurement = new Measurement();

        await Task.WhenAll(Enumerable.Range(0, 16).Select(writer => Task.Run(() =>
        {
            for (int i = 0; i < 1000; i++)
            {
                measurement.Record(writer);
                _ = measurement.Count;
            }
        })));

        measurement.Count.Should().Be(16_000);
        measurement.MaxMs.Should().Be(15);
    }

    [TestMethod]
    public async Task NullArguments_Throw()
    {
        var measurement = new Measurement();

#pragma warning disable CS8600, CS8625 // WHY: the guards under test are reachable only with the null the annotations forbid.
        Action provider = () => _ = new Measurement(null);
        Func<Task> valued = () => measurement.TimeAsync((Func<Task<int>>)null);
        Func<Task> plain = () => measurement.TimeAsync((Func<Task>)null);
#pragma warning restore CS8600, CS8625

        provider.Should().Throw<ArgumentNullException>().WithParameterName("timeProvider");
        await valued.Should().ThrowAsync<ArgumentNullException>().WithParameterName("operation");
        await plain.Should().ThrowAsync<ArgumentNullException>().WithParameterName("operation");
    }
}
