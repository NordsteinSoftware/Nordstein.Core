namespace Nordstein.Core.Testing.Postgres;

/// <summary>
/// One test's database on a <see cref="PostgresServer"/>. Disposing it drops the database, also
/// after a failed or inconclusive test when it is held in an <c>await using</c>.
/// </summary>
public sealed class PostgresDatabase : IAsyncDisposable
{
    private readonly Func<string, Task> drop;
    private int disposed;

    internal PostgresDatabase(string name, string connectionString, Func<string, Task> drop)
    {
        Name = name;
        ConnectionString = connectionString;
        this.drop = drop;
    }

    /// <summary>
    /// The database's generated name, <c>test_</c> plus 32 hex digits.
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// The connection string of this database in key-value form
    /// (<c>Host=…;Port=…;Database=…;Username=…;Password=…</c>), on the loopback interface.
    /// </summary>
    public string ConnectionString { get; }

    /// <summary>
    /// Drops the database, once, forcing off any session still open. Never cancelled, so a
    /// cancelled test still cleans up. Errors propagate.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref disposed, 1) == 1)
        {
            return;
        }

        await drop(Name).ConfigureAwait(false);
    }
}
