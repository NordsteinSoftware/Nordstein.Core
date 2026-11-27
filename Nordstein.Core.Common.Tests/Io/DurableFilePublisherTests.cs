using Autofac;
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Nordstein.Core.Common.Io;
using Nordstein.Core.Common.Lifecycle;
using Nordstein.Core.Testing;

namespace Nordstein.Core.Common.Tests.Io;

[TestClass]
public sealed class DurableFilePublisherTests : BaseTest<Module>
{
    private IDurableFilePublisher Publisher => GetServices().GetRequiredService<IDurableFilePublisher>();

    private ITempDirectory NewTempDirectory()
        => GetServices().GetRequiredService<ITempDirectory.Create>()(null, "durable-publish-test");

    [TestMethod]
    public async Task Publish_WritesContentDurablyToFreshPath()
    {
        using ITempDirectory temp = NewTempDirectory();
        string destination = temp.Combine("payload.blob");
        byte[] content = "the durable payload"u8.ToArray();

        await using (IFileWriteHandle handle = Publisher.BeginWrite(temp.Path))
        {
            await handle.Content.WriteAsync(content);
            await handle.PublishAsync(destination);
        }

        File.Exists(destination).Should().BeTrue();
        (await File.ReadAllBytesAsync(destination)).Should().Equal(content);
        NoStagingFilesRemain(temp.Path);
    }

    [TestMethod]
    public async Task Publish_WhenDestinationExists_ThrowsAndLeavesNoStagingFile()
    {
        using ITempDirectory temp = NewTempDirectory();
        string destination = temp.Combine("payload.blob");
        await File.WriteAllTextAsync(destination, "original");

        Func<Task> act = async () =>
        {
            await using IFileWriteHandle handle = Publisher.BeginWrite(temp.Path);
            await handle.Content.WriteAsync("replacement"u8.ToArray());
            await handle.PublishAsync(destination);
        };

        await act.Should().ThrowAsync<DestinationAlreadyExistsException>();
        (await File.ReadAllTextAsync(destination)).Should().Be("original");
        NoStagingFilesRemain(temp.Path);
    }

    [TestMethod]
    public async Task PublishReplacing_OverExistingDestination_ReplacesIt()
    {
        using ITempDirectory temp = NewTempDirectory();
        string destination = temp.Combine("payload.blob");
        await File.WriteAllTextAsync(destination, "original");

        await using (IFileWriteHandle handle = Publisher.BeginWrite(temp.Path))
        {
            await handle.Content.WriteAsync("replacement"u8.ToArray());
            await handle.PublishReplacingAsync(destination);
        }

        (await File.ReadAllTextAsync(destination)).Should().Be("replacement");
        NoStagingFilesRemain(temp.Path);
    }

    [TestMethod]
    public async Task Dispose_WithoutPublish_RemovesStagingAndCreatesNoDestination()
    {
        using ITempDirectory temp = NewTempDirectory();
        string destination = temp.Combine("payload.blob");

        await using (IFileWriteHandle handle = Publisher.BeginWrite(temp.Path))
        {
            await handle.Content.WriteAsync("abandoned"u8.ToArray());
            // No publish: the write is aborted on dispose.
        }

        File.Exists(destination).Should().BeFalse();
        NoStagingFilesRemain(temp.Path);
    }

