using AwesomeAssertions;
using Nordstein.Core.Testing.Postgres.Tests.Internal;
using Npgsql;

namespace Nordstein.Core.Testing.Postgres.Tests;

[TestClass]
[TestCategory("Integration")]
public sealed class PostgresServerTests
{
    public required TestContext TestContext { get; init; }

    private CancellationToken CancellationToken => TestContext.CancellationTokenSource.Token;

    [TestMethod]
    public async Task CreateDatabase_TwoTests_DoNotSeeEachOthersTables()
    {
        await using PostgresDatabase first = await SharedPostgres.Server.CreateDatabaseAsync(CancellationToken);
        await using PostgresDatabase second = await SharedPostgres.Server.CreateDatabaseAsync(CancellationToken);

        await ScalarAsync(first.ConnectionString, "CREATE TABLE only_here (id integer); SELECT 1");

        first.Name.Should().StartWith("test_").And.NotBe(second.Name);
        (await ScalarAsync(first.ConnectionString, "SELECT to_regclass('only_here') IS NOT NULL")).Should().Be(true);
        (await ScalarAsync(second.ConnectionString, "SELECT to_regclass('only_here') IS NOT NULL")).Should().Be(false);
    }

    [TestMethod]
    public async Task CreateDatabase_ConnectionString_IsLoopbackAndNamesTheDatabase()
    {
        await using PostgresDatabase database = await SharedPostgres.Server.CreateDatabaseAsync(CancellationToken);
        var parsed = new NpgsqlConnectionStringBuilder(database.ConnectionString);

        parsed.Host.Should().Be("127.0.0.1");
        parsed.Database.Should().Be(database.Name);
        (await ScalarAsync(database.ConnectionString, "SELECT current_setting('max_connections')")).Should().Be("200");
    }

    [TestMethod]
    public async Task Dispose_WithOpenPooledSession_DropsTheDatabaseOnce()
    {
        await using PostgresDatabase probe = await SharedPostgres.Server.CreateDatabaseAsync(CancellationToken);
        PostgresDatabase database = await SharedPostgres.Server.CreateDatabaseAsync(CancellationToken);
        await ScalarAsync(database.ConnectionString, "SELECT 1");

        await database.DisposeAsync();
        await database.DisposeAsync();

        (await ScalarAsync(probe.ConnectionString, $"SELECT count(*) FROM pg_database WHERE datname = '{database.Name}'")).Should().Be(0L);
    }

    [TestMethod]
    public async Task CreateDatabase_FromTemplate_ClonesItsSchemaAndBuildsItOnce()
    {
        int builds = 0;
        var template = new PostgresTemplate(
            "tpl_clone_once",
            async (connectionString, cancellationToken) =>
            {
                Interlocked.Increment(ref builds);
                await using var connection = new NpgsqlConnection(connectionString);
                await connection.OpenAsync(cancellationToken);
                await using var command = new NpgsqlCommand("CREATE TABLE from_template (id integer PRIMARY KEY)", connection);
                await command.ExecuteNonQueryAsync(cancellationToken);
            });

        PostgresDatabase[] clones = await Task.WhenAll(
            Enumerable.Range(0, 3).Select(_ => SharedPostgres.Server.CreateDatabaseAsync(template, CancellationToken)));

        try
        {
            builds.Should().Be(1);
            foreach (PostgresDatabase clone in clones)
            {
                (await ScalarAsync(clone.ConnectionString, "SELECT to_regclass('from_template') IS NOT NULL")).Should().Be(true);
            }

            await ScalarAsync(clones[0].ConnectionString, "INSERT INTO from_template VALUES (1); SELECT 1");
            (await ScalarAsync(clones[1].ConnectionString, "SELECT count(*) FROM from_template")).Should().Be(0L);
        }
        finally
        {
            foreach (PostgresDatabase clone in clones)
            {
                await clone.DisposeAsync();
            }
        }
    }

    [TestMethod]
    public async Task CreateDatabase_FromTemplate_TemplateRefusesConnections()
    {
        var template = new PostgresTemplate("tpl_closed", (_, _) => Task.CompletedTask);
        await using PostgresDatabase clone = await SharedPostgres.Server.CreateDatabaseAsync(template, CancellationToken);
        string templateConnection = new NpgsqlConnectionStringBuilder(clone.ConnectionString) { Database = "tpl_closed", Pooling = false }.ConnectionString;

        Func<Task> act = () => ScalarAsync(templateConnection, "SELECT 1");

        (await act.Should().ThrowAsync<PostgresException>()).Which.SqlState.Should().Be(PostgresErrorCodes.ObjectNotInPrerequisiteState);
    }

    [TestMethod]
    public async Task CreateDatabase_TemplateBuildFails_ThrowsForEveryCaller()
    {
        var template = new PostgresTemplate("tpl_broken", (_, _) => throw new InvalidOperationException("planted"));

        Func<Task> first = () => SharedPostgres.Server.CreateDatabaseAsync(template, CancellationToken);
        Func<Task> second = () => SharedPostgres.Server.CreateDatabaseAsync(template, CancellationToken);

        (await first.Should().ThrowAsync<InvalidOperationException>().WithMessage("*tpl_broken*")).Which.InnerException.Should().BeOfType<InvalidOperationException>();
        await second.Should().ThrowAsync<InvalidOperationException>().WithMessage("*tpl_broken*");
    }

