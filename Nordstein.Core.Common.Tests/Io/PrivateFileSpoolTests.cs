using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Nordstein.Core.Common.Io;
using Nordstein.Core.Common.Lifecycle;
using Nordstein.Core.Testing;

namespace Nordstein.Core.Common.Tests.Io;

[TestClass]
public sealed class PrivateFileSpoolTests : BaseTest<Module>
{
    private const UnixFileMode OwnerReadWrite = UnixFileMode.UserRead | UnixFileMode.UserWrite;
    private const UnixFileMode OwnerOnlyDirectory = OwnerReadWrite | UnixFileMode.UserExecute;

    private IPrivateFileSpool Spool => GetServices().GetRequiredService<IPrivateFileSpool>();

    private ITempDirectory NewTempDirectory()
        => GetServices().GetRequiredService<ITempDirectory.Create>()(null, "private-file-spool-test");

    [TestMethod]
    public async Task SpoolAsync_Payload_WritesTheBytesAndReturnsSha256AndLength()
    {
        using ITempDirectory temp = NewTempDirectory();
        string path = temp.Combine("item.spool");
        byte[] payload = Encoding.UTF8.GetBytes("private spool fixture");

        SpooledContent spooled = await Spool.SpoolAsync(new MemoryStream(payload), path, 1024, CancellationToken);

        spooled.Sha256.ToArray().Should().Equal(SHA256.HashData(payload));
        spooled.Length.Should().Be(payload.LongLength);
        (await File.ReadAllBytesAsync(path, CancellationToken)).Should().Equal(payload);
    }

    [TestMethod]
    public async Task SpoolAsync_PayloadLargerThanOneBuffer_HashesAndCopiesEveryByte()
    {
        using ITempDirectory temp = NewTempDirectory();
        string path = temp.Combine("item.spool");
        byte[] payload = RandomNumberGenerator.GetBytes(200_000);

        SpooledContent spooled = await Spool.SpoolAsync(
            new ChunkedStream(payload, 7_001), path, payload.LongLength, CancellationToken);

        spooled.Sha256.ToArray().Should().Equal(SHA256.HashData(payload));
        spooled.Length.Should().Be(payload.LongLength);
        (await File.ReadAllBytesAsync(path, CancellationToken)).Should().Equal(payload);
    }

    [TestMethod]
    public async Task SpoolAsync_EmptyPayload_WritesAnEmptyFile()
    {
        using ITempDirectory temp = NewTempDirectory();
        string path = temp.Combine("item.spool");

        SpooledContent spooled = await Spool.SpoolAsync(new MemoryStream(), path, 0, CancellationToken);

        spooled.Length.Should().Be(0);
        spooled.Sha256.ToArray().Should().Equal(SHA256.HashData(ReadOnlySpan<byte>.Empty));
        new FileInfo(path).Length.Should().Be(0);
    }

    [TestMethod]
    public async Task SpoolAsync_PayloadExactlyAtTheCap_IsAccepted()
    {
        using ITempDirectory temp = NewTempDirectory();
        string path = temp.Combine("item.spool");

        SpooledContent spooled = await Spool.SpoolAsync(new MemoryStream(new byte[16]), path, 16, CancellationToken);

        spooled.Length.Should().Be(16);
    }

    [TestMethod]
    public async Task SpoolAsync_PayloadOneByteOverTheCap_IsRefused()
    {
        using ITempDirectory temp = NewTempDirectory();
        string path = temp.Combine("item.spool");

        Func<Task> act = () => Spool.SpoolAsync(new MemoryStream(new byte[17]), path, 16, CancellationToken);

        await act.Should().ThrowAsync<SpoolLimitExceededException>();
        File.Exists(path).Should().BeFalse();
    }

    [TestMethod]
    public async Task SpoolAsync_CapCrossedMidStream_ThrowsAndLeavesNoFileAndStopsReading()
    {
        using ITempDirectory temp = NewTempDirectory();
        string path = temp.Combine("item.spool");
        var source = new ChunkedStream(new byte[100], 10);

        Func<Task> act = () => Spool.SpoolAsync(source, path, 25, CancellationToken);

        (await act.Should().ThrowAsync<SpoolLimitExceededException>()).Which.MaxBytes.Should().Be(25);
        File.Exists(path).Should().BeFalse();
        source.Position.Should().Be(30, "the copy stops at the read that crosses the cap");
    }