    [TestMethod]
    public async Task Publish_CalledTwice_ThrowsInvalidOperation()
    {
        using ITempDirectory temp = NewTempDirectory();

        await using IFileWriteHandle handle = Publisher.BeginWrite(temp.Path);
        await handle.Content.WriteAsync("payload"u8.ToArray());
        await handle.PublishAsync(temp.Combine("first.blob"));

        Func<Task> act = () => handle.PublishAsync(temp.Combine("second.blob"));

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [TestMethod]
    public void BeginWrite_InMissingDirectory_ThrowsStagingUnavailable()
    {
        using ITempDirectory temp = NewTempDirectory();
        string missing = Path.Combine(temp.Path, "no-such-directory");

        Action act = () => Publisher.BeginWrite(missing);

        act.Should().Throw<StagingUnavailableException>();
    }

    [TestMethod]
    public async Task Publish_WithEmptyContent_ProducesAnEmptyFile()
    {
        using ITempDirectory temp = NewTempDirectory();
        string destination = temp.Combine("empty.blob");

        await using (IFileWriteHandle handle = Publisher.BeginWrite(temp.Path))
        {
            await handle.PublishAsync(destination);
        }

        File.Exists(destination).Should().BeTrue();
        (await File.ReadAllBytesAsync(destination)).Should().BeEmpty();
        NoStagingFilesRemain(temp.Path);
    }

    [TestMethod]
    public async Task Publish_FlushesTheDestinationDirectoryAfterTheMove()
    {
        using ITempDirectory temp = NewTempDirectory();
        string destination = temp.Combine("payload.blob");
        RecordingDirectorySync sync = new(destination, expectedContent: "payload");

        await using (IFileWriteHandle handle = PublisherWith(sync).BeginWrite(temp.Path))
        {
            await handle.Content.WriteAsync("payload"u8.ToArray());
            await handle.PublishAsync(destination);
        }

        sync.Flushes.Should().Equal((temp.Path, true));
    }

    [TestMethod]
    public async Task PublishReplacing_FlushesTheDestinationDirectoryAfterTheMove()
    {
        using ITempDirectory temp = NewTempDirectory();
        string destination = temp.Combine("payload.blob");
        await File.WriteAllTextAsync(destination, "original");
        RecordingDirectorySync sync = new(destination, expectedContent: "replacement");

        await using (IFileWriteHandle handle = PublisherWith(sync).BeginWrite(temp.Path))
        {
            await handle.Content.WriteAsync("replacement"u8.ToArray());
            await handle.PublishReplacingAsync(destination);
        }

        sync.Flushes.Should().Equal((temp.Path, true));
    }

    [TestMethod]
    public async Task Publish_WithRelativeDestination_FlushesItsFullDirectory()
    {
        using ITempDirectory temp = NewTempDirectory();
        string destination = Path.GetRelativePath(Environment.CurrentDirectory, temp.Combine("payload.blob"));
        RecordingDirectorySync sync = new(destination);

        await using (IFileWriteHandle handle = PublisherWith(sync).BeginWrite(temp.Path))
        {
            await handle.PublishAsync(destination);
        }

        sync.Flushes.Should().Equal((Path.GetFullPath(temp.Path), true));
    }

    [TestMethod]
    public async Task Publish_WhenDestinationExists_DoesNotFlush()
    {
        using ITempDirectory temp = NewTempDirectory();
        string destination = temp.Combine("payload.blob");
        await File.WriteAllTextAsync(destination, "original");
        RecordingDirectorySync sync = new(destination);

        Func<Task> act = async () =>
        {
            await using IFileWriteHandle handle = PublisherWith(sync).BeginWrite(temp.Path);
            await handle.PublishAsync(destination);
        };

        await act.Should().ThrowAsync<DestinationAlreadyExistsException>();
        sync.Flushes.Should().BeEmpty();
    }

    [TestMethod]
    public async Task Publish_WhenTheDirectoryFlushFails_ThrowsAndKeepsThePublishedFile()
    {
        using ITempDirectory temp = NewTempDirectory();
        string destination = temp.Combine("payload.blob");
        RecordingDirectorySync sync = new(destination) { Failure = new IOException("fsync failed") };
        IFileWriteHandle handle = PublisherWith(sync).BeginWrite(temp.Path);

        Func<Task> act = () => handle.PublishAsync(destination);

        await act.Should().ThrowAsync<IOException>().WithMessage("fsync failed");
        await handle.DisposeAsync();
        File.Exists(destination).Should().BeTrue();
        NoStagingFilesRemain(temp.Path);
    }

    [TestMethod]
    public async Task PublishReplacing_ToAFilesystemRoot_ThrowsArgumentAndNeitherMovesNorFlushes()
    {
        using ITempDirectory temp = NewTempDirectory();
        string? root = Path.GetPathRoot(temp.Path);
        Assert.IsNotNull(root);
        RecordingDirectorySync sync = new(temp.Combine("unused"));

        Func<Task> act = async () =>
        {
            await using IFileWriteHandle handle = PublisherWith(sync).BeginWrite(temp.Path);
            await handle.PublishReplacingAsync(root);
        };

        (await act.Should().ThrowAsync<ArgumentException>()).Which.ParamName.Should().Be("destinationPath");
        sync.Flushes.Should().BeEmpty();
        NoStagingFilesRemain(temp.Path);
    }

    private IDurableFilePublisher PublisherWith(IDirectorySync sync)
        => GetServices(builder => builder.RegisterInstance(sync).As<IDirectorySync>())
            .GetRequiredService<IDurableFilePublisher>();

    /// <summary>
    /// Records each flushed directory and whether the published file was already in place
    /// (with its new content) at that moment, which proves the flush ran after the move.
    /// </summary>
    private sealed class RecordingDirectorySync(string destination, string expectedContent = "") : IDirectorySync
    {
        public List<(string Directory, bool PublishedBeforeFlush)> Flushes { get; } = [];

        public IOException? Failure { get; init; }

        public DirectorySyncResult Flush(string directory)
        {
            bool published = File.Exists(destination) && File.ReadAllText(destination) == expectedContent;
            Flushes.Add((directory, published));
            return Failure is null ? DirectorySyncResult.Flushed : throw Failure;
        }
    }

    private static void NoStagingFilesRemain(string directory)
        => Directory.EnumerateFiles(directory)
            .Where(file => file.EndsWith(".tmp", StringComparison.Ordinal))
            .Should().BeEmpty();
}
