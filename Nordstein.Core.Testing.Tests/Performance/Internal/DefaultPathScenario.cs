using Nordstein.Core.Testing.Performance;

namespace Nordstein.Core.Testing.Tests.Performance.Internal;

/// <summary>A perf scenario that keeps the base class's default budget path, to read it.</summary>
internal sealed class DefaultPathScenario : PerfTestBase<Module>
{
    public string DefaultBudgetsPath => BudgetsPath;

    protected override string OptInVariable => "UNUSED";
}
