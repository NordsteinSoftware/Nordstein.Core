using System.Xml;
using System.Xml.Linq;

namespace Nordstein.Core.Testing.Fences;

/// <summary>
/// A solution file (<c>.sln</c> or <c>.slnx</c>) and the projects it lists: the source of truth a
/// fence derives the assemblies it scans from, so a project added to the solution cannot escape
/// the scan.
/// </summary>
/// <remarks>
/// <para>
/// Pair the scan with a test that loads every production project (<see cref="SolutionProject.LoadAssembly"/>):
/// a new project the fence's test project does not reference then fails that test instead of
/// going unscanned, and with one that compares the project files on disk with the solution's
/// list, so a project left out of the solution fails too.
/// </para>
/// <para>Read once, at construction; immutable afterwards and safe to read from any thread.</para>
/// </remarks>
public sealed class SolutionFile
{
    /// <summary>
    /// Finds <paramref name="fileName"/> in <paramref name="startDirectory"/> or the nearest
    /// directory above it and reads the projects it lists. A test passes
    /// <see cref="AppContext.BaseDirectory"/>: its output directory sits below the solution.
    /// </summary>
    /// <param name="fileName">The solution's file name, without a directory, ending in <c>.sln</c> or <c>.slnx</c>.</param>
    /// <param name="startDirectory">The directory to start the search in.</param>
    /// <exception cref="ArgumentNullException"><paramref name="fileName"/> or <paramref name="startDirectory"/> is null.</exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="fileName"/> is blank, holds a directory, or is not a <c>.sln</c> or
    /// <c>.slnx</c> file; or <paramref name="startDirectory"/> is blank.
    /// </exception>
    /// <exception cref="FileNotFoundException">No directory from <paramref name="startDirectory"/> upwards holds the file.</exception>
    /// <exception cref="InvalidDataException">A <c>.slnx</c> file is not well-formed XML or lists a project without a path.</exception>
    public SolutionFile(string fileName, string startDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        ArgumentException.ThrowIfNullOrWhiteSpace(startDirectory);
        if (System.IO.Path.GetFileName(fileName) != fileName)
        {
            throw new ArgumentException("The solution's file name holds no directory; pass the directory as startDirectory.", nameof(fileName));
        }

        string extension = System.IO.Path.GetExtension(fileName);
        bool isXml = string.Equals(extension, ".slnx", StringComparison.OrdinalIgnoreCase);
        if (!isXml && !string.Equals(extension, ".sln", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException($"'{fileName}' is not a .sln or .slnx solution file.", nameof(fileName));
        }

        Directory = Locate(fileName, System.IO.Path.GetFullPath(startDirectory));
        Path = System.IO.Path.Combine(Directory, fileName);
        string content = File.ReadAllText(Path);
        Projects = (isXml ? ReadSlnx(content) : ReadSln(content))
            .Select(project => new SolutionProject(this, project.Name, project.Path.Replace('\\', '/')))
            .OrderBy(project => project.RelativePath, StringComparer.Ordinal)
            .ToArray();
    }

    /// <summary>The solution file's full path.</summary>
    public string Path { get; }

    /// <summary>The full path of the directory holding the solution; project paths are relative to it.</summary>
    public string Directory { get; }

    /// <summary>
    /// The projects the solution lists (C#, F# and Visual Basic project files; solution folders and
    /// loose files are not projects), sorted by <see cref="SolutionProject.RelativePath"/>.
    /// </summary>
    public IReadOnlyList<SolutionProject> Projects { get; }

    private static string Locate(string fileName, string startDirectory)
    {
        for (DirectoryInfo? directory = new(startDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(System.IO.Path.Combine(directory.FullName, fileName)))
            {
                return directory.FullName;
            }
        }

        throw new FileNotFoundException($"{fileName} was not found in {startDirectory} or any directory above it.", fileName);
    }

    /// <summary>
    /// The project lines of a <c>.sln</c>, <c>Project("{type}") = "Name", "Path", "{id}"</c>: split
    /// on its quotes, the name and the path are the fourth and sixth fields.
    /// </summary>
    private static IEnumerable<(string Name, string Path)> ReadSln(string content)
        => content.Split('\n')
            .Where(line => line.StartsWith("Project(\"", StringComparison.Ordinal))
            .Select(line => line.Split('"'))
            .Where(fields => fields.Length > 5 && IsProjectFile(fields[5]))
            .Select(fields => (fields[3], fields[5]));

    private static bool IsProjectFile(string path)
        => path.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase)
            || path.EndsWith(".fsproj", StringComparison.OrdinalIgnoreCase)
            || path.EndsWith(".vbproj", StringComparison.OrdinalIgnoreCase);

    private IEnumerable<(string Name, string Path)> ReadSlnx(string content)
    {
        XDocument document;
        try
        {
            document = XDocument.Parse(content);
        }
        catch (XmlException exception)
        {
            throw new InvalidDataException($"{Path} is not a well-formed .slnx file: {exception.Message}", exception);
        }

        return document.Descendants().Where(element => element.Name.LocalName == "Project")
            .Select(project => project.Attribute("Path")?.Value
                ?? throw new InvalidDataException($"{Path} lists a project without a Path attribute."))
            .Where(IsProjectFile)
            .Select(path => (System.IO.Path.GetFileNameWithoutExtension(path.Replace('\\', '/')), path))
            .ToArray();
    }
}
