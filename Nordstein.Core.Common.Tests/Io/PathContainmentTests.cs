using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Nordstein.Core.Common.Io;
using Nordstein.Core.Common.Lifecycle;
using Nordstein.Core.Testing;

namespace Nordstein.Core.Common.Tests.Io;

[TestClass]
public sealed class PathContainmentTests : BaseTest<Module>
{
    private IPathContainment Containment => GetServices().GetRequiredService<IPathContainment>();

    [TestMethod]
    public void MayBeWithin_PathIsTheRoot_ReturnsTrue()
    {
        using ITempDirectory temp = NewTempDirectory();
        string root = CreateDirectory(temp, "data");

        Containment.MayBeWithin(root, root).Should().BeTrue();
    }

    [TestMethod]
    public void MayBeWithin_ChildThatDoesNotExistYet_ReturnsTrue()
    {
        using ITempDirectory temp = NewTempDirectory();
        string root = CreateDirectory(temp, "data");

        Containment.MayBeWithin(Path.Combine(root, "not", "yet", "there.bin"), root).Should().BeTrue();
    }

    [TestMethod]
    public void MayBeWithin_RootWithTrailingSeparator_ReturnsTrue()
    {
        using ITempDirectory temp = NewTempDirectory();
        string root = CreateDirectory(temp, "data");

        Containment.MayBeWithin(Path.Combine(root, "inner"), root + Path.DirectorySeparatorChar).Should().BeTrue();
    }

    [TestMethod]
    public void MayBeWithin_SiblingSharingThePrefix_ReturnsFalse()
    {
        using ITempDirectory temp = NewTempDirectory();
        string root = CreateDirectory(temp, "data");
        string sibling = CreateDirectory(temp, "data2");

        Containment.MayBeWithin(Path.Combine(sibling, "inner"), root).Should().BeFalse();
    }

    [TestMethod]
    public void MayBeWithin_ParentOfTheRoot_ReturnsFalse()
    {
        using ITempDirectory temp = NewTempDirectory();
        string root = CreateDirectory(temp, "data");

        Containment.MayBeWithin(temp.Path, root).Should().BeFalse();
    }

    [TestMethod]
    public void MayBeWithin_UnrelatedPath_ReturnsFalse()
    {
        using ITempDirectory temp = NewTempDirectory();
        string root = CreateDirectory(temp, "data");
        string other = CreateDirectory(temp, "other");

        Containment.MayBeWithin(other, root).Should().BeFalse();
    }

    [TestMethod]
    public void MayBeWithin_ChildDifferingOnlyInCase_ReturnsTrue()
    {
        // A case-insensitive filesystem (SMB, NTFS) aliases these; the check errs to "inside" everywhere.
        using ITempDirectory temp = NewTempDirectory();
        string root = CreateDirectory(temp, "data");
        string variant = Path.Combine(temp.Path, "DATA", "inner");

        Containment.MayBeWithin(variant, root).Should().BeTrue();
    }

    [TestMethod]
    public void MayBeWithin_DotSegmentsLeavingTheRoot_ReturnsFalse()
    {
        using ITempDirectory temp = NewTempDirectory();
        string root = CreateDirectory(temp, "data");
        string escaping = Path.Combine(root, ".", "inner", "..", "..", "other");

        Containment.MayBeWithin(escaping, root).Should().BeFalse();
    }

    [TestMethod]
    public void MayBeWithin_DotSegmentsStayingInTheRoot_ReturnsTrue()
    {
        using ITempDirectory temp = NewTempDirectory();
        string root = CreateDirectory(temp, "data");
        string staying = Path.Combine(temp.Path, "other", "..", "data", ".", "inner");

        Containment.MayBeWithin(staying, root).Should().BeTrue();
    }

    [TestMethod]
    public void MayBeWithin_PathThroughALinkIntoTheRoot_ReturnsTrue()
    {
        using ITempDirectory temp = NewTempDirectory();
        string root = CreateDirectory(temp, "data");
        string target = CreateDirectory(temp, Path.Combine("data", "secrets"));
        string link = CreateDirectoryLink(temp.Combine("innocent"), target);

        Containment.MayBeWithin(Path.Combine(link, "key.bin"), root).Should().BeTrue();
    }

