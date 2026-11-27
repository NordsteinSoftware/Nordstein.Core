using System.Runtime.Versioning;
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Nordstein.Core.Common.Io;
using Nordstein.Core.Common.Lifecycle;
using Nordstein.Core.Testing;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace Nordstein.Core.Common.Tests.Io;

/// <summary>
/// Durable directory creation: every missing directory is created and the parent of each is
/// flushed, outermost first; an existing directory is left alone.
/// </summary>
[TestClass]
public sealed class DirectorySyncExtensionsTests : BaseTest<Module>
{
    [TestMethod]
    public void CreateDurably_MissingChain_CreatesItAndFlushesEachParentOutermostFirst()
    {
        using ITempDirectory temp = NewTempDirectory();
        IDirectorySync sync = Sync();
        string target = Path.Combine(temp.Path, "a", "b", "c");

        IReadOnlyList<string> created = sync.CreateDurably(target);

        string a = Path.Combine(temp.Path, "a");
        string b = Path.Combine(a, "b");
        created.Should().Equal(a, b, target);
        Directory.Exists(target).Should().BeTrue();
        sync.ReceivedCalls().Select(call => call.GetArguments()[0]).Should().Equal(temp.Path, a, b);
    }

    [TestMethod]
    public void CreateDurably_ExistingDirectory_CreatesAndFlushesNothing()
    {
        using ITempDirectory temp = NewTempDirectory();
        IDirectorySync sync = Sync();

        sync.CreateDurably(temp.Path).Should().BeEmpty();
        sync.ReceivedCalls().Should().BeEmpty();
    }

    [TestMethod]
    public void CreateDurably_TrailingSeparator_NamesEachDirectoryOnce()
    {
        using ITempDirectory temp = NewTempDirectory();
        IDirectorySync sync = Sync();
        string target = Path.Combine(temp.Path, "x");

        IReadOnlyList<string> created = sync.CreateDurably(target + Path.DirectorySeparatorChar);

        created.Should().Equal(target);
        sync.ReceivedCalls().Select(call => call.GetArguments()[0]).Should().Equal(temp.Path);
    }

    [TestMethod]
    public void CreateDurably_DotSegments_AreResolvedFirst()
    {
        using ITempDirectory temp = NewTempDirectory();
        string target = Path.Combine(temp.Path, "a", "..", "b");

        Sync().CreateDurably(target).Should().Equal(Path.Combine(temp.Path, "b"));
    }

    [TestMethod]
    [OSCondition(OperatingSystems.Linux | OperatingSystems.OSX | OperatingSystems.FreeBSD)]
    [UnsupportedOSPlatform("windows")]
    public void CreateDurably_UnixMode_AppliesToEveryCreatedDirectory()
    {
        using ITempDirectory temp = NewTempDirectory();
        string target = Path.Combine(temp.Path, "p", "q");

        IReadOnlyList<string> created = Sync().CreateDurably(target, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);

        created.Should().AllSatisfy(directory => File.GetUnixFileMode(directory).Should().Be(UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute));
    }

    [TestMethod]
    public void CreateDurably_RealSync_Works()
    {
        using ITempDirectory temp = NewTempDirectory();
        IDirectorySync sync = GetServices().GetRequiredService<IDirectorySync>();
        string target = Path.Combine(temp.Path, "real", "sync");

        sync.CreateDurably(target).Should().HaveCount(2);
        Directory.Exists(target).Should().BeTrue();
    }

    [TestMethod]
    public void CreateDurably_FileInTheWay_ThrowsAndFlushesNothing()
    {
        using ITempDirectory temp = NewTempDirectory();
        IDirectorySync sync = Sync();
        string file = Path.Combine(temp.Path, "file");
        File.WriteAllText(file, "x");

        Action act = () => sync.CreateDurably(Path.Combine(file, "below"));

        act.Should().Throw<IOException>();
        sync.ReceivedCalls().Should().BeEmpty();
    }

    [TestMethod]
    public void CreateDurably_FlushFails_ThrowsAndTheDirectoriesStay()
    {
        using ITempDirectory temp = NewTempDirectory();
        IDirectorySync sync = Substitute.For<IDirectorySync>();
        sync.Flush(Arg.Any<string>()).Throws(new IOException("flush failed"));
        string target = Path.Combine(temp.Path, "kept");

        Action act = () => sync.CreateDurably(target);

        act.Should().Throw<IOException>().WithMessage("flush failed");
        Directory.Exists(target).Should().BeTrue();
    }

    [TestMethod]
    public void CreateDurably_ConcurrentCallers_AllSucceed()
    {
        using ITempDirectory temp = NewTempDirectory();
        IDirectorySync sync = Sync();
        string target = Path.Combine(temp.Path, "shared", "leaf");

        Parallel.For(0, 8, _ => sync.CreateDurably(target));

        Directory.Exists(target).Should().BeTrue();
    }

    [TestMethod]
    public void CreateDurably_InvalidArguments_Throw()
    {
        // WHY: the null-forgiving operator is banned, tests included. A default array element is a
        // genuine null the compiler types as non-null, as a mis-wired caller would pass.
        IDirectorySync missingSync = (new IDirectorySync[1])[0];
        string missingDirectory = (new string[1])[0];

        FluentActions.Invoking(() => missingSync.CreateDurably("x")).Should().Throw<ArgumentNullException>().WithParameterName("sync");
        FluentActions.Invoking(() => Sync().CreateDurably(missingDirectory)).Should().Throw<ArgumentNullException>().WithParameterName("directory");
        FluentActions.Invoking(() => Sync().CreateDurably(string.Empty)).Should().Throw<ArgumentException>().WithParameterName("directory");
    }

    private static IDirectorySync Sync()
    {
        IDirectorySync sync = Substitute.For<IDirectorySync>();
        sync.Flush(Arg.Any<string>()).Returns(DirectorySyncResult.Flushed);
        return sync;
    }

    private ITempDirectory NewTempDirectory()
        => GetServices().GetRequiredService<ITempDirectory.Create>()(null, "create-durably-test");
}
