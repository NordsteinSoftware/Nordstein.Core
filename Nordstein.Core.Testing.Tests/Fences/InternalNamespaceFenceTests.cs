using System.Reflection;
using System.Text.RegularExpressions;
using AwesomeAssertions;
using Nordstein.Core.Testing.Fences;

namespace Nordstein.Core.Testing.Tests.Fences;

/// <summary>
/// The <c>Internal/</c> fence on Core itself: every internal type of a shipped package lives in an
/// <c>Internal/</c> folder with the matching <c>.Internal</c> namespace. Two checks make the rule:
/// the compiled assemblies put every internal type in an <c>Internal</c> namespace
/// (<see cref="FenceScanExtensions.InternalTypesOutsideInternalNamespaces"/>), and every source
/// file of a shipped package declares the namespace of its folder, so the namespace is the folder.
/// The scope is every project of <c>Nordstein.Core.sln</c> that is not a test project, the
/// <c>Nordstein.Core.Testing</c> harness included: it ships as a package too. The planted types in
/// <c>Fences/</c> are the positive controls (<see cref="FenceScanExtensionsTests"/>).
/// </summary>
[TestClass]
public sealed partial class InternalNamespaceFenceTests
{
    private const string SolutionName = "Nordstein.Core.sln";

    [TestMethod]
    public void ShippedPackages_KeepEveryInternalTypeInAnInternalNamespace()
        => ShippedProjects()
            .Select(project => project.LoadAssembly())
            .SelectMany(assembly => assembly.InternalTypesOutsideInternalNamespaces())
            .Should().Equal(
                Array.Empty<string>(),
                "an internal type is an implementation detail and lives in its concept's Internal/ folder, "
                + "namespace ….Internal; the folder's root holds only the public surface");

    [TestMethod]
    public void ShippedPackages_SourceFilesDeclareTheNamespaceOfTheirFolder()
    {
        string[] mismatches = ShippedProjects()
            .SelectMany(project => Directory
                .EnumerateFiles(project.Directory, "*.cs", SearchOption.AllDirectories)
                .Where(file => !IsBuildOutput(project.Directory, file))
                .Select(file => Mismatch(project, file)))
            .OfType<string>()
            .Order(StringComparer.Ordinal)
            .ToArray();

        mismatches.Should().Equal(
            Array.Empty<string>(),
            "a file's namespace is its project's name plus its folder path, so a type in an .Internal "
            + "namespace sits in an Internal/ folder");
    }

    [TestMethod]
    public void ShippedPackages_AllLoadInThisTestHost()
    {
        SolutionProject[] shipped = ShippedProjects();

        shipped.Should().NotBeEmpty($"{SolutionName} lists the shipped packages");
        shipped.Where(project => !project.TryLoadAssembly(out _))
            .Select(project => project.Name)
            .Should().Equal(
                Array.Empty<string>(),
                "every shipped package is scanned here; add a ProjectReference to Nordstein.Core.Testing.Tests "
                + "for a new one");
    }

    [TestMethod]
    public void ProjectFilesOnDisk_AreExactlyTheSolutionsProjects()
    {
        var solution = new SolutionFile(SolutionName, AppContext.BaseDirectory);

        string[] onDisk = Directory.EnumerateDirectories(solution.Directory)
            .SelectMany(directory => Directory.EnumerateFiles(directory, "*.csproj"))
            .Select(path => Path.GetRelativePath(solution.Directory, path).Replace('\\', '/'))
            .Order(StringComparer.Ordinal)
            .ToArray();

        onDisk.Should().Equal(
            solution.Projects.Select(project => project.RelativePath),
            "a project left out of the solution would escape every fence derived from it");
    }

    [TestMethod]
    public void Mismatch_OfAPlantedFileInTheWrongFolder_IsReported()
    {
        var solution = new SolutionFile(SolutionName, AppContext.BaseDirectory);
        SolutionProject project = solution.Projects.Single(candidate => candidate.Name == "Nordstein.Core.Testing.Tests");

        Mismatch(project, Path.Combine(project.Directory, "Fences", "PlantedMisplacedTypes.cs"))
            .Should().BeNull("the planted types file declares its folder's namespace");
        Mismatch(project, Path.Combine(project.Directory, "Fences", "PlantedGlobalType.cs"))
            .Should().BeNull("a file without a namespace declaration has no namespace to compare");
        Mismatch(project, Path.Combine(project.Directory, "Fences", "Internal", "..", "Internalised", "PlantedLookalike.cs"))
            .Should().BeNull();
        MismatchOf(project, "Fences/Internal/Moved.cs", "namespace Nordstein.Core.Testing.Tests.Fences;\n")
            .Should().Be("Nordstein.Core.Testing.Tests/Fences/Internal/Moved.cs declares Nordstein.Core.Testing.Tests.Fences, "
                + "its folder is Nordstein.Core.Testing.Tests.Fences.Internal");
    }

    private static SolutionProject[] ShippedProjects()
        => [.. new SolutionFile(SolutionName, AppContext.BaseDirectory).Projects.Where(project => !project.IsTestProject)];

    private static bool IsBuildOutput(string projectDirectory, string file)
    {
        string first = Path.GetRelativePath(projectDirectory, file).Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)[0];
        return first is "bin" or "obj";
    }

    private static string? Mismatch(SolutionProject project, string file)
        => MismatchOf(project, Path.GetRelativePath(project.Directory, file).Replace('\\', '/'), File.ReadAllText(file));

    /// <summary>
    /// Core declares file-scoped namespaces only (an error otherwise, .editorconfig), so the one
    /// <c>namespace X;</c> line is the file's namespace. A file without one (global usings) declares
    /// no type to place.
    /// </summary>
    private static string? MismatchOf(SolutionProject project, string relativeFile, string source)
    {
        Match declared = FileScopedNamespace().Match(source);
        if (!declared.Success)
        {
            return null;
        }

        string folder = Path.GetDirectoryName(relativeFile)?.Replace('\\', '/') ?? string.Empty;
        string expected = string.Join('.', new[] { project.Name }.Concat(folder.Split('/', StringSplitOptions.RemoveEmptyEntries)));
        string actual = declared.Groups["name"].Value;
        return actual == expected
            ? null
            : $"{project.Name}/{relativeFile} declares {actual}, its folder is {expected}";
    }

    [GeneratedRegex(@"^namespace\s+(?<name>[\w.]+)\s*;", RegexOptions.Multiline)]
    private static partial Regex FileScopedNamespace();
}
