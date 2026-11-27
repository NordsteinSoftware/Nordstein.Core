namespace Nordstein.Core.Testing.Performance;

/// <summary>
/// The switch that keeps an expensive opt-in suite (performance scenarios, long soak tests) out of
/// an ordinary <c>dotnet test</c> run: a test it gates runs only when an environment variable is
/// exactly <c>1</c>, and otherwise reports as skipped (inconclusive), never as passed.
/// </summary>
/// <remarks>
/// <para>
/// The variable is read on every call, from the process environment. Unset, empty and <c>0</c>
/// close the gate. Any other value (<c>true</c>, <c>yes</c>, <c> 1</c>) throws
/// <see cref="InvalidOperationException"/>: a run that meant to opt in with the wrong spelling fails
/// loudly instead of skipping the suite it asked for.
/// </para>
/// <para>
/// Gate in a <c>[TestInitialize]</c>, never in a test body: a body that forgets the call puts the
/// suite back into every run. <see cref="PerfTestBase{TModule}"/> does exactly that. Immutable;
/// safe to use from any thread.
/// </para>
/// </remarks>
public sealed class OptInGate
{
    private readonly Func<string, string?> readVariable;

    /// <summary>
    /// Creates a gate on the process environment variable <paramref name="variableName"/>.
    /// </summary>
    /// <param name="variableName">The variable that opts in, such as <c>EXAMPLE_RUN_PERF_TESTS</c>.</param>
    /// <exception cref="ArgumentNullException"><paramref name="variableName"/> is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="variableName"/> is blank or contains <c>=</c>.</exception>
    public OptInGate(string variableName)
        : this(variableName, Environment.GetEnvironmentVariable)
    {
    }

    internal OptInGate(string variableName, Func<string, string?> readVariable)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(variableName);
        ArgumentNullException.ThrowIfNull(readVariable);
        if (variableName.Contains('=', StringComparison.Ordinal))
        {
            throw new ArgumentException("An environment variable's name cannot contain '='.", nameof(variableName));
        }

        VariableName = variableName;
        this.readVariable = readVariable;
    }

    /// <summary>The environment variable that opts in.</summary>
    public string VariableName { get; }

    /// <summary>
    /// Whether the run opted in: the variable is exactly <c>1</c>.
    /// </summary>
    /// <exception cref="InvalidOperationException">The variable holds a value other than unset, empty, <c>0</c> or <c>1</c>.</exception>
    public bool IsOpen
        => readVariable(VariableName) switch
        {
            "1" => true,
            null or "" or "0" => false,
            { } other => throw new InvalidOperationException(
                $"{VariableName} is '{other}'; 1 runs the opt-in tests, unset, empty or 0 skips them."),
        };

    /// <summary>
    /// Ends the calling test as inconclusive (reported as skipped) unless the run opted in.
    /// </summary>
    /// <exception cref="AssertInconclusiveException">The gate is closed.</exception>
    /// <exception cref="InvalidOperationException">The variable holds a value other than unset, empty, <c>0</c> or <c>1</c>.</exception>
    public void SkipUnlessOpen()
    {
        if (!IsOpen)
        {
            Assert.Inconclusive($"This test is opt-in: set {VariableName}=1 to run it. Unset, empty or 0 skips it.");
        }
    }
}
