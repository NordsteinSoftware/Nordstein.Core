# Nordstein.Core.Testing.Postgres

A PostgreSQL test harness: one throwaway container per test process, one database per test.

```csharp
internal static class SharedPostgres
{
    private static readonly PostgresServer Server = new(new PostgresServerOptions
    {
        Image = "postgres:17.2",
        RequireVariable = "EXAMPLE_REQUIRE_DOCKER_TESTS",
    });

    public static Task<PostgresDatabase> CreateDatabaseAsync(CancellationToken cancellationToken)
        => Server.CreateDatabaseAsync(cancellationToken);
}

[TestMethod]
public async Task Query_OnRealServer_Answers()
{
    await using PostgresDatabase database = await SharedPostgres.CreateDatabaseAsync(CancellationToken);
    // database.ConnectionString belongs to this test alone
}
```

- No container runtime: the test is inconclusive (skipped). With the require variable set to `1`
  it fails instead; CI sets it.
- `PostgresTemplate` clones each test database from a schema-only template, built once.
- Opt-in: no other Core package references this one.

See [`docs/testing.md`](../docs/testing.md#the-postgresql-harness-nordsteincoretestingpostgres).
