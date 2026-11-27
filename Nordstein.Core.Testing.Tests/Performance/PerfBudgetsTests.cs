using System.Globalization;
using AwesomeAssertions;
using Nordstein.Core.Common.Lifecycle;
using Nordstein.Core.Testing.Performance;

namespace Nordstein.Core.Testing.Tests.Performance;

/// <summary>
/// <see cref="PerfBudgets"/>: a commented JSON file flattened to dotted keys, every malformed
/// shape refused loudly; a missing key measures without failing; a committed budget fails a
/// measurement past it with a message that names the key, both numbers and the file.
/// </summary>
[TestClass]
public sealed class PerfBudgetsTests : BaseTest<Module>
{
    private const string Budgets = """
        // Budgets for the example scenarios; each number carries its calibration.
        {
          "listing": {
            /* p95 latency, milliseconds */
            "p95Ms": { "firstPage": 12.5, "deepPage": 40 },
            "rowsPerSecond": 5000
          },
          "purgeMs": 0
        }
        """;

    [TestMethod]
    public void Constructor_FlattensNestedObjectsToDottedKeys()
    {
        using ITempDirectory directory = GetServices().GetTempDirectory();
        string path = Write(directory, Budgets);

        var budgets = new PerfBudgets(path);

        budgets.Path.Should().Be(path);
        budgets.Keys.Should().Equal("listing.p95Ms.deepPage", "listing.p95Ms.firstPage", "listing.rowsPerSecond", "purgeMs");
        budgets.Find("listing.p95Ms.firstPage").Should().Be(12.5);
        budgets.Find("listing.rowsPerSecond").Should().Be(5000);
        budgets.Find("purgeMs").Should().Be(0);
    }

    [TestMethod]
    public void Find_OfAnAbsentKeyOrAGroup_IsNull()
    {
        using ITempDirectory directory = GetServices().GetTempDirectory();
        var budgets = new PerfBudgets(Write(directory, Budgets));

        budgets.Find("listing.p95Ms.missing").Should().BeNull();
        budgets.Find("listing.p95Ms").Should().BeNull("a group is not a budget");
        budgets.Find("LISTING.ROWSPERSECOND").Should().BeNull("keys compare ordinally");
    }

    [TestMethod]
    [DataRow("")]
    [DataRow(" ")]
    public void Find_WithABlankKey_Throws(string key)
    {
        using ITempDirectory directory = GetServices().GetTempDirectory();
        var budgets = new PerfBudgets(Write(directory, Budgets));

        Action act = () => budgets.Find(key);

        act.Should().Throw<ArgumentException>().WithParameterName("key");
    }

    [TestMethod]
    public void Constructor_OfAnEmptyObject_HasNoBudgets()
    {
        using ITempDirectory directory = GetServices().GetTempDirectory();

        new PerfBudgets(Write(directory, "{ }")).Keys.Should().BeEmpty();
    }

    [TestMethod]
    public void Constructor_OfAMissingFile_ThrowsFileNotFound()
    {
        using ITempDirectory directory = GetServices().GetTempDirectory();

        Action act = () => _ = new PerfBudgets(Path.Combine(directory.Path, "absent.json"));

        act.Should().Throw<FileNotFoundException>();
    }

    [TestMethod]
    [DataRow("{ \"a\": ", "is not valid JSON")]
    [DataRow("", "is not valid JSON")]
    [DataRow("{ \"a\": 1, }", "is not valid JSON")]
    [DataRow("[1, 2]", "root must be an object")]
    [DataRow("{ \"a\": \"12\" }", "Budget 'a'*is String*budgets must be numbers")]
    [DataRow("{ \"a\": { \"b\": true } }", "Budget 'a.b'*is True*budgets must be numbers")]
    [DataRow("{ \"a\": null }", "Budget 'a'*is Null")]
    [DataRow("{ \"a\": [1] }", "Budget 'a'*is Array")]
    [DataRow("{ \"a\": -1 }", "Budget 'a'*is -1*zero or positive")]
    [DataRow("{ \"a\": 1e400 }", "Budget 'a'*is not a finite number")]
    [DataRow("{ \"a.b\": 1 }", "name 'a.b' may not contain a dot")]
    [DataRow("{ \"a\": { \"\": 1 } }", "'a.' has an empty name")]
    [DataRow("{ \"a\": 1, \"a\": 2 }", "Budget 'a' is declared twice")]
    public void Constructor_OfAMalformedFile_ThrowsInvalidDataNamingTheProblem(string content, string problem)
    {
        using ITempDirectory directory = GetServices().GetTempDirectory();
        string path = Write(directory, content);

        Action act = () => _ = new PerfBudgets(path);

        act.Should().Throw<InvalidDataException>().WithMessage($"*{path}*").And.Message.Should().Match($"*{problem}*");
    }

    [TestMethod]
    public void Constructor_WithABlankPath_Throws()
    {
#pragma warning disable CS8625 // WHY: the guard under test is reachable only with the null the annotations forbid.
        Action nullPath = () => _ = new PerfBudgets(null);
#pragma warning restore CS8625
        Action blank = () => _ = new PerfBudgets(" ");

        nullPath.Should().Throw<ArgumentNullException>().WithParameterName("path");
        blank.Should().Throw<ArgumentException>().WithParameterName("path");
    }

