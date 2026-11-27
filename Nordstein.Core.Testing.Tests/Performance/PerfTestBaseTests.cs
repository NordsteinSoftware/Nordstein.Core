using AwesomeAssertions;
using Nordstein.Core.Common.Lifecycle;
using Nordstein.Core.Testing.Performance;
using Nordstein.Core.Testing.Tests.Performance.Internal;
using NSubstitute;

namespace Nordstein.Core.Testing.Tests.Performance;

/// <summary>
/// <see cref="PerfTestBase{TModule}"/>: the gate runs as a test initializer, the budgets load only
/// behind it (lazily, once per test), and the report and budget helpers write their lines to the
/// test context.
/// </summary>
[TestClass]
public sealed class PerfTestBaseTests : BaseTest<Module>
{
    [TestMethod]
    public void RequirePerfRun_WithTheVariableUnset_SkipsAsInconclusive()
    {
        var scenario = new ExampleScenario("NORDSTEIN_CORE_TESTING_PERF_" + Guid.NewGuid().ToString("N"), "unused.json")
        {
            TestContext = TestContext,
        };

        Action act = scenario.RequirePerfRun;

        act.Should().Throw<AssertInconclusiveException>().WithMessage("*opt-in*=1 to run it*");
    }

    [TestMethod]
    public void RequirePerfRun_WithTheVariableSet_Runs()
    {
        string variable = "NORDSTEIN_CORE_TESTING_PERF_" + Guid.NewGuid().ToString("N");
        var scenario = new ExampleScenario(variable, "unused.json") { TestContext = TestContext };
        try
        {
            Environment.SetEnvironmentVariable(variable, "1");

            Action act = scenario.RequirePerfRun;

            act.Should().NotThrow();
        }
        finally
        {
            Environment.SetEnvironmentVariable(variable, null);
        }
    }

    [TestMethod]
    public void RequirePerfRun_IsATestInitializer()
        => typeof(PerfTestBase<Module>).GetMethod(nameof(PerfTestBase<Module>.RequirePerfRun))
            ?.IsDefined(typeof(TestInitializeAttribute), inherit: false)
            .Should().BeTrue("a gate called from a test body is one a new scenario can forget");

    [TestMethod]
    public void BudgetsPath_ByDefault_IsPerfBudgetsJsonBesideTheTestAssembly()
        => new DefaultPathScenario { TestContext = TestContext }.DefaultBudgetsPath
            .Should().Be(Path.Combine(AppContext.BaseDirectory, "perf-budgets.json"));

    [TestMethod]
    public void BudgetHelpers_LoadTheFileOnFirstUseAndWriteToTheTestContext()
    {
        using ITempDirectory directory = GetServices().GetTempDirectory();
        string path = Path.Combine(directory.Path, "perf-budgets.json");
        File.WriteAllText(path, """{ "scenario": { "p95Ms": 10, "perSecond": 100 } }""");
        TestContext context = Substitute.For<TestContext>();
        var scenario = new ExampleScenario("UNUSED", path) { TestContext = context };

        scenario.AtMost("scenario.p95Ms", 9, "ms");
        File.Delete(path);
        scenario.AtLeast("scenario.perSecond", 150, "/s");
        Action over = () => scenario.AtMost("scenario.p95Ms", 11, "ms");

        over.Should().Throw<AssertFailedException>().WithMessage("scenario.p95Ms has a committed budget of 10.00ms*");
        context.Received(1).WriteLine("BUDGET [scenario.p95Ms] measured 9.00ms <= budget 10.00ms?");
        context.Received(1).WriteLine("BUDGET [scenario.perSecond] measured 150.00/s >= budget 100.00/s?");
    }

    [TestMethod]
    public void BudgetHelpers_WithAMissingFile_FailOnlyWhenUsed()
    {
        var scenario = new ExampleScenario("UNUSED", Path.Combine(AppContext.BaseDirectory, $"absent-{Guid.NewGuid():N}.json"))
        {
            TestContext = Substitute.For<TestContext>(),
        };

        Action act = () => scenario.AtMost("scenario.p95Ms", 1, "ms");

        act.Should().Throw<FileNotFoundException>();
    }

    [TestMethod]
    public void Report_WritesTheMeasurementAndScalarLines()
    {
        TestContext context = Substitute.For<TestContext>();
        var scenario = new ExampleScenario("UNUSED", "unused.json") { TestContext = context };
        var measurement = new Measurement();
        measurement.Record(2);

        scenario.ReportMeasurement("listing", measurement);
        scenario.ReportValue("listing.rows", "50000");

        context.Received(1).WriteLine("MEASURED [listing] n=1 p50=2.00ms p95=2.00ms max=2.00ms mean=2.00ms");
        context.Received(1).WriteLine("MEASURED [listing.rows] 50000");
    }

    [TestMethod]
    public void Report_WithNullArguments_Throws()
    {
        var scenario = new ExampleScenario("UNUSED", "unused.json") { TestContext = Substitute.For<TestContext>() };

#pragma warning disable CS8625 // WHY: the guards under test are reachable only with the null the annotations forbid.
        Action measurement = () => scenario.ReportMeasurement("listing", null);
        Action label = () => scenario.ReportValue(null, "1");
        Action value = () => scenario.ReportValue("listing", null);
#pragma warning restore CS8625

        measurement.Should().Throw<ArgumentNullException>().WithParameterName("measurement");
        label.Should().Throw<ArgumentNullException>().WithParameterName("scenario");
        value.Should().Throw<ArgumentNullException>().WithParameterName("value");
    }
}
