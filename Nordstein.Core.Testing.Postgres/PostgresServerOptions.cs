namespace Nordstein.Core.Testing.Postgres;

/// <summary>
/// How a <see cref="PostgresServer"/> starts.
/// </summary>
public sealed record PostgresServerOptions
{
    /// <summary>
    /// The container image, pinned to the tag (and ideally the digest) the deployed stack runs,
    /// for example <c>postgres:17.2</c>.
    /// </summary>
    public required string Image { get; init; }

    /// <summary>
    /// The environment variable that turns a missing container runtime from a skipped test into a
    /// failed one when it is <c>1</c>, such as <c>EXAMPLE_REQUIRE_DOCKER_TESTS</c>. CI sets it, so
    /// the container tests can never be lost silently.
    /// </summary>
    public required string RequireVariable { get; init; }

    /// <summary>
    /// The server's <c>max_connections</c>. Parallel tests share the server; the default is 200.
    /// </summary>
    public int MaxConnections { get; init; } = 200;

    /// <summary>
    /// How long starting the container, or initializing a template, may take. The default is five
    /// minutes, which covers a first image pull.
    /// </summary>
    public TimeSpan StartTimeout { get; init; } = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Throws when a value is unusable.
    /// </summary>
    /// <exception cref="InvalidOperationException">A value is blank, zero or negative.</exception>
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Image))
        {
            throw new InvalidOperationException($"{nameof(Image)} must name a container image.");
        }

        if (string.IsNullOrWhiteSpace(RequireVariable))
        {
            throw new InvalidOperationException($"{nameof(RequireVariable)} must name an environment variable.");
        }

        if (MaxConnections <= 0)
        {
            throw new InvalidOperationException($"{nameof(MaxConnections)} must be positive.");
        }

        if (StartTimeout <= TimeSpan.Zero)
        {
            throw new InvalidOperationException($"{nameof(StartTimeout)} must be positive.");
        }
    }
}