    [TestMethod]
    public void MayBeWithin_RootIsALink_ComparesItsTarget()
    {
        using ITempDirectory temp = NewTempDirectory();
        string realRoot = CreateDirectory(temp, "real-data");
        string rootLink = CreateDirectoryLink(temp.Combine("data"), realRoot);

        Containment.MayBeWithin(Path.Combine(realRoot, "inner"), rootLink).Should().BeTrue();
        Containment.MayBeWithin(CreateDirectory(temp, "elsewhere"), rootLink).Should().BeFalse();
    }

    [TestMethod]
    public void MayBeWithin_RelativeLinkTarget_ResolvesAgainstTheLinksDirectory()
    {
        using ITempDirectory temp = NewTempDirectory();
        string root = CreateDirectory(temp, "data");
        CreateDirectory(temp, "links");
        string link = CreateDirectoryLink(temp.Combine(Path.Combine("links", "to-data")), Path.Combine("..", "data"));

        Containment.MayBeWithin(link, root).Should().BeTrue();
    }

    [TestMethod]
    public void MayBeWithin_LinkOutOfTheRoot_ReturnsFalse()
    {
        // A link that sits inside the root but points out of it: the real location is outside.
        using ITempDirectory temp = NewTempDirectory();
        string root = CreateDirectory(temp, "data");
        string outside = CreateDirectory(temp, "outside");
        string link = CreateDirectoryLink(Path.Combine(root, "escape"), outside);

        Containment.MayBeWithin(Path.Combine(link, "key.bin"), root).Should().BeFalse();
    }

    [TestMethod]
    public void MayBeWithin_ParentStepAfterALink_StepsFromTheLinksTarget()
    {
        // The operating system resolves "link/.." to the parent of the link's target, so a lexical
        // collapse (outside/keys) would miss that this names data/keys.
        using ITempDirectory temp = NewTempDirectory();
        string root = CreateDirectory(temp, "data");
        string target = CreateDirectory(temp, Path.Combine("data", "inner"));
        CreateDirectory(temp, "outside");
        string link = CreateDirectoryLink(temp.Combine(Path.Combine("outside", "link")), target);

        Containment.MayBeWithin(Path.Combine(link, "..", "keys"), root).Should().BeTrue();
    }

    [TestMethod]
    public void MayBeWithin_ChainOfLinks_FollowsEveryHop()
    {
        using ITempDirectory temp = NewTempDirectory();
        string root = CreateDirectory(temp, "data");
        string first = CreateDirectoryLink(temp.Combine("first"), root);
        string second = CreateDirectoryLink(temp.Combine("second"), first);

        Containment.MayBeWithin(Path.Combine(second, "key.bin"), root).Should().BeTrue();
    }

    [TestMethod]
    public void MayBeWithin_PathContinuingPastARegularFile_KeepsTheTailAsWritten()
    {
        using ITempDirectory temp = NewTempDirectory();
        string root = CreateDirectory(temp, "data");
        string file = Path.Combine(root, "file.bin");
        File.WriteAllBytes(file, [1]);

        Containment.MayBeWithin(Path.Combine(file, "beyond"), root).Should().BeTrue();
    }

    [TestMethod]
    public void MayBeWithin_LinkLoop_ThrowsIOException()
    {
        using ITempDirectory temp = NewTempDirectory();
        string root = CreateDirectory(temp, "data");
        string a = temp.Combine("a");
        string b = temp.Combine("b");
        CreateDirectoryLink(a, b);
        CreateDirectoryLink(b, a);

        Action act = () => Containment.MayBeWithin(Path.Combine(a, "key.bin"), root);

        act.Should().Throw<IOException>();
    }

    [TestMethod]
    public void MayBeWithin_NullPath_ThrowsArgumentNull()
    {
        string missing = (new string[1])[0]; // a null behind the non-nullable signature, without `!`

        Action act = () => Containment.MayBeWithin(missing, "/root");

        act.Should().Throw<ArgumentNullException>().WithParameterName("path");
    }

