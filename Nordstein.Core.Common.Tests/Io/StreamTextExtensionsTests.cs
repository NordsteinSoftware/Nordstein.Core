using System.Text;
using AwesomeAssertions;
using Nordstein.Core.Common.Io;
using Nordstein.Core.Testing;

namespace Nordstein.Core.Common.Tests.Io;

[TestClass]
public sealed class StreamTextExtensionsTests : BaseTest<Module>
{
    [TestMethod]
    public async Task ReadAllTextAsync_CapExactlyReached_ReturnsTheText()
    {
        using var stream = Utf8("abcde");

        string text = await stream.ReadAllTextAsync(5, leaveOpen: false, CancellationToken);

        text.Should().Be("abcde");
    }

    [TestMethod]
    public async Task ReadAllTextAsync_CapExceeded_Throws_NamingTheCap()
    {
        using var stream = Utf8("abcdef");

        Func<Task> read = () => stream.ReadAllTextAsync(5, leaveOpen: false, CancellationToken);

        (await read.Should().ThrowAsync<TextLimitExceededException>()).Which.MaxCharacters.Should().Be(5);
    }

    [TestMethod]
    public async Task ReadAllTextAsync_CapExceeded_StopsBeforeReadingTheRest()
    {
        using var stream = new CountingStream(Encoding.UTF8.GetBytes(new string('x', 1 << 20)));

        Func<Task> read = () => stream.ReadAllTextAsync(10, leaveOpen: false, CancellationToken);

        await read.Should().ThrowAsync<TextLimitExceededException>();
        stream.BytesRead.Should().BeLessThan(1 << 20);
    }

    [TestMethod]
    public async Task ReadAllTextAsync_LineEndings_BecomeLineFeeds_TheCapCountsTheNormalizedText()
    {
        using var stream = Utf8("a\r\nb\rc\n");

        string text = await stream.ReadAllTextAsync(6, leaveOpen: false, CancellationToken);

        text.Should().Be("a\nb\nc\n");
    }

    [TestMethod]
    public async Task ReadAllTextAsync_ACrLfSplitAcrossReads_IsOneLineFeed()
    {
        using var stream = new ChunkedStream(Encoding.UTF8.GetBytes("a\r\nb\r"), chunk: 2);

        string text = await stream.ReadAllTextAsync(10, leaveOpen: false, CancellationToken);

        text.Should().Be("a\nb\n");
    }

    [TestMethod]
    public async Task ReadAllTextAsync_Bom_IsHonouredAndStripped_BomOnlyIsEmpty()
    {
        using var withBom = new MemoryStream([0xEF, 0xBB, 0xBF, (byte)'h', (byte)'i']);
        using var bomOnly = new MemoryStream([0xEF, 0xBB, 0xBF]);

        (await withBom.ReadAllTextAsync(2, leaveOpen: false, CancellationToken)).Should().Be("hi");
        (await bomOnly.ReadAllTextAsync(0, leaveOpen: false, CancellationToken)).Should().BeEmpty();
    }

    [TestMethod]
    public async Task ReadAllTextAsync_LeaveOpen_DecidesWhetherTheStreamIsDisposed()
    {
        var kept = Utf8("x");
        var closed = Utf8("x");

        await kept.ReadAllTextAsync(1, leaveOpen: true, CancellationToken);
        await closed.ReadAllTextAsync(1, leaveOpen: false, CancellationToken);

        kept.CanRead.Should().BeTrue();
        closed.CanRead.Should().BeFalse();
        await kept.DisposeAsync();
    }

    [TestMethod]
    public async Task ReadAllTextAsync_Cancelled_Throws()
    {
        using var stream = Utf8("abc");
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        Func<Task> read = () => stream.ReadAllTextAsync(10, leaveOpen: false, cancelled.Token);

        await read.Should().ThrowAsync<OperationCanceledException>();
    }

    [TestMethod]
    public async Task ReadAllTextAsync_InvalidArguments_Throw()
    {
        using var stream = Utf8("abc");
        Stream none = (new Stream[1])[0];

        Func<Task> noStream = () => none.ReadAllTextAsync(1, leaveOpen: false, CancellationToken);
        Func<Task> negative = () => stream.ReadAllTextAsync(-1, leaveOpen: false, CancellationToken);

        await noStream.Should().ThrowAsync<ArgumentNullException>().WithParameterName("stream");
        await negative.Should().ThrowAsync<ArgumentOutOfRangeException>().WithParameterName("maxCharacters");
    }

    private static MemoryStream Utf8(string text) => new(Encoding.UTF8.GetBytes(text));

    // a stream that hands out at most `chunk` bytes per read, so a CR and its LF arrive apart
    private sealed class ChunkedStream(byte[] bytes, int chunk) : MemoryStream(bytes)
    {
        public override int Read(byte[] buffer, int offset, int count) => base.Read(buffer, offset, Math.Min(count, chunk));

        public override int Read(Span<byte> buffer) => base.Read(buffer[..Math.Min(buffer.Length, chunk)]);

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
            => base.ReadAsync(buffer[..Math.Min(buffer.Length, chunk)], cancellationToken);

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
            => base.ReadAsync(buffer, offset, Math.Min(count, chunk), cancellationToken);
    }

    private sealed class CountingStream(byte[] bytes) : MemoryStream(bytes)
    {
        public long BytesRead { get; private set; }

        public override int Read(byte[] buffer, int offset, int count) => Count(base.Read(buffer, offset, count));

        public override int Read(Span<byte> buffer) => Count(base.Read(buffer));

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
            => Count(await base.ReadAsync(buffer, cancellationToken));

        public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
            => Count(await base.ReadAsync(buffer, offset, count, cancellationToken));

        private int Count(int read)
        {
            BytesRead += read;
            return read;
        }
    }
}
