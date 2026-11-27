using System.Globalization;

namespace Nordstein.Core.Testing.Performance;

/// <summary>
/// The base of a performance scenario: the opt-in gate as a test initializer, the committed budgets
/// loaded behind it, and the report-then-assert helpers that turn a <see cref="Measurement"/> into a
/// pass or a defect, each writing its line to the test log.
/// </summary>
/// <typeparam name="TModule">The Autofac module that wires the code under measurement.</typeparam>
/// <remarks>
/// <para>
/// <b>The gate.</b> <see cref="RequirePerfRun"/> is a <c>[TestInitialize]</c>: every scenario ends
/// inconclusive (skipped) unless <see cref="OptInVariable"/> is <c>1</c>, before it builds a
/// container or seeds a row, so an ordinary solution-wide test run never runs, and never passes, a
/// performance test. MSTest runs <see cref="BaseTest{TModule}.Initialize"/> first, so the skip
/// strands no container.
/// </para>
/// <para>
/// <b>The budgets</b> are read from <see cref="BudgetsPath"/> on first use and kept for the rest of
/// the test (a fresh instance per test method: never across tests). Reading them lazily keeps a
/// missing or malformed budget file behind the gate: a gated run never touches it.
/// </para>
/// <para>
/// Like <see cref="BaseTest{TModule}"/>, a scenario keeps no state in fields: it builds its
/// measurement, its data and its container inside the test method.
/// </para>
/// </remarks>
public abstract class PerfTestBase<TModule> : BaseTest<TModule>
    where TModule : Autofac.Module, new()
{
    private PerfBudgets? budgets;

    /// <summary>
    /// The environment variable that opts a run into the performance suite, such as
    /// <c>EXAMPLE_RUN_PERF_TESTS</c>; one per product, deliberately not the variable that requires
    /// container tests, so a pipeline that enables those does not also start the performance suite.
    /// </summary>
    protected abstract string OptInVariable { get; }

    /// <summary>
    /// The budget file: <c>perf-budgets.json</c> beside the test assembly unless overridden (copy
    /// it to the output directory from the test project).
    /// </summary>
    protected virtual string BudgetsPath => Path.Combine(AppContext.BaseDirectory, "perf-budgets.json");

    /// <summary>
    /// Ends the scenario as inconclusive unless the run opted in (<see cref="OptInGate"/>).
    /// </summary>
    /// <exception cref="AssertInconclusiveException">The run did not opt in.</exception>
    /// <exception cref="InvalidOperationException">The variable holds a value other than unset, empty, <c>0</c> or <c>1</c>.</exception>
    [TestInitialize]
    public void RequirePerfRun() => new OptInGate(OptInVariable).SkipUnlessOpen();

    /// <summary>
    /// Writes <c>MEASURED [scenario] n=… p50=… p95=… max=… mean=…</c> to the test log.
    /// </summary>
    /// <param name="scenario">The label of the measured operation.</param>
    /// <param name="measurement">The measurement to summarise.</param>
    /// <exception cref="ArgumentNullException"><paramref name="scenario"/> or <paramref name="measurement"/> is null.</exception>
    protected void Report(string scenario, Measurement measurement)
    {
        ArgumentNullException.ThrowIfNull(measurement);
        Report(scenario, measurement.Describe());
    }

    /// <summary>
    /// Writes <c>MEASURED [scenario] value</c> to the test log, for a scalar such as a row count or
    /// a throughput.
    /// </summary>
    /// <param name="scenario">The label of the value.</param>
    /// <param name="value">The value as text.</param>
    /// <exception cref="ArgumentNullException"><paramref name="scenario"/> or <paramref name="value"/> is null.</exception>
    protected void Report(string scenario, string value)
    {
        ArgumentNullException.ThrowIfNull(scenario);
        ArgumentNullException.ThrowIfNull(value);
        TestContext.WriteLine(string.Create(CultureInfo.InvariantCulture, $"MEASURED [{scenario}] {value}"));
    }

    /// <summary>
    /// Asserts a latency against its committed budget (<see cref="PerfBudgets.ShouldBeAtMost"/>),
    /// writing the comparison or <c>MEASURE-ONLY</c> to the test log.
    /// </summary>
    /// <param name="budgetKey">The budget's dotted key.</param>
    /// <param name="measured">The measured value, such as <see cref="Measurement.P95Ms"/>.</param>
    /// <param name="unit">The unit printed after each number, such as <c>ms</c>.</param>
    /// <exception cref="AssertFailedException">The value is above the committed budget.</exception>
    /// <exception cref="FileNotFoundException">The budget file does not exist.</exception>
    /// <exception cref="InvalidDataException">The budget file is malformed.</exception>
    protected void ShouldBeAtMostBudget(string budgetKey, double measured, string unit)
        => Budgets.ShouldBeAtMost(budgetKey, measured, unit, TestContext.WriteLine);

    /// <summary>
    /// Asserts a throughput against its committed floor (<see cref="PerfBudgets.ShouldBeAtLeast"/>),
    /// writing the comparison or <c>MEASURE-ONLY</c> to the test log.
    /// </summary>
    /// <param name="budgetKey">The floor's dotted key.</param>
    /// <param name="measured">The measured value.</param>
    /// <param name="unit">The unit printed after each number, such as <c>/s</c>.</param>
    /// <exception cref="AssertFailedException">The value is below the committed floor.</exception>
    /// <exception cref="FileNotFoundException">The budget file does not exist.</exception>
    /// <exception cref="InvalidDataException">The budget file is malformed.</exception>
    protected void ShouldBeAtLeastBudget(string budgetKey, double measured, string unit)
        => Budgets.ShouldBeAtLeast(budgetKey, measured, unit, TestContext.WriteLine);

    private PerfBudgets Budgets => budgets ??= new PerfBudgets(BudgetsPath);
}