    [TestMethod]
    public void ShouldBeAtMost_WithinTheBudget_LogsTheComparisonAndPasses()
    {
        using ITempDirectory directory = GetServices().GetTempDirectory();
        var budgets = new PerfBudgets(Write(directory, Budgets));
        var log = new List<string>();

        budgets.ShouldBeAtMost("listing.p95Ms.firstPage", 12.5, "ms", log.Add);

        log.Should().Equal("BUDGET [listing.p95Ms.firstPage] measured 12.50ms <= budget 12.50ms?");
    }

    [TestMethod]
    public void ShouldBeAtMost_PastTheBudget_FailsNamingTheKeyTheNumbersAndTheFile()
    {
        using ITempDirectory directory = GetServices().GetTempDirectory();
        var budgets = new PerfBudgets(Write(directory, Budgets));
        var log = new List<string>();

        Action act = () => budgets.ShouldBeAtMost("listing.p95Ms.firstPage", 13.004, "ms", log.Add);

        act.Should().Throw<AssertFailedException>().Which.Message.Should().Be(
            "listing.p95Ms.firstPage has a committed budget of 12.50ms and measured 13.00ms; read its calibration and "
            + "regression signature in perf-budgets.json before assuming the budget is wrong.");
        log.Should().ContainSingle().Which.Should().StartWith("BUDGET [listing.p95Ms.firstPage]");
    }

    [TestMethod]
    public void ShouldBeAtLeast_BelowTheFloor_FailsNamingTheFloor()
    {
        using ITempDirectory directory = GetServices().GetTempDirectory();
        var budgets = new PerfBudgets(Write(directory, Budgets));
        var log = new List<string>();

        Action below = () => budgets.ShouldBeAtLeast("listing.rowsPerSecond", 4999.5, "/s", log.Add);

        below.Should().Throw<AssertFailedException>().WithMessage(
            "listing.rowsPerSecond has a committed floor of 5000.00/s and measured 4999.50/s;*the floor is wrong.");
        budgets.ShouldBeAtLeast("listing.rowsPerSecond", 5000, "/s", log.Add);
        log.Should().Equal(
            "BUDGET [listing.rowsPerSecond] measured 4999.50/s >= budget 5000.00/s?",
            "BUDGET [listing.rowsPerSecond] measured 5000.00/s >= budget 5000.00/s?");
    }

    [TestMethod]
    public void Assertions_WithoutACommittedBudget_MeasureOnlyAndPass()
    {
        using ITempDirectory directory = GetServices().GetTempDirectory();
        var budgets = new PerfBudgets(Write(directory, Budgets));
        var log = new List<string>();

        budgets.ShouldBeAtMost("listing.p95Ms.newScenario", 1e6, "ms", log.Add);
        budgets.ShouldBeAtLeast("listing.newThroughput", 0, "/s", log.Add);

        log.Should().Equal(
            "MEASURE-ONLY [listing.p95Ms.newScenario] 1000000.00ms — no budget committed yet.",
            "MEASURE-ONLY [listing.newThroughput] 0.00/s — no budget committed yet.");
    }

    [TestMethod]
    public void Assertions_FormatInvariantly()
    {
        using ITempDirectory directory = GetServices().GetTempDirectory();
        var budgets = new PerfBudgets(Write(directory, Budgets));
        var log = new List<string>();
        CultureInfo original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("de-AT");

            budgets.ShouldBeAtMost("listing.p95Ms.deepPage", 39.5, "ms", log.Add);
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }

        log.Should().Equal("BUDGET [listing.p95Ms.deepPage] measured 39.50ms <= budget 40.00ms?");
    }

    [TestMethod]
    [DataRow(double.NaN)]
    [DataRow(double.PositiveInfinity)]
    [DataRow(double.NegativeInfinity)]
    public void Assertions_OfANonFiniteMeasurement_Throw(double measured)
    {
        using ITempDirectory directory = GetServices().GetTempDirectory();
        var budgets = new PerfBudgets(Write(directory, Budgets));

        Action atMost = () => budgets.ShouldBeAtMost("purgeMs", measured, "ms", _ => { });
        Action atLeast = () => budgets.ShouldBeAtLeast("listing.rowsPerSecond", measured, "/s", _ => { });

        atMost.Should().Throw<ArgumentOutOfRangeException>().WithParameterName("measured");
        atLeast.Should().Throw<ArgumentOutOfRangeException>().WithParameterName("measured");
    }

    [TestMethod]
    public void Assertions_WithNullArguments_Throw()
    {
        using ITempDirectory directory = GetServices().GetTempDirectory();
        var budgets = new PerfBudgets(Write(directory, Budgets));

#pragma warning disable CS8625 // WHY: the guards under test are reachable only with the null the annotations forbid.
        Action key = () => budgets.ShouldBeAtMost(null, 1, "ms", _ => { });
        Action unit = () => budgets.ShouldBeAtMost("purgeMs", 1, null, _ => { });
        Action log = () => budgets.ShouldBeAtLeast("purgeMs", 1, "ms", null);
#pragma warning restore CS8625

        key.Should().Throw<ArgumentNullException>().WithParameterName("key");
        unit.Should().Throw<ArgumentNullException>().WithParameterName("unit");
        log.Should().Throw<ArgumentNullException>().WithParameterName("log");
    }

    private static string Write(ITempDirectory directory, string content)
    {
        string path = Path.Combine(directory.Path, "perf-budgets.json");
        File.WriteAllText(path, content);
        return path;
    }
}
