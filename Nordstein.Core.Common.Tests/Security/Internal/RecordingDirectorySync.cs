using Nordstein.Core.Common.Io;

namespace Nordstein.Core.Common.Tests.Security.Internal;

/// <summary>
/// An <see cref="IDirectorySync"/> that records each flushed directory and the entries it held at that moment,
/// and can fail on a chosen flush. One instance per test.
/// </summary>
internal sealed class RecordingDirectorySync : IDirectorySync
{
    private readonly List<(string Directory, string[] Entries)> flushes = [];

    /// <summary>
    /// The exception the flush numbered <see cref="FailAt"/> throws, or <see langword="null"/> for none.
    /// </summary>
    public Exception? Failure { get; init; }

    /// <summary>
    /// The zero-based number of the flush that throws <see cref="Failure"/>.
    /// </summary>
    public int FailAt { get; init; }

    /// <summary>
    /// The flushes so far, oldest first.
    /// </summary>
    public IReadOnlyList<(string Directory, string[] Entries)> Flushes => flushes;

    public DirectorySyncResult Flush(string directory)
    {
        flushes.Add((directory, [.. Directory.EnumerateFileSystemEntries(directory).Select(Path.GetFileName).OfType<string>().Order(StringComparer.Ordinal)]));
        if (Failure is not null && flushes.Count - 1 == FailAt)
        {
            throw Failure;
        }

        return DirectorySyncResult.Flushed;
    }
}