    [TestMethod]
    public async Task SpoolAsync_FileAlreadyAtThePath_ThrowsAndLeavesItUntouched()
    {
        using ITempDirectory temp = NewTempDirectory();
        string path = temp.Combine("item.spool");
        await File.WriteAllTextAsync(path, "someone else's", CancellationToken);

        Func<Task> act = () => Spool.SpoolAsync(new MemoryStream(new byte[4]), path, 1024, CancellationToken);

        (await act.Should().ThrowAsync<DestinationAlreadyExistsException>()).Which.Path.Should().Be(path);
        (await File.ReadAllTextAsync(path, CancellationToken)).Should().Be("someone else's");
    }

    [TestMethod]
    public async Task SpoolAsync_CancelledBeforeStart_ThrowsAndCreatesNothing()
    {
        using ITempDirectory temp = NewTempDirectory();
        string directory = temp.Combine("spool");
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        Func<Task> act = () => Spool.SpoolAsync(
            new MemoryStream(new byte[4]), Path.Combine(directory, "item.spool"), 1024, cancellation.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        Directory.Exists(directory).Should().BeFalse();
    }

    [TestMethod]
    public async Task SpoolAsync_CancelledMidStream_ThrowsAndLeavesNoFile()
    {
        using ITempDirectory temp = NewTempDirectory();
        string path = temp.Combine("item.spool");
        using var cancellation = new CancellationTokenSource();
        var source = new ChunkedStream(new byte[100], 10, afterFirstRead: cancellation.Cancel);

        Func<Task> act = () => Spool.SpoolAsync(source, path, 1024, cancellation.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        File.Exists(path).Should().BeFalse();
    }

    [TestMethod]
    public async Task SpoolAsync_SourceFailsMidStream_RethrowsAndLeavesNoFile()
    {
        using ITempDirectory temp = NewTempDirectory();
        string path = temp.Combine("item.spool");
        var source = new ChunkedStream(new byte[100], 10, afterFirstRead: () => throw new IOException("source broke"));

        Func<Task> act = () => Spool.SpoolAsync(source, path, 1024, CancellationToken);

        await act.Should().ThrowAsync<IOException>().WithMessage("source broke");
        File.Exists(path).Should().BeFalse();
    }

    [TestMethod]
    [OSCondition(OperatingSystems.Linux | OperatingSystems.OSX | OperatingSystems.FreeBSD)]
    [UnsupportedOSPlatform("windows")]
    public async Task SpoolAsync_OnUnix_CreatesTheFileOwnerReadWriteOnly()
    {
        using ITempDirectory temp = NewTempDirectory();
        string path = temp.Combine("item.spool");

        await Spool.SpoolAsync(new MemoryStream(new byte[4]), path, 1024, CancellationToken);

        File.GetUnixFileMode(path).Should().Be(OwnerReadWrite);
    }

    [TestMethod]
    [OSCondition(OperatingSystems.Linux | OperatingSystems.OSX | OperatingSystems.FreeBSD)]
    [UnsupportedOSPlatform("windows")]
    public async Task SpoolAsync_MissingDirectory_CreatesItOwnerOnly()
    {
        using ITempDirectory temp = NewTempDirectory();
        string directory = temp.Combine("spool");
        string path = Path.Combine(directory, "item.spool");

        await Spool.SpoolAsync(new MemoryStream(new byte[4]), path, 1024, CancellationToken);

        File.GetUnixFileMode(directory).Should().Be(OwnerOnlyDirectory);
        File.Exists(path).Should().BeTrue();
    }

    [TestMethod]
    [OSCondition(OperatingSystems.Linux | OperatingSystems.OSX | OperatingSystems.FreeBSD)]
    [UnsupportedOSPlatform("windows")]
    public async Task SpoolAsync_ExistingDirectory_KeepsItsMode()
    {
        using ITempDirectory temp = NewTempDirectory();
        string directory = temp.Combine("spool");
        const UnixFileMode shared = OwnerOnlyDirectory | UnixFileMode.GroupRead | UnixFileMode.GroupExecute;
        Directory.CreateDirectory(directory, shared);

        await Spool.SpoolAsync(new MemoryStream(new byte[4]), Path.Combine(directory, "item.spool"), 1024, CancellationToken);

        File.GetUnixFileMode(directory).Should().Be(shared);
    }

    [TestMethod]
    public async Task SpoolAsync_RelativePath_ResolvesAgainstTheWorkingDirectory()
    {
        using ITempDirectory temp = NewTempDirectory();
        string relative = Path.GetRelativePath(Environment.CurrentDirectory, temp.Combine("item.spool"));

        await Spool.SpoolAsync(new MemoryStream(new byte[4]), relative, 1024, CancellationToken);

        File.Exists(temp.Combine("item.spool")).Should().BeTrue();
    }

    [TestMethod]
    public async Task SpoolAsync_InvalidArguments_Throw()
    {
        using ITempDirectory temp = NewTempDirectory();
        string path = temp.Combine("item.spool");

        Func<Task> nullSource = () => Spool.SpoolAsync(null!, path, 1, CancellationToken); // guard test
        Func<Task> emptyPath = () => Spool.SpoolAsync(new MemoryStream(), "", 1, CancellationToken);
        Func<Task> negativeCap = () => Spool.SpoolAsync(new MemoryStream(), path, -1, CancellationToken);
        Func<Task> directoryPath = () => Spool.SpoolAsync(new MemoryStream(), temp.Path + Path.DirectorySeparatorChar, 1, CancellationToken);
        Func<Task> rootPath = () => Spool.SpoolAsync(new MemoryStream(), Path.GetPathRoot(temp.Path) ?? "/", 1, CancellationToken);

        await nullSource.Should().ThrowAsync<ArgumentNullException>();
        await emptyPath.Should().ThrowAsync<ArgumentException>();
        await negativeCap.Should().ThrowAsync<ArgumentOutOfRangeException>();
        await directoryPath.Should().ThrowAsync<ArgumentException>();
        await rootPath.Should().ThrowAsync<ArgumentException>();
        File.Exists(path).Should().BeFalse();
    }

    [TestMethod]
    public void SpooledContent_ValidArguments_ExposesThem()
    {
        byte[] hash = SHA256.HashData([1, 2, 3]);

        var content = new SpooledContent(hash, 3);

        content.Sha256.ToArray().Should().Equal(hash);
        content.Length.Should().Be(3);
    }

    [TestMethod]
    public void SpooledContent_InvalidArguments_Throw()
    {
        Action shortHash = () => _ = new SpooledContent(new byte[31], 0);
        Action negativeLength = () => _ = new SpooledContent(new byte[32], -1);

        shortHash.Should().Throw<ArgumentException>().WithParameterName("sha256");
        negativeLength.Should().Throw<ArgumentOutOfRangeException>().WithParameterName("length");
    }

    [TestMethod]
    public void SpoolLimitExceededException_NamesTheCap()
    {
        var exception = new SpoolLimitExceededException(42);

        exception.MaxBytes.Should().Be(42);
        exception.Message.Should().Be("The content exceeds 42 bytes.");
    }

    /// <summary>Serves a buffer in fixed chunks and can run an action after its first read.</summary>
    private sealed class ChunkedStream : Stream
    {
        private readonly byte[] content;
        private readonly int chunk;
        private readonly Action? afterFirstRead;
        private long position;

        public ChunkedStream(byte[] content, int chunk, Action? afterFirstRead = null)
        {
            this.content = content;
            this.chunk = chunk;
            this.afterFirstRead = afterFirstRead;
        }

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => content.Length;

        public override long Position
        {
            get => position;
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count)
            => Read(buffer.AsSpan(offset, count));

        public override int Read(Span<byte> buffer)
        {
            if (position > 0)
            {
                afterFirstRead?.Invoke();
            }

            int read = (int)Math.Min(Math.Min(chunk, buffer.Length), content.Length - position);
            content.AsSpan((int)position, read).CopyTo(buffer);
            position += read;
            return read;
        }

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(Read(buffer.Span));
        }

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
            => ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
