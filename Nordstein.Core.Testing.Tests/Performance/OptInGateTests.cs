using AwesomeAssertions;
using Nordstein.Core.Testing.Performance;

namespace Nordstein.Core.Testing.Tests.Performance;

/// <summary>
/// <see cref="OptInGate"/>: an opt-in suite runs only when its variable is exactly <c>1</c>; unset,
/// empty or <c>0</c> report the test as skipped (inconclusive), never as passed; any other value
/// fails loudly instead of skipping silently.
/// </summary>
[TestClass]
public sealed class OptInGateTests
{
    private const string Variable = "EXAMPLE_RUN_SLOW_TESTS";

    [TestMethod]
    public void Unset_IsClosedAndSkipsAsInconclusive()
    {
        var gate = new OptInGate(Variable, _ => null);

        Action act = gate.SkipUnlessOpen;

        gate.IsOpen.Should().BeFalse();
        act.Should().Throw<AssertInconclusiveException>()
            .WithMessage($"*opt-in*set {Variable}=1 to run it*");
    }

    [TestMethod]
    [DataRow("")]
    [DataRow("0")]
    public void EmptyOrZero_IsClosedAndSkips(string value)
    {
        var gate = new OptInGate(Variable, _ => value);

        Action act = gate.SkipUnlessOpen;

        gate.IsOpen.Should().BeFalse();
        act.Should().Throw<AssertInconclusiveException>();
    }

    [TestMethod]
    public void One_IsOpenAndRuns()
    {
        var gate = new OptInGate(Variable, _ => "1");

        Action act = gate.SkipUnlessOpen;

        gate.IsOpen.Should().BeTrue();
        act.Should().NotThrow();
    }

    [TestMethod]
    [DataRow("true")]
    [DataRow("yes")]
    [DataRow(" 1")]
    [DataRow("2")]
    public void AnyOtherValue_FailsLoudlyInsteadOfSkipping(string value)
    {
        var gate = new OptInGate(Variable, _ => value);

        Action read = () => _ = gate.IsOpen;
        Action skip = gate.SkipUnlessOpen;

        read.Should().Throw<InvalidOperationException>().WithMessage($"*{Variable}*'{value}'*1 runs*unset, empty or 0 skips*");
        skip.Should().Throw<InvalidOperationException>();
    }

    [TestMethod]
    public void Gate_ReadsItsOwnVariableEveryTime()
    {
        var asked = new List<string>();
        string? value = null;
        var gate = new OptInGate(Variable, name =>
        {
            asked.Add(name);
            return value;
        });

        bool before = gate.IsOpen;
        value = "1";
        bool after = gate.IsOpen;

        (before, after).Should().Be((false, true));
        asked.Should().Equal(Variable, Variable);
        gate.VariableName.Should().Be(Variable);
    }

    [TestMethod]
    public void Gate_OnTheProcessEnvironment_ReadsTheRealVariable()
    {
        string variable = "NORDSTEIN_CORE_TESTING_GATE_" + Guid.NewGuid().ToString("N");
        var gate = new OptInGate(variable);
        try
        {
            gate.IsOpen.Should().BeFalse("the variable is unset");

            Environment.SetEnvironmentVariable(variable, "1");

            gate.IsOpen.Should().BeTrue();
        }
        finally
        {
            Environment.SetEnvironmentVariable(variable, null);
        }
    }

    [TestMethod]
    [DataRow("")]
    [DataRow(" ")]
    [DataRow("WITH=EQUALS")]
    public void Constructor_WithAnUnusableVariableName_Throws(string name)
    {
        Action act = () => _ = new OptInGate(name);

        act.Should().Throw<ArgumentException>().WithParameterName("variableName");
    }

    [TestMethod]
    public void Constructor_WithNullArguments_Throws()
    {
#pragma warning disable CS8625 // WHY: the guards under test are reachable only with the null the annotations forbid.
        Action name = () => _ = new OptInGate(null);
        Action reader = () => _ = new OptInGate(Variable, null);
#pragma warning restore CS8625

        name.Should().Throw<ArgumentNullException>().WithParameterName("variableName");
        reader.Should().Throw<ArgumentNullException>().WithParameterName("readVariable");
    }
}
