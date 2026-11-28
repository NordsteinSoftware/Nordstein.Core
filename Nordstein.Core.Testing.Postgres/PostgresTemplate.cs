namespace Nordstein.Core.Testing.Postgres;

/// <summary>
/// A schema-only database that test databases are cloned from, so a test does not pay for
/// migrations. A <see cref="PostgresServer"/> creates it once, on first use, and then closes it to
/// connections.
/// </summary>
/// <remarks>
/// The template holds schema and its bookkeeping only, never test data: every clone starts from
/// the same state. Immutable; safe to keep in a static field.
/// </remarks>
public sealed class PostgresTemplate
{
    /// <summary>
    /// Creates a template description.
    /// </summary>
    /// <param name="name">
    /// The template database's name: a lower-case letter, then up to 30 lower-case letters, digits
    /// or underscores.
    /// </param>
    /// <param name="initialize">
    /// Builds the schema, given the template's connection string (typically: apply the product's
    /// migrations). Runs once per server, on the server's start timeout rather than a test's token.
    /// Sessions it leaves open are closed for it.
    /// </param>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="name"/> does not have the form above.</exception>
    public PostgresTemplate(string name, Func<string, CancellationToken, Task> initialize)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(initialize);
        if (name.Length is 0 or > 31
            || name[0] is < 'a' or > 'z'
            || !name.All(character => character is (>= 'a' and <= 'z') or (>= '0' and <= '9') or '_'))
        {
            throw new ArgumentException(
                $"'{name}' is not a template name: a lower-case letter, then up to 30 lower-case letters, digits or underscores.",
                nameof(name));
        }

        Name = name;
        Initialize = initialize;
    }

    /// <summary>
    /// The template database's name.
    /// </summary>
    public string Name { get; }

    internal Func<string, CancellationToken, Task> Initialize { get; }
}
