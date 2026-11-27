namespace Nordstein.Core.Storage.Tests;

/// <summary>
/// The test's view into <see cref="TestDocRepository"/>'s archive hooks: <see cref="WhileLocking"/>
/// runs once inside the lock hook, standing in for a writer the lock waited for, and
/// <see cref="RelationsArchived"/> counts the relation clean-ups that ran.
/// </summary>
internal sealed class ArchiveLockProbe
{
    private int relationsArchived;

    /// <summary>Runs on the next lock, then clears itself, so a nested archive does not run it again.</summary>
    public Func<Task>? WhileLocking { get; set; }

    /// <summary>How many archives reached <c>ArchiveRelationsAsync</c>.</summary>
    public int RelationsArchived => relationsArchived;

    internal async Task LockAsync()
    {
        Func<Task>? whileLocking = WhileLocking;
        WhileLocking = null;
        if (whileLocking is not null)
        {
            await whileLocking();
        }
    }

    internal void RelationsRan() => Interlocked.Increment(ref relationsArchived);
}
