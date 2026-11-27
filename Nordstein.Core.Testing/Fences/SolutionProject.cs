using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Text.RegularExpressions;

namespace Nordstein.Core.Testing.Fences;

/// <summary>
/// One project a <see cref="SolutionFile"/> lists: its name, its paths, its assembly name and
/// whether it is a test project, and the means to load its assembly into the test host for a scan.
/// </summary>
/// <remarks>
/// Immutable; safe to read from any thread. <see cref="AssemblyName"/> reads the project file on
/// first use and keeps the answer.
/// </remarks>
public sealed partial class SolutionProject
{
    private readonly SolutionFile solution;
    private readonly Lazy<string> assemblyName;

    internal SolutionProject(SolutionFile solution, string name, string relativePath)
    {
        this.solution = solution;
        Name = name;
        RelativePath = relativePath;
        FullPath = Path.GetFullPath(Path.Combine(solution.Directory, relativePath));
        Directory = Path.GetFullPath(Path.Combine(FullPath, ".."));
        assemblyName = new Lazy<string>(ReadAssemblyName, LazyThreadSafetyMode.ExecutionAndPublication);
    }

    /// <summary>The project's name: the one a <c>.sln</c> gives it, for a <c>.slnx</c> the project file's name without extension.</summary>
    public string Name { get; }

    /// <summary>The project file's path relative to the solution's directory, with <c>/</c> separators.</summary>
    public string RelativePath { get; }

    /// <summary>The project file's full path.</summary>
    public string FullPath { get; }

    /// <summary>The full path of the directory holding the project file.</summary>
    public string Directory { get; }

    /// <summary>
    /// Whether the project is a test project by the Nordstein naming convention: its name ends in
    /// <c>Tests</c> (<c>Product.Area.Tests</c>, <c>Product.PerfTests</c>). Test-support projects that
    /// ship no production code but are named otherwise (a shared harness) are the caller's to exclude.
    /// </summary>
    public bool IsTestProject => Name.EndsWith("Tests", StringComparison.Ordinal);

    /// <summary>
    /// The assembly the project builds: the project file's <c>&lt;AssemblyName&gt;</c>, else the
    /// project's <see cref="Name"/>. Only the project file itself is read; a name set in an imported
    /// file (<c>Directory.Build.props</c>) is not seen.
    /// </summary>
    /// <exception cref="FileNotFoundException">The project file does not exist.</exception>
    /// <exception cref="InvalidOperationException">
    /// The project file declares more than one different assembly name, or one that needs MSBuild to
    /// evaluate (it holds <c>$(</c>).
    /// </exception>
    public string AssemblyName => assemblyName.Value;

    /// <summary>
    /// Loads the project's assembly by <see cref="AssemblyName"/> into the test host.
    /// </summary>
    /// <returns>The loaded assembly.</returns>
    /// <exception cref="AssertFailedException">
    /// The assembly is not loadable from the test host, because the test project does not reference
    /// the project: the message names the project and the remedy, and the
    /// <see cref="FileNotFoundException"/> is the inner exception.
    /// </exception>
    public Assembly LoadAssembly()
    {
        try
        {
            return Assembly.Load(new AssemblyName(AssemblyName));
        }
        catch (FileNotFoundException exception)
        {
            throw new AssertFailedException(
                $"{Name} is listed in {Path.GetFileName(solution.Path)} but its assembly {AssemblyName} is not loadable "
                + "from this test host, so a fence cannot scan it. Add a ProjectReference to it in the test project "
                + "that runs the fence.",
                exception);
        }
    }

    /// <summary>
    /// Loads the project's assembly by <see cref="AssemblyName"/> if the test host can.
    /// </summary>
    /// <param name="assembly">The loaded assembly, or null when it is not loadable.</param>
    /// <returns>Whether the assembly was loaded.</returns>
    public bool TryLoadAssembly([NotNullWhen(true)] out Assembly? assembly)
    {
        try
        {
            assembly = Assembly.Load(new AssemblyName(AssemblyName));
            return true;
        }
        catch (FileNotFoundException)
        {
            assembly = null;
            return false;
        }
    }

    /// <summary>The name and relative path, as in <c>Product.Area (src/Product.Area/Product.Area.csproj)</c>.</summary>
    public override string ToString() => $"{Name} ({RelativePath})";

    private string ReadAssemblyName()
    {
        if (!File.Exists(FullPath))
        {
            throw new FileNotFoundException($"{Name}'s project file {RelativePath} does not exist.", FullPath);
        }

        string[] declared = AssemblyNameElement().Matches(File.ReadAllText(FullPath))
            .Select(match => match.Groups["name"].Value.Trim())
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (declared.Length > 1)
        {
            throw new InvalidOperationException(
                $"{RelativePath} declares more than one AssemblyName ({string.Join(", ", declared)}); a fence cannot tell which it builds.");
        }

        if (declared.Length == 0)
        {
            return Name;
        }

        if (declared[0].Contains("$(", StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"{RelativePath} sets AssemblyName to '{declared[0]}', which needs MSBuild to evaluate; a fence cannot read it.");
        }

        return declared[0];
    }

    [GeneratedRegex("<AssemblyName>(?<name>[^<]+)</AssemblyName>")]
    private static partial Regex AssemblyNameElement();
}
