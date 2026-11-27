using System.Globalization;
using System.Text;
using System.Text.Json;
using Autofac;
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Nordstein.Core.Common.Io;
using Nordstein.Core.Common.Lifecycle;
using Nordstein.Core.Common.Security;
using Nordstein.Core.Common.Tests.Security.Internal;
using Nordstein.Core.Common.Time;
using Nordstein.Core.Testing;
using NSubstitute;

namespace Nordstein.Core.Common.Tests.Security;

[TestClass]
public sealed class MasterKeyRingInitializerTests : BaseTest<Module>
{
    private static DateTimeOffset Now => new(2026, 8, 19, 12, 30, 0, TimeSpan.Zero);

    [TestMethod]
    public async Task Initialize_ExistingEmptyDirectory_WritesARingTheRingLoads()
    {
        using ITempDirectory parent = Parent();
        string keyDir = Directory.CreateDirectory(parent.Combine("keys")).FullName;
        var ringId = Guid.NewGuid();
        IServiceProvider services = Services(Options(keyDir), Clock(Now));

        MasterKeyRingInitialized created = await services.GetRequiredService<IMasterKeyRingInitializer>()
            .InitializeAsync(ringId, CancellationToken);

        created.Should().Be(new MasterKeyRingInitialized(Path.Combine(keyDir, KeyRingFiles.FileName), ringId, CreatedDirectory: false));
        IMasterKeyRing ring = services.GetRequiredService<IMasterKeyRing>();
        ring.RingId.Should().Be(ringId);
        ring.ActiveKeyVersion.Should().Be(1);
        ring.Unwrap(ring.Wrap("value"u8, "slot"u8), "slot"u8).Should().Equal("value"u8.ToArray());
        if (!OperatingSystem.IsWindows())
        {
            File.GetUnixFileMode(created.Path).Should().Be(KeyRingFiles.Mode0600);
            File.GetUnixFileMode(keyDir).Should().Be(KeyRingFiles.Mode0700);
        }
    }

    [TestMethod]
    public async Task Initialize_Ring_HasThePublishedFormat()
    {
        using ITempDirectory parent = Parent();
        string keyDir = parent.Combine("keys");
        var ringId = Guid.NewGuid();

        MasterKeyRingInitialized created = await Initializer(Options(keyDir), Clock(Now)).InitializeAsync(ringId, CancellationToken);

        using JsonDocument ring = JsonDocument.Parse(await File.ReadAllBytesAsync(created.Path, CancellationToken));
        JsonElement root = ring.RootElement;
        root.EnumerateObject().Select(member => member.Name).Should().Equal("format", "ringId", "keys");
        root.GetProperty("format").GetString().Should().Be(KeyRingFiles.FormatMarker);
        root.GetProperty("ringId").GetString().Should().Be(ringId.ToString("D"));
        JsonElement key = root.GetProperty("keys").EnumerateArray().Should().ContainSingle().Which;
        key.EnumerateObject().Select(member => member.Name).Should().Equal("version", "createdAt", "status", "key", "canary");
        key.GetProperty("version").GetInt32().Should().Be(1);
        key.GetProperty("createdAt").GetString().Should().Be(Now.ToString("O", CultureInfo.InvariantCulture));
        key.GetProperty("status").GetString().Should().Be("active");
        byte[] keyBytes = key.GetProperty("key").GetBytesFromBase64();
        keyBytes.Should().HaveCount(32);
        JsonElement canary = key.GetProperty("canary");
        KeyRingFiles.Open(keyBytes, canary.GetProperty("nonce").GetBytesFromBase64(), canary.GetProperty("ciphertext").GetBytesFromBase64(), [])
            .Should().Equal(Encoding.ASCII.GetBytes(KeyRingFiles.CanaryPlaintext));
    }

