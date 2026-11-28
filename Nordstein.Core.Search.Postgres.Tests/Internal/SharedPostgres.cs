using Nordstein.Core.Testing.Postgres;

namespace Nordstein.Core.Search.Postgres.Tests.Internal;

/// <summary>
/// The one PostgreSQL server of this test process (docs/testing.md, "Container-backed tests": the
/// opt-in exception). It holds no test data: every test creates and drops a database of its own.
/// </summary>
internal static class SharedPostgres
{
    /// <summary>
    /// PostgreSQL 17 with pgvector 0.8.2, pinned by digest.
    /// </summary>
    public const string Image =
        "pgvector/pgvector:0.8.2-pg17@sha256:feb68f4f15446397d8cac7f4fe48fe4586de83160d1fc48b46283312d1a33966";

    /// <summary>
    /// CI sets this to 1, so a missing container runtime fails the suite instead of skipping it.
    /// </summary>
    public const string RequireVariable = "NORDSTEIN_CORE_REQUIRE_DOCKER_TESTS";

    private static readonly PostgresServer Server = new(new PostgresServerOptions
    {
        Image = Image,
        RequireVariable = RequireVariable,
    });

    public static Task<PostgresDatabase> CreateDatabaseAsync(CancellationToken cancellationToken)
        => Server.CreateDatabaseAsync(cancellationToken);
}
