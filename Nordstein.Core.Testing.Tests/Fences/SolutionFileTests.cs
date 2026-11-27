using System.Reflection;
using AwesomeAssertions;
using Nordstein.Core.Common.Lifecycle;
using Nordstein.Core.Testing.Fences;

namespace Nordstein.Core.Testing.Tests.Fences;

/// <summary>
/// <see cref="SolutionFile"/> and <see cref="SolutionProject"/>: the projects a <c>.sln</c> or
/// <c>.slnx</c> lists, found by walking up from a directory, with their assembly names, so a fence
/// scans every production project and a new one cannot escape it.
/// </summary>
[TestClass]
public sealed class SolutionFileTests : BaseTest<Module>
{
    private const string Sln = """
        Microsoft Visual Studio Solution File, Format Version 12.00
        Project("{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}") = "Example.Service", "src\Example.Service\Example.Service.csproj", "{11111111-1111-1111-1111-111111111111}"
        EndProject
        Project("{2150E333-8FDC-42A3-9474-1A3956D46DE8}") = "docs", "docs", "{22222222-2222-2222-2222-222222222222}"
        EndProject
        Project("{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}") = "Example.Service.Tests", "tests\Example.Service.Tests\Example.Service.Tests.csproj", "{33333333-3333-3333-3333-333333333333}"
        EndProject
        Project("{F2A71F9B-5D33-465A-A702-920D77279786}") = "Example.Scripts", "src\Example.Scripts\Example.Scripts.fsproj", "{44444444-4444-4444-4444-444444444444}"
        EndProject
        Global
        EndGlobal
        """;

    private const string Slnx = """
        <Solution>
          <Folder Name="/src/">
            <Project Path="src/Example.Service/Example.Service.csproj" />
            <Project Path="src\Example.Worker\Example.Worker.csproj" />
          </Folder>
          <Project Path="tests/Example.Service.Tests/Example.Service.Tests.csproj" />
          <Project Path="deploy/docker-compose.dcproj" />
          <File Path="README.md" />
        </Solution>
        """;

    [TestMethod]
    public void Sln_ListsItsProjectsSortedByPathWithoutSolutionFolders()
    {
        using ITempDirectory root = GetServices().GetTempDirectory();
        WriteSolution(root.Path, "Example.sln", Sln);

        var solution = new SolutionFile("Example.sln", root.Path);

        solution.Path.Should().Be(Path.Combine(root.Path, "Example.sln"));
        solution.Directory.Should().Be(root.Path);
        solution.Projects.Select(project => (project.Name, project.RelativePath)).Should().Equal(
            ("Example.Scripts", "src/Example.Scripts/Example.Scripts.fsproj"),
            ("Example.Service", "src/Example.Service/Example.Service.csproj"),
            ("Example.Service.Tests", "tests/Example.Service.Tests/Example.Service.Tests.csproj"));
    }

    [TestMethod]
    public void Slnx_ListsItsCodeProjectsIncludingThoseInFolders()
    {
        using ITempDirectory root = GetServices().GetTempDirectory();
        WriteSolution(root.Path, "Example.slnx", Slnx);

        var solution = new SolutionFile("Example.slnx", root.Path);

        solution.Projects.Select(project => (project.Name, project.RelativePath)).Should().Equal(
            ("Example.Service", "src/Example.Service/Example.Service.csproj"),
            ("Example.Worker", "src/Example.Worker/Example.Worker.csproj"),
            ("Example.Service.Tests", "tests/Example.Service.Tests/Example.Service.Tests.csproj"));
    }

    [TestMethod]
    public void Constructor_WalksUpFromTheStartDirectory()
    {
        using ITempDirectory root = GetServices().GetTempDirectory();
        WriteSolution(root.Path, "Example.sln", Sln);
        string deep = Directory.CreateDirectory(Path.Combine(root.Path, "bin", "Debug", "net10.0")).FullName;

        var solution = new SolutionFile("Example.sln", deep);

        solution.Directory.Should().Be(root.Path);
    }

    [TestMethod]
    public void Constructor_WhenNoAncestorHoldsTheFile_ThrowsFileNotFound()
    {
        using ITempDirectory root = GetServices().GetTempDirectory();

        Action act = () => _ = new SolutionFile($"Missing-{Guid.NewGuid():N}.sln", root.Path);

        act.Should().Throw<FileNotFoundException>().WithMessage($"*was not found in {root.Path} or any directory above it*");
    }