    [TestMethod]
    public async Task Initialize_TwoRings_HoldDifferentKeys()
    {
        using ITempDirectory parent = Parent();
        string first = parent.Combine("first");
        string second = parent.Combine("second");

        await Initializer(Options(first)).InitializeAsync(Guid.NewGuid(), CancellationToken);
        await Initializer(Options(second)).InitializeAsync(Guid.NewGuid(), CancellationToken);

        KeyRingFiles.ReadKey(first, 1).Should().NotEqual(KeyRingFiles.ReadKey(second, 1));
    }

    [TestMethod]
    public async Task Initialize_MissingDirectory_CreatesItOwnerOnly()
    {
        using ITempDirectory parent = Parent();
        string keyDir = parent.Combine("keys");

        MasterKeyRingInitialized created = await Initializer(Options(keyDir)).InitializeAsync(Guid.NewGuid(), CancellationToken);

        created.CreatedDirectory.Should().BeTrue();
        File.Exists(created.Path).Should().BeTrue();
        if (!OperatingSystem.IsWindows())
        {
            File.GetUnixFileMode(keyDir).Should().Be(KeyRingFiles.Mode0700);
        }
    }

    [TestMethod]
    public async Task Initialize_ExistingDirectoryOpenToOthers_IsMadeOwnerOnly()
    {
        SkipWithoutUnixModes();
        using ITempDirectory parent = Parent();
        string keyDir = Directory.CreateDirectory(parent.Combine("keys")).FullName;
        KeyRingFiles.SetMode(keyDir, KeyRingFiles.Mode0700 | UnixFileMode.GroupRead | UnixFileMode.GroupExecute | UnixFileMode.OtherRead | UnixFileMode.OtherExecute);

        await Initializer(Options(keyDir)).InitializeAsync(Guid.NewGuid(), CancellationToken);

        KeyRingFiles.ModeOf(keyDir).Should().Be(KeyRingFiles.Mode0700);
    }

    [TestMethod]
    public async Task Initialize_MissingParent_ThrowsAndCreatesNothing()
    {
        using ITempDirectory parent = Parent();
        string keyDir = parent.Combine(Path.Combine("absent", "keys"));

        Func<Task> act = () => Initializer(Options(keyDir)).InitializeAsync(Guid.NewGuid(), CancellationToken);

        await act.Should().ThrowAsync<DirectoryNotFoundException>();
        Directory.Exists(parent.Combine("absent")).Should().BeFalse();
    }

    [TestMethod]
    public async Task Initialize_EmptyRingId_ThrowsAndCreatesNothing()
    {
        using ITempDirectory parent = Parent();
        string keyDir = parent.Combine("keys");

        Func<Task> act = () => Initializer(Options(keyDir)).InitializeAsync(Guid.Empty, CancellationToken);

        (await act.Should().ThrowAsync<ArgumentException>()).Which.ParamName.Should().Be("ringId");
        Directory.Exists(keyDir).Should().BeFalse();
    }

