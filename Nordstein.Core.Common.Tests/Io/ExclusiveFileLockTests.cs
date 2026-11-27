using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Nordstein.Core.Common.Io;
using Nordstein.Core.Common.Lifecycle;
using Nordstein.Core.Testing;

namespace Nordstein.Core.Common.Tests.Io;

[TestClass]
public sealed class ExclusiveFileLockTests : BaseTest<Module>
{
    private IExclusiveFileLock FileLock => GetServices().GetRequiredService<IExclusiveFileLock>();

    private ITempDirectory NewTempDirectory()
        => GetServices().GetRequiredService<ITempDirectory.Create>()(null, "exclusive-file-lock-test");

    [TestMethod]
    public async Task TryAcquire_OnMissingFile_CreatesItAndReturnsALease()
    {
        using ITempDirectory temp = NewTempDirectory();
        string path = temp.Combine("writer.lock");

        await using IAsyncDisposable? lease = FileLock.TryAcquire(path);

        lease.Should().NotBeNull();
        File.Exists(path).Should().BeTrue();
    }

    [TestMethod]
    public async Task TryAcquire_WhileHeld_ReturnsNull()
    {
        using ITempDirectory temp = NewTempDirectory();
        string path = temp.Combine("writer.lock");
        await using IAsyncDisposable? first = FileLock.TryAcquire(path);

        IAsyncDisposable? second = FileLock.TryAcquire(path);

        first.Should().NotBeNull();
        second.Should().BeNull();
    }

    [TestMethod]
    public async Task TryAcquire_AfterTheLeaseIsDisposed_ReturnsANewLease()
    {
        using ITempDirectory temp = NewTempDirectory();
        string path = temp.Combine("writer.lock");
        IAsyncDisposable? first = FileLock.TryAcquire(path);
        Assert.IsNotNull(first);
        await first.DisposeAsync();

        await using IAsyncDisposable? second = FileLock.TryAcquire(path);

        second.Should().NotBeNull();
    }

    [TestMethod]
    public async Task DisposeAsync_KeepsTheLockFileAndItsContent()
    {
        using ITempDirectory temp = NewTempDirectory();
        string path = temp.Combine("writer.lock");
        await File.WriteAllTextAsync(path, "existing", CancellationToken);
        IAsyncDisposable? lease = FileLock.TryAcquire(path);
        Assert.IsNotNull(lease);

        await lease.DisposeAsync();

        (await File.ReadAllTextAsync(path, CancellationToken)).Should().Be("existing");
    }

    [TestMethod]
    public async Task DisposeAsync_Twice_ReleasesOnce()
    {
        using ITempDirectory temp = NewTempDirectory();
        string path = temp.Combine("writer.lock");
        IAsyncDisposable? first = FileLock.TryAcquire(path);
        Assert.IsNotNull(first);
        await first.DisposeAsync();
        await using IAsyncDisposable? second = FileLock.TryAcquire(path);

        Func<Task> act = async () => await first.DisposeAsync();

        await act.Should().NotThrowAsync();
        FileLock.TryAcquire(path).Should().BeNull();
    }

    [TestMethod]
    public void TryAcquire_InAMissingDirectory_ThrowsDirectoryNotFoundException()
    {
        using ITempDirectory temp = NewTempDirectory();

        Action act = () => FileLock.TryAcquire(temp.Combine(Path.Combine("missing", "writer.lock")));

        act.Should().Throw<DirectoryNotFoundException>();
    }

    [TestMethod]
    [OSCondition(OperatingSystems.Linux | OperatingSystems.OSX | OperatingSystems.FreeBSD)]
    public void TryAcquire_OnADirectory_ThrowsInsteadOfReportingItHeld()
    {
        using ITempDirectory temp = NewTempDirectory();

        Action act = () => FileLock.TryAcquire(temp.Path);

        act.Should().Throw<UnauthorizedAccessException>();
    }

    [TestMethod]
    public void TryAcquire_WithEmptyPath_ThrowsArgumentException()
    {
        Action act = () => FileLock.TryAcquire("");

        act.Should().Throw<ArgumentException>();
    }
}