    [TestMethod]
    [DataRow("Example.csproj")]
    [DataRow("Example")]
    public void Constructor_WithAFileThatIsNotASolution_Throws(string fileName)
    {
        using ITempDirectory root = GetServices().GetTempDirectory();

        Action act = () => _ = new SolutionFile(fileName, root.Path);

        act.Should().Throw<ArgumentException>().WithParameterName("fileName").WithMessage("*.sln or .slnx*");
    }

    [TestMethod]
    [DataRow("sub/Example.sln")]
    [DataRow("")]
    [DataRow(" ")]
    public void Constructor_WithAPathOrBlankForTheFileName_Throws(string fileName)
    {
        using ITempDirectory root = GetServices().GetTempDirectory();

        Action act = () => _ = new SolutionFile(fileName, root.Path);

        act.Should().Throw<ArgumentException>().WithParameterName("fileName");
    }

    [TestMethod]
    public void Constructor_WithABlankStartDirectory_Throws()
    {
        Action act = () => _ = new SolutionFile("Example.sln", " ");

        act.Should().Throw<ArgumentException>().WithParameterName("startDirectory");
    }

    [TestMethod]
    public void Constructor_WithNullArguments_Throws()
    {
#pragma warning disable CS8625 // WHY: the guards under test are reachable only with the null the annotations forbid.
        Action fileName = () => _ = new SolutionFile(null, ".");
        Action startDirectory = () => _ = new SolutionFile("Example.sln", null);
#pragma warning restore CS8625

        fileName.Should().Throw<ArgumentNullException>().WithParameterName("fileName");
        startDirectory.Should().Throw<ArgumentNullException>().WithParameterName("startDirectory");
    }

    [TestMethod]
    public void Slnx_ThatIsNotXml_ThrowsInvalidData()
    {
        using ITempDirectory root = GetServices().GetTempDirectory();
        WriteSolution(root.Path, "Broken.slnx", "<Solution><Project Path=");

        Action act = () => _ = new SolutionFile("Broken.slnx", root.Path);

        act.Should().Throw<InvalidDataException>().WithMessage("*Broken.slnx*");
    }

    [TestMethod]
    public void Slnx_WithAProjectWithoutAPath_ThrowsInvalidData()
    {
        using ITempDirectory root = GetServices().GetTempDirectory();
        WriteSolution(root.Path, "Pathless.slnx", "<Solution><Project /></Solution>");

        Action act = () => _ = new SolutionFile("Pathless.slnx", root.Path);

        act.Should().Throw<InvalidDataException>().WithMessage("*without a Path*");
    }

    [TestMethod]
    public void Project_PathsAndTestClassification()
    {
        using ITempDirectory root = GetServices().GetTempDirectory();
        WriteSolution(root.Path, "Example.sln", Sln);

        IReadOnlyList<SolutionProject> projects = new SolutionFile("Example.sln", root.Path).Projects;

        SolutionProject service = projects.Single(project => project.Name == "Example.Service");
        service.FullPath.Should().Be(Path.Combine(root.Path, "src", "Example.Service", "Example.Service.csproj"));
        service.Directory.Should().Be(Path.Combine(root.Path, "src", "Example.Service"));
        service.IsTestProject.Should().BeFalse();
        service.ToString().Should().Be("Example.Service (src/Example.Service/Example.Service.csproj)");
        projects.Single(project => project.Name == "Example.Service.Tests").IsTestProject.Should().BeTrue();
    }

    [TestMethod]
    public void AssemblyName_IsTheProjectsOwnOrItsName()
    {
        using ITempDirectory root = GetServices().GetTempDirectory();
        WriteSolution(root.Path, "Example.sln", Sln);
        WriteProject(root.Path, "src/Example.Service/Example.Service.csproj", "<Project><PropertyGroup><AssemblyName> example-service </AssemblyName></PropertyGroup></Project>");
        WriteProject(root.Path, "tests/Example.Service.Tests/Example.Service.Tests.csproj", "<Project />");

        IReadOnlyList<SolutionProject> projects = new SolutionFile("Example.sln", root.Path).Projects;

        projects.Single(project => project.Name == "Example.Service").AssemblyName.Should().Be("example-service");
        projects.Single(project => project.Name == "Example.Service.Tests").AssemblyName.Should().Be("Example.Service.Tests");
    }