    [TestMethod]
    public async Task Initialize_CancelledBeforehand_CreatesNothing()
    {
        using ITempDirectory parent = Parent();
        string keyDir = parent.Combine("keys");
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        Func<Task> act = () => Initializer(Options(keyDir)).InitializeAsync(Guid.NewGuid(), cancelled.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        Directory.Exists(keyDir).Should().BeFalse();
    }

    [TestMethod]
    public async Task Initialize_CancelledDuringTheWrite_RemovesTheRingAndTheDirectoryItCreated()
    {
        // The clock is read after the directory exists and before the ring is opened; cancelling there makes
        // the write observe the cancellation deterministically.
        using ITempDirectory parent = Parent();
        string keyDir = parent.Combine("keys");
        using var cancellation = new CancellationTokenSource();
        IClock cancellingClock = Substitute.For<IClock>();
        cancellingClock.UtcNow.Returns(_ =>
        {
            cancellation.Cancel();
            return Now;
        });

        Func<Task> act = () => Initializer(Options(keyDir), cancellingClock).InitializeAsync(Guid.NewGuid(), cancellation.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        Directory.Exists(keyDir).Should().BeFalse("the directory this call created goes with its failure");
    }

    [TestMethod]
    public async Task Initialize_CancelledDuringTheWriteInAnExistingDirectory_RemovesOnlyTheRing()
    {
        using ITempDirectory parent = Parent();
        string keyDir = Directory.CreateDirectory(parent.Combine("keys")).FullName;
        using var cancellation = new CancellationTokenSource();
        IClock cancellingClock = Substitute.For<IClock>();
        cancellingClock.UtcNow.Returns(_ =>
        {
            cancellation.Cancel();
            return Now;
        });

        Func<Task> act = () => Initializer(Options(keyDir), cancellingClock).InitializeAsync(Guid.NewGuid(), cancellation.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        Directory.Exists(keyDir).Should().BeTrue("a directory that existed before is never removed");
        Directory.EnumerateFileSystemEntries(keyDir).Should().BeEmpty();
    }

    [TestMethod]
    public async Task Initialize_FailureBeforeTheOpenInACreatedDirectory_RemovesTheDirectory()
    {
        using ITempDirectory parent = Parent();
        string keyDir = parent.Combine("keys");
        IClock failingClock = Substitute.For<IClock>();
        failingClock.UtcNow.Returns<DateTimeOffset>(_ => throw new InvalidOperationException("clock failed"));

        Func<Task> act = () => Initializer(Options(keyDir), failingClock).InitializeAsync(Guid.NewGuid(), CancellationToken);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("clock failed");
        Directory.Exists(keyDir).Should().BeFalse();
    }

    [TestMethod]
    public async Task Initialize_RingExists_RefusesWithoutTouchingIt()
    {
        using ITempDirectory parent = Parent();
        string keyDir = parent.Combine("keys");
        IMasterKeyRingInitializer initializer = Initializer(Options(keyDir));
        string path = (await initializer.InitializeAsync(Guid.NewGuid(), CancellationToken)).Path;
        byte[] original = await File.ReadAllBytesAsync(path, CancellationToken);

        Func<Task> again = () => initializer.InitializeAsync(Guid.NewGuid(), CancellationToken);

        (await again.Should().ThrowAsync<MasterKeyRingException>()).Which.Failure.Should().Be(MasterKeyRingFailure.AlreadyInitialized);
        (await File.ReadAllBytesAsync(path, CancellationToken)).Should().Equal(original);
    }

    [TestMethod]
    public async Task Initialize_DirectoryNotEmpty_RefusesWithoutTouchingIt()
    {
        using ITempDirectory parent = Parent();
        string keyDir = Directory.CreateDirectory(parent.Combine("keys")).FullName;
        await File.WriteAllTextAsync(Path.Combine(keyDir, "stray.txt"), "x", CancellationToken);

        Func<Task> act = () => Initializer(Options(keyDir)).InitializeAsync(Guid.NewGuid(), CancellationToken);

        (await act.Should().ThrowAsync<MasterKeyRingException>()).Which.Failure.Should().Be(MasterKeyRingFailure.AlreadyInitialized);
        Directory.EnumerateFileSystemEntries(keyDir).Should().ContainSingle();
    }

    [TestMethod]
    public async Task Initialize_DanglingLinkAtTheRingPath_RefusesAndLeavesIt()
    {
        SkipWithoutUnixModes();
        using ITempDirectory parent = Parent();
        string keyDir = Directory.CreateDirectory(parent.Combine("keys")).FullName;
        string path = Path.Combine(keyDir, KeyRingFiles.FileName);
        File.CreateSymbolicLink(path, Path.Combine(parent.Path, "nowhere"));

        Func<Task> act = () => Initializer(Options(keyDir)).InitializeAsync(Guid.NewGuid(), CancellationToken);

        (await act.Should().ThrowAsync<MasterKeyRingException>()).Which.Failure.Should().Be(MasterKeyRingFailure.AlreadyInitialized);
        new FileInfo(path).LinkTarget.Should().NotBeNull("the entry that was there before is left alone");
    }

    [TestMethod]
    public async Task Initialize_RingAppearsBetweenTheCheckAndTheOpen_RefusesAndKeepsTheOtherRing()
    {
        // The clock is read between the check and the open, so a clock that writes another run's ring makes the
        // race deterministic. The loser refuses, and neither the winner's ring nor its directory is deleted.
        using ITempDirectory parent = Parent();
        string keyDir = parent.Combine("keys");
        string path = Path.Combine(keyDir, KeyRingFiles.FileName);
        byte[] winner = "the other run's ring"u8.ToArray();
        IClock racingClock = Substitute.For<IClock>();
        racingClock.UtcNow.Returns(_ =>
        {
            File.WriteAllBytes(path, winner);
            return Now;
        });

        Func<Task> act = () => Initializer(Options(keyDir), racingClock).InitializeAsync(Guid.NewGuid(), CancellationToken);

        // The inner IOException is the create-new refusal; the check's own refusal carries none.
        MasterKeyRingException thrown = (await act.Should().ThrowAsync<MasterKeyRingException>()).Which;
        thrown.Failure.Should().Be(MasterKeyRingFailure.AlreadyInitialized);
        thrown.InnerException.Should().BeOfType<IOException>();
        (await File.ReadAllBytesAsync(path, CancellationToken)).Should().Equal(winner);
    }

    [TestMethod]
    public async Task Initialize_KeyDirectoryInsideAnExcludedRoot_RefusesBeforeCreatingAnything()
    {
        using ITempDirectory parent = Parent();
        string backedUp = Directory.CreateDirectory(parent.Combine("backed-up")).FullName;
        string keyDir = Path.Combine(backedUp, "keys");

        Func<Task> act = () => Initializer(Options(keyDir, [backedUp])).InitializeAsync(Guid.NewGuid(), CancellationToken);

        (await act.Should().ThrowAsync<MasterKeyRingException>()).Which.Failure.Should().Be(MasterKeyRingFailure.PathInExcludedRoot);
        Directory.Exists(keyDir).Should().BeFalse();
    }

    [TestMethod]
    public async Task Initialize_KeyDirectorySwappedForALinkIntoAnExcludedRootAfterTheCheck_RefusesAndRemovesTheRing()
    {
        // The clock is read between the check and the open, so a clock that swaps the directory this call just
        // created for a link into the excluded root makes the race deterministic.
        SkipWithoutUnixModes();
        using ITempDirectory parent = Parent();
        string backedUp = Directory.CreateDirectory(parent.Combine("backed-up")).FullName;
        string inside = KeyRingFiles.CreateKeyDirectory(Path.Combine(backedUp, "keys"));
        string keyDir = parent.Combine("keys");
        IClock swappingClock = Substitute.For<IClock>();
        swappingClock.UtcNow.Returns(_ =>
        {
            Directory.Delete(keyDir);
            Directory.CreateSymbolicLink(keyDir, inside);
            return Now;
        });

        Func<Task> act = () => Initializer(Options(keyDir, [backedUp]), swappingClock).InitializeAsync(Guid.NewGuid(), CancellationToken);

        (await act.Should().ThrowAsync<MasterKeyRingException>()).Which.Failure.Should().Be(MasterKeyRingFailure.PathInExcludedRoot);
        Directory.EnumerateFileSystemEntries(inside).Should().BeEmpty("the ring written into the excluded root is removed");
    }

    [TestMethod]
    public async Task Initialize_RingReplacedBeforeTheSecondCheck_RefusesAndKeepsTheOtherRing()
    {
        // A placement check that fails only after the write, while another ring now sits at the path: the
        // cleanup must not delete a ring this call did not write.
        using ITempDirectory parent = Parent();
        string keyDir = parent.Combine("keys");
        string path = Path.Combine(keyDir, KeyRingFiles.FileName);
        byte[] other = Encoding.UTF8.GetBytes($"{{\"format\":\"{KeyRingFiles.FormatMarker}\",\"ringId\":\"{Guid.NewGuid():D}\",\"keys\":[]}}");
        int checks = 0;
        IPathContainment containment = Substitute.For<IPathContainment>();
        containment.MayBeWithin(Arg.Any<string>(), Arg.Any<string>()).Returns(_ =>
        {
            if (++checks == 1)
            {
                return false;
            }

            File.WriteAllBytes(path, other);
            return true;
        });

        Func<Task> act = () => Initializer(Options(keyDir, ["/excluded"]), containment: containment).InitializeAsync(Guid.NewGuid(), CancellationToken);

        (await act.Should().ThrowAsync<MasterKeyRingException>()).Which.Failure.Should().Be(MasterKeyRingFailure.PathInExcludedRoot);
        (await File.ReadAllBytesAsync(path, CancellationToken)).Should().Equal(other);
    }

    [TestMethod]
    public async Task Initialize_RingUnreadableAtTheSecondCheck_RefusesAndKeepsIt()
    {
        using ITempDirectory parent = Parent();
        string keyDir = parent.Combine("keys");
        string path = Path.Combine(keyDir, KeyRingFiles.FileName);
        int checks = 0;
        IPathContainment containment = Substitute.For<IPathContainment>();
        containment.MayBeWithin(Arg.Any<string>(), Arg.Any<string>()).Returns(_ =>
        {
            if (++checks == 1)
            {
                return false;
            }

            File.WriteAllText(path, "not a ring");
            return true;
        });

        Func<Task> act = () => Initializer(Options(keyDir, ["/excluded"]), containment: containment).InitializeAsync(Guid.NewGuid(), CancellationToken);

        (await act.Should().ThrowAsync<MasterKeyRingException>()).Which.Failure.Should().Be(MasterKeyRingFailure.PathInExcludedRoot);
        (await File.ReadAllTextAsync(path, CancellationToken)).Should().Be("not a ring");
    }

    [TestMethod]
    public async Task Initialize_ExistingDirectory_FlushesTheKeyDirectoryOnceTheRingIsThere()
    {
        using ITempDirectory parent = Parent();
        string keyDir = Directory.CreateDirectory(parent.Combine("keys")).FullName;
        var sync = new RecordingDirectorySync();

        await Initializer(Options(keyDir), sync: sync).InitializeAsync(Guid.NewGuid(), CancellationToken);

        sync.Flushes.Should().ContainSingle();
        sync.Flushes[0].Directory.Should().Be(keyDir);
        sync.Flushes[0].Entries.Should().Equal(KeyRingFiles.FileName);
    }

    [TestMethod]
    public async Task Initialize_MissingDirectory_FlushesItsParentThenTheKeyDirectory()
    {
        using ITempDirectory parent = Parent();
        string keyDir = parent.Combine("keys");
        var sync = new RecordingDirectorySync();

        await Initializer(Options(keyDir), sync: sync).InitializeAsync(Guid.NewGuid(), CancellationToken);

        sync.Flushes.Select(flush => flush.Directory).Should().Equal(Path.GetFullPath(parent.Path), keyDir);
        sync.Flushes[0].Entries.Should().Equal("keys");
        sync.Flushes[1].Entries.Should().Equal(KeyRingFiles.FileName);
    }

    [TestMethod]
    public async Task Initialize_KeyDirectoryFlushFails_ThrowsAndRemovesTheRing()
    {
        using ITempDirectory parent = Parent();
        string keyDir = Directory.CreateDirectory(parent.Combine("keys")).FullName;
        var sync = new RecordingDirectorySync { Failure = new IOException("fsync failed") };

        Func<Task> act = () => Initializer(Options(keyDir), sync: sync).InitializeAsync(Guid.NewGuid(), CancellationToken);

        await act.Should().ThrowAsync<IOException>().WithMessage("fsync failed");
        Directory.EnumerateFileSystemEntries(keyDir).Should().BeEmpty("a ring whose name is not durable must not stay behind");
    }

    [TestMethod]
    public async Task Initialize_ParentFlushFailsForACreatedDirectory_ThrowsAndRemovesTheDirectory()
    {
        using ITempDirectory parent = Parent();
        string keyDir = parent.Combine("keys");
        var sync = new RecordingDirectorySync { Failure = new IOException("fsync failed") };

        Func<Task> act = () => Initializer(Options(keyDir), sync: sync).InitializeAsync(Guid.NewGuid(), CancellationToken);

        await act.Should().ThrowAsync<IOException>().WithMessage("fsync failed");
        Directory.Exists(keyDir).Should().BeFalse();
    }

    [TestMethod]
    public async Task Initialize_KeyDirectoryFlushFailsForACreatedDirectory_RemovesTheRingAndTheDirectory()
    {
        using ITempDirectory parent = Parent();
        string keyDir = parent.Combine("keys");
        var sync = new RecordingDirectorySync { Failure = new IOException("fsync failed"), FailAt = 1 };

        Func<Task> act = () => Initializer(Options(keyDir), sync: sync).InitializeAsync(Guid.NewGuid(), CancellationToken);

        await act.Should().ThrowAsync<IOException>().WithMessage("fsync failed");
        Directory.Exists(keyDir).Should().BeFalse();
    }

    [TestMethod]
    public void Precheck_RingPresentOrDirectoryNotEmptyOrInsideAnExcludedRoot_RefusesWithoutWriting()
    {
        using ITempDirectory parent = Parent();
        string backedUp = Directory.CreateDirectory(parent.Combine("backed-up")).FullName;
        string withRing = Directory.CreateDirectory(parent.Combine("with-ring")).FullName;
        File.WriteAllText(Path.Combine(withRing, KeyRingFiles.FileName), "{}");
        string notEmpty = Directory.CreateDirectory(parent.Combine("not-empty")).FullName;
        File.WriteAllText(Path.Combine(notEmpty, "stray"), "x");
        string clean = Directory.CreateDirectory(parent.Combine("clean")).FullName;

        FailureOf(() => Initializer(Options(withRing, [backedUp])).Precheck()).Should().Be(MasterKeyRingFailure.AlreadyInitialized);
        FailureOf(() => Initializer(Options(notEmpty, [backedUp])).Precheck()).Should().Be(MasterKeyRingFailure.AlreadyInitialized);
        FailureOf(() => Initializer(Options(Path.Combine(backedUp, "keys"), [backedUp])).Precheck()).Should().Be(MasterKeyRingFailure.PathInExcludedRoot);
        FailureOf(() => Initializer(Options(clean, [backedUp])).Precheck()).Should().BeNull();
        FailureOf(() => Initializer(Options(parent.Combine("missing"), [backedUp])).Precheck()).Should().BeNull();
        Directory.EnumerateFileSystemEntries(clean).Should().BeEmpty("a precheck writes nothing");
        Directory.Exists(parent.Combine("missing")).Should().BeFalse("a precheck creates nothing");
    }

    [TestMethod]
    public void Precheck_InsideAnExcludedRootInAnotherCaseOrWithATrailingSeparator_Refuses()
    {
        using ITempDirectory parent = Parent();
        string backedUp = Directory.CreateDirectory(parent.Combine("backup")).FullName;

        FailureOf(() => Initializer(Options(parent.Combine(Path.Combine("Backup", "keys")), [backedUp])).Precheck())
            .Should().Be(MasterKeyRingFailure.PathInExcludedRoot);
        FailureOf(() => Initializer(Options(Path.Combine(backedUp, "keys"), [backedUp + Path.DirectorySeparatorChar])).Precheck())
            .Should().Be(MasterKeyRingFailure.PathInExcludedRoot);
        FailureOf(() => Initializer(Options(parent.Combine(Path.Combine("backup2", "keys")), [backedUp])).Precheck())
            .Should().BeNull();
    }

    [TestMethod]
    public void Precheck_KeyDirectoryStepsBackFromALinkIntoAnExcludedRoot_RefusesAsTheLoadWould()
    {
        // <parent>/outside/link -> <backup>/inner: the text-collapsed path is <parent>/outside/keys, but the
        // operating system walks <parent>/outside/link/../keys into the excluded root.
        SkipWithoutUnixModes();
        using ITempDirectory parent = Parent();
        string backedUp = Directory.CreateDirectory(parent.Combine("backup")).FullName;
        string inner = Directory.CreateDirectory(Path.Combine(backedUp, "inner")).FullName;
        string outside = Directory.CreateDirectory(parent.Combine("outside")).FullName;
        Directory.CreateSymbolicLink(Path.Combine(outside, "link"), inner);

        FailureOf(() => Initializer(Options(Path.Combine(outside, "link", "..", "keys"), [backedUp])).Precheck())
            .Should().Be(MasterKeyRingFailure.PathInExcludedRoot);
    }

    [TestMethod]
    public void Precheck_KeyDirectoryIsALinkLoop_RefusesWithFileRejected()
    {
        SkipWithoutUnixModes();
        using ITempDirectory parent = Parent();
        string backedUp = Directory.CreateDirectory(parent.Combine("backup")).FullName;
        string keyDir = parent.Combine("keys");
        string other = parent.Combine("other");
        Directory.CreateSymbolicLink(keyDir, other);
        Directory.CreateSymbolicLink(other, keyDir);

        Action act = () => Initializer(Options(keyDir, [backedUp])).Precheck();

        MasterKeyRingException thrown = act.Should().Throw<MasterKeyRingException>().Which;
        thrown.Failure.Should().Be(MasterKeyRingFailure.FileRejected);
        thrown.InnerException.Should().BeOfType<IOException>();
    }

    [TestMethod]
    [DataRow("[]")]
    [DataRow("{}")]
    [DataRow("{\"ringId\":1}")]
    [DataRow("{\"ringId\":\"not-a-guid\"}")]
    [DataRow("not json")]
    public async Task Remove_FileThatIsNotARing_RefusesAndDeletesNothing(string text)
    {
        using ITempDirectory parent = Parent();
        string keyDir = parent.Combine("keys");
        IMasterKeyRingInitializer initializer = Initializer(Options(keyDir));
        MasterKeyRingInitialized created = await initializer.InitializeAsync(Guid.NewGuid(), CancellationToken);
        await File.WriteAllTextAsync(created.Path, text, CancellationToken);

        Action act = () => initializer.Remove(created);

        act.Should().Throw<InvalidOperationException>();
        (await File.ReadAllTextAsync(created.Path, CancellationToken)).Should().Be(text);
    }

    [TestMethod]
    public async Task Remove_CreatedDirectory_DeletesTheRingAndTheDirectory()
    {
        using ITempDirectory parent = Parent();
        string keyDir = parent.Combine("keys");
        IMasterKeyRingInitializer initializer = Initializer(Options(keyDir));
        MasterKeyRingInitialized created = await initializer.InitializeAsync(Guid.NewGuid(), CancellationToken);

        initializer.Remove(created);

        File.Exists(created.Path).Should().BeFalse();
        Directory.Exists(keyDir).Should().BeFalse();
    }

    [TestMethod]
    public async Task Remove_ExistingDirectory_DeletesTheRingOnly()
    {
        using ITempDirectory parent = Parent();
        string keyDir = Directory.CreateDirectory(parent.Combine("keys")).FullName;
        IMasterKeyRingInitializer initializer = Initializer(Options(keyDir));
        MasterKeyRingInitialized created = await initializer.InitializeAsync(Guid.NewGuid(), CancellationToken);

        initializer.Remove(created);

        File.Exists(created.Path).Should().BeFalse();
        Directory.Exists(keyDir).Should().BeTrue("a directory that existed before is never removed");
    }

    [TestMethod]
    public async Task Remove_RingReplacedSince_RefusesAndDeletesNothing()
    {
        using ITempDirectory parent = Parent();
        string keyDir = parent.Combine("keys");
        IMasterKeyRingInitializer initializer = Initializer(Options(keyDir));
        MasterKeyRingInitialized created = await initializer.InitializeAsync(Guid.NewGuid(), CancellationToken);
        byte[] original = await File.ReadAllBytesAsync(created.Path, CancellationToken);

        Action otherRing = () => initializer.Remove(created with { RingId = Guid.NewGuid() });
        Action missing = () => initializer.Remove(created with { Path = Path.Combine(keyDir, "absent.json") });

        otherRing.Should().Throw<InvalidOperationException>();
        missing.Should().Throw<InvalidOperationException>();
        (await File.ReadAllBytesAsync(created.Path, CancellationToken)).Should().Equal(original);
        Directory.Exists(keyDir).Should().BeTrue();
    }

    [TestMethod]
    public async Task Remove_CreatedDirectoryNoLongerEmpty_DeletesTheRingAndThrows()
    {
        using ITempDirectory parent = Parent();
        string keyDir = parent.Combine("keys");
        IMasterKeyRingInitializer initializer = Initializer(Options(keyDir));
        MasterKeyRingInitialized created = await initializer.InitializeAsync(Guid.NewGuid(), CancellationToken);
        await File.WriteAllTextAsync(Path.Combine(keyDir, "later.txt"), "x", CancellationToken);

        Action act = () => initializer.Remove(created);

        act.Should().Throw<IOException>();
        File.Exists(created.Path).Should().BeFalse();
        File.Exists(Path.Combine(keyDir, "later.txt")).Should().BeTrue();
    }

    [TestMethod]
    public void Remove_Null_ThrowsArgumentNull()
    {
        IMasterKeyRingInitializer initializer = Initializer(Options("/keys"));
        MasterKeyRingInitialized missing = (new MasterKeyRingInitialized[1])[0];

        Action act = () => initializer.Remove(missing);

        act.Should().Throw<ArgumentNullException>().Which.ParamName.Should().Be("created");
    }

    [TestMethod]
    public void Container_ResolvesOneInstance()
    {
        IServiceProvider services = Services(Options("/keys"));

        services.GetRequiredService<IMasterKeyRingInitializer>().Should().BeSameAs(services.GetRequiredService<IMasterKeyRingInitializer>());
    }

    private static MasterKeyRingFailure? FailureOf(Action act)
    {
        try
        {
            act();
            return null;
        }
        catch (MasterKeyRingException exception)
        {
            return exception.Failure;
        }
    }

    private static MasterKeyRingOptions Options(string keyDirectory, IReadOnlyList<string>? excludedRoots = null)
        => new() { KeyDirectory = keyDirectory, ExcludedRoots = excludedRoots ?? [] };

    private static IClock Clock(DateTimeOffset now)
    {
        IClock clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(now);
        return clock;
    }

    private IMasterKeyRingInitializer Initializer(
        MasterKeyRingOptions options, IClock? clock = null, IDirectorySync? sync = null, IPathContainment? containment = null)
        => Services(options, clock, sync, containment).GetRequiredService<IMasterKeyRingInitializer>();

    private IServiceProvider Services(
        MasterKeyRingOptions options, IClock? clock = null, IDirectorySync? sync = null, IPathContainment? containment = null)
        => GetServices(builder =>
        {
            builder.RegisterInstance(options);
            if (clock is not null)
            {
                builder.RegisterInstance(clock).As<IClock>();
            }

            if (sync is not null)
            {
                builder.RegisterInstance(sync).As<IDirectorySync>();
            }

            if (containment is not null)
            {
                builder.RegisterInstance(containment).As<IPathContainment>();
            }
        });

    private ITempDirectory Parent() => GetServices().GetTempDirectory("keyring-init-test");

    private static void SkipWithoutUnixModes()
    {
        if (OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("Unix file modes and symbolic links are not evaluated on Windows.");
        }
    }
}