    [TestMethod]
    public async Task CreateDatabase_NullTemplate_Throws()
    {
        Func<Task> act = () => SharedPostgres.Server.CreateDatabaseAsync(Missing.Reference<PostgresTemplate>(), CancellationToken);

        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [TestMethod]
    public async Task CreateDatabase_CallerCancelled_ThrowsCancellation()
    {
        await using PostgresDatabase warm = await SharedPostgres.Server.CreateDatabaseAsync(CancellationToken);
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        Func<Task> act = () => SharedPostgres.Server.CreateDatabaseAsync(cancelled.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [TestMethod]
    public async Task ServerStart_FirstCallerCancelled_LaterCallersStillGetTheServer()
    {
        await using var server = new PostgresServer(SharedPostgres.Options);
        using var cancellation = new CancellationTokenSource();

        Task<PostgresDatabase> first = server.CreateDatabaseAsync(cancellation.Token);
        await cancellation.CancelAsync();

        await FluentActions.Awaiting(() => first).Should().ThrowAsync<OperationCanceledException>();
        await using PostgresDatabase database = await server.CreateDatabaseAsync(CancellationToken);
        (await ScalarAsync(database.ConnectionString, "SELECT 1")).Should().Be(1);
    }

    [TestMethod]
    public async Task ServerStart_NoRuntimeAndNotRequired_EndsInconclusive()
    {
        await using PostgresServer server = Unreachable(_ => null);

        Func<Task> act = () => server.CreateDatabaseAsync(CancellationToken);

        (await act.Should().ThrowAsync<AssertInconclusiveException>()).Which.Message.Should().Contain("EXAMPLE_REQUIRE_DOCKER_TESTS=1");
    }

    [TestMethod]
    [DataRow("")]
    [DataRow("0")]
    public async Task ServerStart_NoRuntimeAndVariableOff_EndsInconclusive(string value)
    {
        await using PostgresServer server = Unreachable(_ => value);

        await FluentActions.Awaiting(() => server.CreateDatabaseAsync(CancellationToken)).Should().ThrowAsync<AssertInconclusiveException>();
    }

    [TestMethod]
    public async Task ServerStart_NoRuntimeButRequired_FailsEveryCaller()
    {
        await using PostgresServer server = Unreachable(_ => "1");
        var template = new PostgresTemplate("tpl_unreachable", (_, _) => Task.CompletedTask);

        Func<Task> first = () => server.CreateDatabaseAsync(CancellationToken);
        Func<Task> second = () => server.CreateDatabaseAsync(template, CancellationToken);

        await first.Should().ThrowAsync<InvalidOperationException>().WithMessage("The shared PostgreSQL server could not be started.");
        await second.Should().ThrowAsync<InvalidOperationException>().WithMessage("The shared PostgreSQL server could not be started.");
    }

    [TestMethod]
    public async Task ServerStart_NoRuntimeAndVariableMisspelled_FailsLoudly()
    {
        await using PostgresServer server = Unreachable(_ => "true");

        Func<Task> act = () => server.CreateDatabaseAsync(CancellationToken);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("EXAMPLE_REQUIRE_DOCKER_TESTS is 'true'*");
    }

    [TestMethod]
    public async Task Dispose_NeverStarted_DoesNothing()
    {
        var server = new PostgresServer(SharedPostgres.Options);

        await server.DisposeAsync();
    }

    [TestMethod]
    public async Task Dispose_StartedWithTemplate_StopsTheServer()
    {
        var server = new PostgresServer(SharedPostgres.Options);
        var template = new PostgresTemplate("tpl_dispose", (_, _) => Task.CompletedTask);
        PostgresDatabase database = await server.CreateDatabaseAsync(template, CancellationToken);
        string connectionString = new NpgsqlConnectionStringBuilder(database.ConnectionString) { Pooling = false, Timeout = 3 }.ConnectionString;

        await server.DisposeAsync();

        await FluentActions.Awaiting(() => ScalarAsync(connectionString, "SELECT 1")).Should().ThrowAsync<NpgsqlException>();
    }

    [TestMethod]
    public void Constructor_NullOrInvalidOptions_Throws()
    {
        Action missing = () => _ = new PostgresServer(Missing.Reference<PostgresServerOptions>());
        Action reader = () => _ = new PostgresServer(SharedPostgres.Options, null, Missing.Reference<Func<string, string?>>());
        Action invalid = () => _ = new PostgresServer(SharedPostgres.Options with { Image = " " });

        missing.Should().Throw<ArgumentNullException>();
        reader.Should().Throw<ArgumentNullException>();
        invalid.Should().Throw<InvalidOperationException>();
    }

    private static PostgresServer Unreachable(Func<string, string?> readVariable)
        => new(
            new PostgresServerOptions
            {
                Image = SharedPostgres.Image,
                RequireVariable = "EXAMPLE_REQUIRE_DOCKER_TESTS",
                StartTimeout = TimeSpan.FromSeconds(20),
            },
            new Uri("tcp://127.0.0.1:1"),
            readVariable);

    private async Task<object?> ScalarAsync(string connectionString, string sql)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(CancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        return await command.ExecuteScalarAsync(CancellationToken);
    }
}