    [TestMethod]
    public void AssemblyName_ThatNeedsMsBuildToEvaluate_Throws()
    {
        using ITempDirectory root = GetServices().GetTempDirectory();
        WriteSolution(root.Path, "Example.sln", Sln);
        WriteProject(root.Path, "src/Example.Service/Example.Service.csproj", "<Project><PropertyGroup><AssemblyName>$(Prefix).Service</AssemblyName></PropertyGroup></Project>");

        SolutionProject service = new SolutionFile("Example.sln", root.Path).Projects.Single(project => project.Name == "Example.Service");
        Action act = () => _ = service.AssemblyName;

        act.Should().Throw<InvalidOperationException>().WithMessage("*$(Prefix).Service*MSBuild*");
    }

    [TestMethod]
    public void AssemblyName_DeclaredTwiceDifferently_Throws()
    {
        using ITempDirectory root = GetServices().GetTempDirectory();
        WriteSolution(root.Path, "Example.sln", Sln);
        WriteProject(
            root.Path,
            "src/Example.Service/Example.Service.csproj",
            "<Project><PropertyGroup><AssemblyName>one</AssemblyName></PropertyGroup><PropertyGroup Condition=\"x\"><AssemblyName>two</AssemblyName></PropertyGroup></Project>");

        SolutionProject service = new SolutionFile("Example.sln", root.Path).Projects.Single(project => project.Name == "Example.Service");
        Action act = () => _ = service.AssemblyName;

        act.Should().Throw<InvalidOperationException>().WithMessage("*more than one AssemblyName*");
    }

    [TestMethod]
    public void AssemblyName_DeclaredTwiceAlike_IsThatName()
    {
        using ITempDirectory root = GetServices().GetTempDirectory();
        WriteSolution(root.Path, "Example.sln", Sln);
        WriteProject(
            root.Path,
            "src/Example.Service/Example.Service.csproj",
            "<Project><PropertyGroup><AssemblyName>same</AssemblyName></PropertyGroup><PropertyGroup><AssemblyName>same</AssemblyName></PropertyGroup></Project>");

        new SolutionFile("Example.sln", root.Path).Projects.Single(project => project.Name == "Example.Service")
            .AssemblyName.Should().Be("same");
    }

    [TestMethod]
    public void AssemblyName_OfAMissingProjectFile_ThrowsFileNotFound()
    {
        using ITempDirectory root = GetServices().GetTempDirectory();
        WriteSolution(root.Path, "Example.sln", Sln);

        SolutionProject service = new SolutionFile("Example.sln", root.Path).Projects.Single(project => project.Name == "Example.Service");
        Action act = () => _ = service.AssemblyName;

        act.Should().Throw<FileNotFoundException>();
    }

    [TestMethod]
    public void LoadAssembly_OfAReferencedProject_LoadsIt()
    {
        var solution = new SolutionFile("Nordstein.Core.sln", AppContext.BaseDirectory);
        SolutionProject testing = solution.Projects.Single(project => project.Name == "Nordstein.Core.Testing");

        testing.LoadAssembly().Should().BeSameAs(typeof(BaseTest<>).Assembly);
        testing.TryLoadAssembly(out Assembly? loaded).Should().BeTrue();
        loaded.Should().BeSameAs(typeof(BaseTest<>).Assembly);
    }

    [TestMethod]
    public void LoadAssembly_OfAProjectThisTestHostDoesNotReference_FailsTheTestWithTheRemedy()
    {
        using ITempDirectory root = GetServices().GetTempDirectory();
        WriteSolution(root.Path, "Example.sln", Sln);
        WriteProject(root.Path, "src/Example.Service/Example.Service.csproj", "<Project />");
        SolutionProject service = new SolutionFile("Example.sln", root.Path).Projects.Single(project => project.Name == "Example.Service");

        Action act = () => service.LoadAssembly();

        act.Should().Throw<AssertFailedException>()
            .WithMessage("*Example.Service*listed in Example.sln*not loadable*Add a ProjectReference*")
            .WithInnerException<FileNotFoundException>();
        service.TryLoadAssembly(out Assembly? missing).Should().BeFalse();
        missing.Should().BeNull();
    }

    [TestMethod]
    public void CoreSolution_ListsTheHarnessAndThisProject()
    {
        var solution = new SolutionFile("Nordstein.Core.sln", AppContext.BaseDirectory);

        solution.Projects.Select(project => project.Name).Should().Contain(["Nordstein.Core.Testing", "Nordstein.Core.Testing.Tests"]);
        solution.Projects.Should().BeInAscendingOrder(project => project.RelativePath, StringComparer.Ordinal);
    }

    private static void WriteSolution(string root, string fileName, string content)
        => File.WriteAllText(Path.Combine(root, fileName), content);

    private static void WriteProject(string root, string relativePath, string content)
    {
        string path = Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path) ?? root);
        File.WriteAllText(path, content);
    }
}
