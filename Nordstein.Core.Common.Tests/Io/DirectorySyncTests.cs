using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Nordstein.Core.Common.Io;
using Nordstein.Core.Common.Io.Internal;
using Nordstein.Core.Common.Lifecycle;
using Nordstein.Core.Testing;

namespace Nordstein.Core.Common.Tests.Io;

[TestClass]
public sealed class DirectorySyncTests : BaseTest<Module>
{
    private const int Einval = 22;
    private const int Eio = 5;

    private IDirectorySync DirectorySync => GetServices().GetRequiredService<IDirectorySync>();

    private ITempDirectory NewTempDirectory()
        => GetServices().GetRequiredService<ITempDirectory.Create>()(null, "directory-sync-test");

    [TestMethod]
    [OSCondition(OperatingSystems.Linux)]
    public void Flush_OnTemporaryDirectory_ReportsFlushed()
    {
        using ITempDirectory temp = NewTempDirectory();
        File.WriteAllText(temp.Combine("entry.txt"), "entry");

        DirectorySyncResult result = DirectorySync.Flush(temp.Path);

        result.Should().Be(DirectorySyncResult.Flushed);
    }

    [TestMethod]
    [OSCondition(OperatingSystems.Linux)]
    public void Flush_OnMissingDirectory_ThrowsIOExceptionNamingThePath()
    {
        using ITempDirectory temp = NewTempDirectory();
        string missing = temp.Combine("no-such-directory");

        Action act = () => DirectorySync.Flush(missing);

        act.Should().Throw<IOException>().Which.Message.Should().Contain(missing).And.Contain("errno 2");
    }

    [TestMethod]
    [OSCondition(OperatingSystems.Linux)]
    public void Flush_OnARegularFile_ThrowsIOException()
    {
        using ITempDirectory temp = NewTempDirectory();
        string file = temp.Combine("not-a-directory.txt");
        File.WriteAllText(file, "file");

        Action act = () => DirectorySync.Flush(file);

        act.Should().Throw<IOException>().Which.Message.Should().Contain(file);
    }

    [TestMethod]
    [OSCondition(OperatingSystems.Windows | OperatingSystems.OSX | OperatingSystems.FreeBSD)]
    public void Flush_OffLinux_DoesNothing()
    {
        DirectorySync.Flush("/this/directory/does/not/exist").Should().Be(DirectorySyncResult.Unsupported);
    }

    [TestMethod]
    public void FsyncFailure_WithEinval_IsReportedAsUnsupported()
    {
        DirectorySyncResult result = LinuxDirectorySync.FsyncFailure(Einval, "/data/blobs");

        result.Should().Be(DirectorySyncResult.Unsupported);
    }

    [TestMethod]
    public void FsyncFailure_WithAnyOtherErrno_ThrowsIOExceptionWithPathAndErrno()
    {
        Action act = () => LinuxDirectorySync.FsyncFailure(Eio, "/data/blobs");

        act.Should().Throw<IOException>().Which.Message.Should().Contain("/data/blobs").And.Contain("errno 5");
    }

    [TestMethod]
    public void Flush_WithEmptyPath_ThrowsArgumentException()
    {
        Action act = () => DirectorySync.Flush("");

        act.Should().Throw<ArgumentException>();
    }
}