    [TestMethod]
    public void MayBeWithin_NullRoot_ThrowsArgumentNull()
    {
        string missing = (new string[1])[0];

        Action act = () => Containment.MayBeWithin("/path", missing);

        act.Should().Throw<ArgumentNullException>().WithParameterName("root");
    }

    [TestMethod]
    [DataRow("", "/root", "path")]
    [DataRow("/path", "", "root")]
    public void MayBeWithin_EmptyArgument_ThrowsArgument(string path, string root, string parameter)
    {
        Action act = () => Containment.MayBeWithin(path, root);

        act.Should().Throw<ArgumentException>().WithParameterName(parameter);
    }

    [TestMethod]
    public void MayBeWithin_ParentStepAfterALinkOutOfTheRoot_CountsTheReadingDotNetOpens()
    {
        // root/escape -> outside/deep. The OS walks root/escape/../keys to outside/keys, but File.ReadAllBytes
        // collapses ".." as text first and opens root/keys: that reading must count.
        using ITempDirectory temp = NewTempDirectory();
        string root = CreateDirectory(temp, "data");
        string deep = CreateDirectory(temp, Path.Combine("outside", "deep"));
        string link = CreateDirectoryLink(Path.Combine(root, "escape"), deep);

        Containment.MayBeWithin(Path.Combine(link, "..", "keys"), root).Should().BeTrue();
    }

    [TestMethod]
    public void MayBeWithin_RootWithAParentStepAfterALink_CountsBothReadingsOfTheRoot()
    {
        // The root itself named through link/..: the OS reads it as inner's parent (data), .NET as outside.
        using ITempDirectory temp = NewTempDirectory();
        string inner = CreateDirectory(temp, Path.Combine("data", "inner"));
        string outside = CreateDirectory(temp, "outside");
        string link = CreateDirectoryLink(Path.Combine(outside, "link"), inner);
        string rootViaLink = Path.Combine(link, "..");

        Containment.MayBeWithin(Path.Combine(temp.Path, "data", "keys"), rootViaLink).Should().BeTrue();
        Containment.MayBeWithin(Path.Combine(outside, "keys"), rootViaLink).Should().BeTrue();
    }

    [TestMethod]
    public void MayBeWithin_ParentStepBackOutOfAMissingTail_FollowsLinksAgain()
    {
        // out/esc -> elsewhere/deep; elsewhere/toroot -> data. The OS walks out/esc/nope/../../toroot to
        // elsewhere/toroot, a link into the root: stepping back above the missing "nope" must resume link resolution.
        using ITempDirectory temp = NewTempDirectory();
        string root = CreateDirectory(temp, "data");
        string deep = CreateDirectory(temp, Path.Combine("elsewhere", "deep"));
        CreateDirectoryLink(temp.Combine(Path.Combine("elsewhere", "toroot")), root);
        CreateDirectory(temp, "out");
        string esc = CreateDirectoryLink(temp.Combine(Path.Combine("out", "esc")), deep);

        Containment.MayBeWithin(Path.Combine(esc, "nope", "..", "..", "toroot", "key.bin"), root).Should().BeTrue();
    }

    [TestMethod]
    public void MayBeWithin_DanglingLinkIntoTheRoot_ReturnsTrue()
    {
        // A create through the link would land in the root, so its target counts before it exists.
        using ITempDirectory temp = NewTempDirectory();
        string root = CreateDirectory(temp, "data");
        string link = CreateDirectoryLink(temp.Combine("keys"), Path.Combine(root, "not-yet"));

        Containment.MayBeWithin(Path.Combine(link, "key.bin"), root).Should().BeTrue();
    }

    [TestMethod]
    public void MayBeWithin_RelativePaths_ResolveAgainstTheCurrentDirectory()
    {
        using ITempDirectory temp = NewTempDirectory();
        string root = CreateDirectory(temp, "data");
        string current = Directory.GetCurrentDirectory();

        Containment.MayBeWithin(Path.GetRelativePath(current, Path.Combine(root, "inner")), root).Should().BeTrue();
        Containment.MayBeWithin(Path.Combine(root, "inner"), Path.GetRelativePath(current, root)).Should().BeTrue();
        Containment.MayBeWithin(Path.GetRelativePath(current, temp.Path), root).Should().BeFalse();
    }

