using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Nordstein.Core.Common.Io;
using Nordstein.Core.Common.Io.Internal;
using Nordstein.Core.Common.Lifecycle;
using Nordstein.Core.Testing;

namespace Nordstein.Core.Common.Tests.Io;

[TestClass]
public sealed class FilesystemProbeTests : BaseTest<Module>
{
    private IFilesystemProbe Probe => GetServices().GetRequiredService<IFilesystemProbe>();

    private IDirectorySync DirectorySync => GetServices().GetRequiredService<IDirectorySync>();

    private IExclusiveFileLock FileLock => GetServices().GetRequiredService<IExclusiveFileLock>();

    private ITempDirectory NewTempDirectory()
        => GetServices().GetRequiredService<ITempDirectory.Create>()(null, "filesystem-probe-test");

    [TestMethod]
    public void Probe_OnALocalDirectory_ReportsEveryGuaranteeHeld()
    {
        using ITempDirectory temp = NewTempDirectory();

        FilesystemCapabilities capabilities = Probe.Probe(temp.Path);

        capabilities.RefusesReplacingRename.Should().BeTrue();
        capabilities.RefusesSecondExclusiveOpen.Should().BeTrue();
    }

    [TestMethod]
    [OSCondition(OperatingSystems.Linux)]
    public void Probe_OnLinux_NamesTheFilesystemAndFlushesTheDirectory()
    {
        using ITempDirectory temp = NewTempDirectory();

        FilesystemCapabilities capabilities = Probe.Probe(temp.Path);

        capabilities.FilesystemType.Should().NotBeNullOrWhiteSpace().And.NotBe("unknown");
        capabilities.DirectorySync.Should().Be(DirectorySyncResult.Flushed);
    }

    [TestMethod]
    [OSCondition(OperatingSystems.Linux)]
    public void FilesystemTypeOf_ThroughASymbolicLink_NamesTheTargetsFilesystem()
    {
        using ITempDirectory temp = NewTempDirectory();
        string link = temp.Combine("link-to-proc");
        Directory.CreateSymbolicLink(link, "/proc");
        var probe = new FilesystemProbe(DirectorySync, FileLock);

        string type = probe.FilesystemTypeOf(Path.Combine(link, "self"));

        type.Should().Be("proc");
    }

    [TestMethod]
    [OSCondition(OperatingSystems.Windows | OperatingSystems.OSX | OperatingSystems.FreeBSD)]
    public void Probe_OffLinux_ReportsAnUnknownTypeAndNoDirectoryFlush()
    {
        using ITempDirectory temp = NewTempDirectory();

        FilesystemCapabilities capabilities = Probe.Probe(temp.Path);

        capabilities.FilesystemType.Should().Be("unknown");
        capabilities.DirectorySync.Should().Be(DirectorySyncResult.Unsupported);
    }

    [TestMethod]
    public void Probe_WhenTheRenameReplacesTheTarget_ReportsItNotRefused()
    {
        using ITempDirectory temp = NewTempDirectory();
        var probe = new FilesystemProbe(DirectorySync, FileLock, (source, target) => File.Move(source, target, overwrite: true));

        FilesystemCapabilities capabilities = probe.Probe(temp.Path);

        capabilities.RefusesReplacingRename.Should().BeFalse();
        Directory.GetFileSystemEntries(temp.Path).Should().BeEmpty();
    }

    [TestMethod]
    public void Probe_WhenASecondLeaseIsGranted_ReportsItNotRefusedAndReleasesBoth()
    {
        using ITempDirectory temp = NewTempDirectory();
        var fileLock = new SharedFileLock();
        var probe = new FilesystemProbe(DirectorySync, fileLock);

        FilesystemCapabilities capabilities = probe.Probe(temp.Path);

        capabilities.RefusesSecondExclusiveOpen.Should().BeFalse();
        fileLock.Granted.Should().Be(2);
        fileLock.Released.Should().Be(2);
    }

    [TestMethod]
    public void Probe_WhenEvenTheFirstLeaseIsRefused_ReportsItNotRefused()
    {
        using ITempDirectory temp = NewTempDirectory();
        var probe = new FilesystemProbe(DirectorySync, new RefusingFileLock());

        FilesystemCapabilities capabilities = probe.Probe(temp.Path);

        capabilities.RefusesSecondExclusiveOpen.Should().BeFalse();
    }

    [TestMethod]
    public void Probe_LeavesNoScratchFilesBehind()
    {
        using ITempDirectory temp = NewTempDirectory();
        File.WriteAllText(temp.Combine("keep.txt"), "keep");

        _ = Probe.Probe(temp.Path);

        Directory.GetFileSystemEntries(temp.Path).Should().Equal(temp.Combine("keep.txt"));
    }

    [TestMethod]
    public void Probe_OnAMissingDirectory_ThrowsDirectoryNotFoundException()
    {
        using ITempDirectory temp = NewTempDirectory();

        Action act = () => Probe.Probe(temp.Combine("missing"));

        act.Should().Throw<DirectoryNotFoundException>();
    }

    [TestMethod]
    public void Probe_WithEmptyPath_ThrowsArgumentException()
    {
        Action act = () => Probe.Probe("");

        act.Should().Throw<ArgumentException>();
    }

    private sealed class SharedFileLock : IExclusiveFileLock
    {
        public int Granted { get; private set; }

        public int Released { get; private set; }

        public IAsyncDisposable? TryAcquire(string path)
        {
            Granted++;
            return new Lease(this);
        }

        private sealed class Lease : IAsyncDisposable
        {
            private readonly SharedFileLock owner;

            public Lease(SharedFileLock owner)
            {
                this.owner = owner;
            }

            public ValueTask DisposeAsync()
            {
                owner.Released++;
                return ValueTask.CompletedTask;
            }
        }
    }

    private sealed class RefusingFileLock : IExclusiveFileLock
    {
        public IAsyncDisposable? TryAcquire(string path) => null;
    }
}
