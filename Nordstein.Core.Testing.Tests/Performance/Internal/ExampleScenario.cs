using Nordstein.Core.Testing.Performance;

namespace Nordstein.Core.Testing.Tests.Performance.Internal;

/// <summary>
/// A perf scenario with a chosen opt-in variable and budget file, never run by MSTest itself (it
/// is not a test class): <c>PerfTestBaseTests</c> calls its helpers directly.
/// </summary>
internal sealed class ExampleScenario : PerfTestBase<Module>
{
    private readonly string variable;
    private readonly string budgetsPath;

    public ExampleScenario(string variable, string budgetsPath)
    {
        this.variable = variable;
        this.budgetsPath = budgetsPath;
    }

    protected override string OptInVariable => variable;

    protected override string BudgetsPath => budgetsPath;

    public void AtMost(string key, double measured, string unit) => ShouldBeAtMostBudget(key, measured, unit);

    public void AtLeast(string key, double measured, string unit) => ShouldBeAtLeastBudget(key, measured, unit);

    public void ReportMeasurement(string scenario, Measurement measurement) => Report(scenario, measurement);

    public void ReportValue(string scenario, string value) => Report(scenario, value);
}