    [TestMethod]
    public void MayBeWithin_ComposedAndDecomposedNames_AreTheSameSegment()
    {
        // "Geschäft" with U+00E4 versus "a" + U+0308: APFS and some SMB/ZFS servers alias them.
        using ITempDirectory temp = NewTempDirectory();
        string root = CreateDirectory(temp, "Geschäft");
        string decomposed = Path.Combine(temp.Path, "Gesch" + "ä" + "ft", "keys");

        Containment.MayBeWithin(decomposed, root).Should().BeTrue();
    }

    [TestMethod]
    public void MayBeWithin_FortyLinksInAChain_Resolves()
    {
        using ITempDirectory temp = NewTempDirectory();
        string root = CreateDirectory(temp, "data");
        string head = LinkChain(temp, root, 40);

        Containment.MayBeWithin(Path.Combine(head, "key.bin"), root).Should().BeTrue();
    }

    [TestMethod]
    public void MayBeWithin_FortyOneLinksInAChain_ThrowsIOException()
    {
        using ITempDirectory temp = NewTempDirectory();
        string root = CreateDirectory(temp, "data");
        string head = LinkChain(temp, root, 41);

        Action act = () => Containment.MayBeWithin(Path.Combine(head, "key.bin"), root);

        act.Should().Throw<IOException>().WithMessage("*More than 40 links*");
    }

    [TestMethod]
    public void MayBeWithin_UnsearchableDirectoryOnTheWay_ThrowsUnauthorizedAccess()
    {
        // readlink answers "not a link" for EACCES too; the stat after it must not let a hidden link pass.
        if (OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("Unix file modes are not applicable on Windows.");
            return;
        }

        if (Environment.IsPrivilegedProcess)
        {
            Assert.Inconclusive("Root bypasses file modes; this needs a process that file permissions bind.");
        }

        using ITempDirectory temp = NewTempDirectory();
        string root = CreateDirectory(temp, "data");
        string locked = CreateDirectory(temp, "locked");
        string link = CreateDirectoryLink(Path.Combine(locked, "keys"), root);
        File.SetUnixFileMode(locked, UnixFileMode.None);
        try
        {
            Action act = () => Containment.MayBeWithin(Path.Combine(link, "key.bin"), root);

            act.Should().Throw<UnauthorizedAccessException>();
        }
        finally
        {
            File.SetUnixFileMode(locked, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
    }

    [TestMethod]
    public void MayBeWithin_DriveRelativeLinkTarget_KeepsTheLinksDrive()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("A drive-relative link target (\\x) exists only on Windows.");
        }

        using ITempDirectory temp = NewTempDirectory();
        string root = CreateDirectory(temp, "data");
        string driveRelative = root[(Path.GetPathRoot(root) ?? string.Empty).Length..];
        string link = CreateDirectoryLink(temp.Combine("keys"), Path.DirectorySeparatorChar + driveRelative);

        Containment.MayBeWithin(Path.Combine(link, "key.bin"), root).Should().BeTrue();
    }

    [TestMethod]
    public void MayBeWithin_NulInAPath_ThrowsArgument()
    {
        Action act = () => Containment.MayBeWithin("/data/ke\0ys", "/data");

        act.Should().Throw<ArgumentException>();
    }

    private static string LinkChain(ITempDirectory temp, string target, int length)
    {
        string next = target;
        for (int index = 0; index < length; index++)
        {
            next = CreateDirectoryLink(temp.Combine($"hop{index}"), next);
        }

        return next;
    }

    private ITempDirectory NewTempDirectory()
        => GetServices().GetRequiredService<ITempDirectory.Create>()(null, "path-containment-test");

    private static string CreateDirectory(ITempDirectory temp, string relative)
        => Directory.CreateDirectory(temp.Combine(relative)).FullName;

    private static string CreateDirectoryLink(string link, string target)
    {
        try
        {
            Directory.CreateSymbolicLink(link, target);
        }
        catch (Exception exception) when (OperatingSystem.IsWindows() && exception is IOException or UnauthorizedAccessException)
        {
            Assert.Inconclusive("Creating a symbolic link needs Developer Mode or an elevated process on Windows.");
        }

        return link;
    }
}
