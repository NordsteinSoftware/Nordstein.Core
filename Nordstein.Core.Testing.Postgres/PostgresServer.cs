using System.Collections.Concurrent;
using System.Data.Common;
using System.Globalization;
using System.Security.Cryptography;
using DotNet.Testcontainers.Containers;
using Nordstein.Core.Testing.Postgres.Internal;
using Testcontainers.PostgreSql;

namespace Nordstein.Core.Testing.Postgres;

/// <summary>
/// One PostgreSQL container, started lazily on first use, that hands every test a database of its
/// own. A test project keeps one instance for its process (the opt-in exception of
/// <c>docs/testing.md</c>, "Container-backed tests") and never exposes the container.
/// </summary>
/// <remarks>
/// <para>
/// Startup runs once, on <see cref="PostgresServerOptions.StartTimeout"/>, never on a caller's
/// token: a cancelled first caller never fails a later one. A failed start is cached and reaches
/// every caller: as an inconclusive (skipped) test, or as a failure when the options'
/// <see cref="PostgresServerOptions.RequireVariable"/> is <c>1</c>.
/// </para>
/// <para>
/// The published port binds to <c>127.0.0.1</c> only and the password is random per start, so no
/// other host can reach the server and no fixed credential opens it. Statements on whole databases
/// run through <c>psql</c> inside the container. Safe for concurrent callers.
/// </para>
/// </remarks>
public sealed class PostgresServer : IAsyncDisposable
{
    private const string Superuser = "postgres";

    private readonly PostgresServerOptions options;
    private readonly Uri? dockerEndpoint;
    private readonly Func<string, string?> readVariable;
    private readonly Lazy<Task<PostgreSqlContainer>> container;
    private readonly ConcurrentDictionary<string, Lazy<Task>> templates = new(StringComparer.Ordinal);

    /// <summary>
    /// Creates a server description. Nothing starts until the first database is asked for.
    /// </summary>
    /// <param name="options">How the server starts.</param>
    /// <exception cref="ArgumentNullException"><paramref name="options"/> is null.</exception>
    /// <exception cref="InvalidOperationException">An option is unusable.</exception>
    public PostgresServer(PostgresServerOptions options)
        : this(options, null, Environment.GetEnvironmentVariable)
    {
    }

    internal PostgresServer(PostgresServerOptions options, Uri? dockerEndpoint, Func<string, string?> readVariable)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(readVariable);
        options.Validate();
        this.options = options;
        this.dockerEndpoint = dockerEndpoint;
        this.readVariable = readVariable;

        // Task.Run: the first caller's thread runs none of the startup, so its cancellation or a
        // synchronous Build() failure cannot leak into the shared task.
        container = new Lazy<Task<PostgreSqlContainer>>(
            () => Task.Run(StartContainerAsync), LazyThreadSafetyMode.ExecutionAndPublication);
    }

    /// <summary>
    /// Creates an empty database for one test.
    /// </summary>
    /// <param name="cancellationToken">Cancels the caller's wait, never the shared startup.</param>
    /// <returns>The database; dispose it to drop it.</returns>
    /// <exception cref="AssertInconclusiveException">No container runtime is usable and it is not required.</exception>
    /// <exception cref="InvalidOperationException">The server could not start and the runtime is required.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.</exception>
    public async Task<PostgresDatabase> CreateDatabaseAsync(CancellationToken cancellationToken)
    {
        PostgreSqlContainer server = await ServerAsync(cancellationToken).ConfigureAwait(false);
        return await CreateDatabaseAsync(server, "template0", cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Creates a database for one test as a clone of <paramref name="template"/>, which is built on
    /// first use.
    /// </summary>
    /// <param name="template">The template to clone.</param>
    /// <param name="cancellationToken">Cancels the caller's wait, never the shared startup.</param>
    /// <returns>The database; dispose it to drop it.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="template"/> is null.</exception>
    /// <exception cref="AssertInconclusiveException">No container runtime is usable and it is not required.</exception>
    /// <exception cref="InvalidOperationException">
    /// The server could not start and the runtime is required, or the template could not be built.
    /// </exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.</exception>
    public async Task<PostgresDatabase> CreateDatabaseAsync(PostgresTemplate template, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(template);
        PostgreSqlContainer server = await ServerAsync(cancellationToken).ConfigureAwait(false);
        Lazy<Task> build = templates.GetOrAdd(
            template.Name,
            _ => new Lazy<Task>(() => Task.Run(() => BuildTemplateAsync(server, template)), LazyThreadSafetyMode.ExecutionAndPublication));
        await build.Value.WaitAsync(cancellationToken).ConfigureAwait(false);
        return await CreateDatabaseAsync(server, template.Name, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Stops and removes the container once its start has finished; a failed start left nothing to
    /// dispose.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        if (!container.IsValueCreated)
        {
            return;
        }

        foreach (Lazy<Task> build in templates.Values.Where(build => build.IsValueCreated))
        {
            await build.Value.ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
        }

        Task<PostgreSqlContainer> start = container.Value;
        await ((Task)start).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
        if (start.IsCompletedSuccessfully)
        {
            await (await start.ConfigureAwait(false)).DisposeAsync().ConfigureAwait(false);
        }
    }

    /// <summary>
    /// The started container, through the skip policy: the caller's own cancellation propagates;
    /// any other start failure ends the test inconclusive, unless the runtime is required.
    /// </summary>
    private async Task<PostgreSqlContainer> ServerAsync(CancellationToken cancellationToken)
    {
        Task<PostgreSqlContainer> start = container.Value;

        try
        {
            return await start.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            if (exception is OperationCanceledException && cancellationToken.IsCancellationRequested)
            {
                throw;
            }

            // Never an exception filter: the CLR swallows an exception thrown inside a filter, so
            // the inconclusive result would vanish. The catch is unnarrowed on purpose: neither
            // Testcontainers nor Docker.DotNet promises one exception type for an unusable runtime.
            if (!IsRuntimeRequired())
            {
                Assert.Inconclusive(
                    $"No usable container runtime, so this test is skipped. {exception.Message} "
                    + $"{exception.InnerException?.Message} (set {options.RequireVariable}=1 to make this a failure.)");
            }

            throw;
        }
    }

    /// <summary>
    /// Whether the require variable is exactly <c>1</c>. Unset, empty and <c>0</c> mean no; any
    /// other spelling throws, so a run that meant to require the runtime never skips silently.
    /// </summary>
    private bool IsRuntimeRequired()
    {
        string? value = readVariable(options.RequireVariable);
        return value switch
        {
            "1" => true,
            null or "" or "0" => false,
            _ => throw new InvalidOperationException(
                $"{options.RequireVariable} is '{value}'; 1 requires the container runtime, unset, empty or 0 skips without it."),
        };
    }

    private async Task<PostgreSqlContainer> StartContainerAsync()
    {
        using var timeout = new CancellationTokenSource(options.StartTimeout);

        try
        {
            PostgreSqlBuilder builder = new PostgreSqlBuilder(options.Image)
                .WithUsername(Superuser)
                .WithDatabase(Superuser)
                .WithPassword(Convert.ToHexString(RandomNumberGenerator.GetBytes(24)))
                .WithCommand("-c", string.Create(CultureInfo.InvariantCulture, $"max_connections={options.MaxConnections}"))
                .WithCreateParameterModifier(LoopbackPorts.Bind);
            if (dockerEndpoint is not null)
            {
                builder = builder.WithDockerEndpoint(dockerEndpoint);
            }

            // Testcontainers pings the Docker endpoint while building, so a missing runtime throws
            // here: Build() is inside the try on purpose.
            PostgreSqlContainer server = builder.Build();
            await server.StartAsync(timeout.Token).ConfigureAwait(false);
            return server;
        }
        catch (Exception exception)
        {
            throw new InvalidOperationException("The shared PostgreSQL server could not be started.", exception);
        }
    }

    /// <summary>
    /// Creates the template, lets the product build its schema, then closes it to connections:
    /// <c>CREATE DATABASE … TEMPLATE</c> refuses a template that still has a session.
    /// </summary>
    private async Task BuildTemplateAsync(PostgreSqlContainer server, PostgresTemplate template)
    {
        using var timeout = new CancellationTokenSource(options.StartTimeout);

        try
        {
            await ExecuteAsync(server, $"CREATE DATABASE {template.Name} TEMPLATE template0", timeout.Token).ConfigureAwait(false);
            await template.Initialize(ConnectionString(server, template.Name), timeout.Token).ConfigureAwait(false);
            await ExecuteAsync(server, $"ALTER DATABASE {template.Name} WITH ALLOW_CONNECTIONS false", timeout.Token).ConfigureAwait(false);
            await ExecuteAsync(
                server,
                $"SELECT pg_terminate_backend(pid) FROM pg_stat_activity WHERE datname = '{template.Name}'",
                timeout.Token).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            throw new InvalidOperationException($"The template database '{template.Name}' could not be built.", exception);
        }
    }

    private static async Task<PostgresDatabase> CreateDatabaseAsync(
        PostgreSqlContainer server,
        string templateName,
        CancellationToken cancellationToken)
    {
        string name = "test_" + Guid.NewGuid().ToString("N");
        await ExecuteAsync(server, $"CREATE DATABASE {name} TEMPLATE {templateName}", cancellationToken).ConfigureAwait(false);
        return new PostgresDatabase(
            name,
            ConnectionString(server, name),
            database => ExecuteAsync(server, $"DROP DATABASE IF EXISTS {database} WITH (FORCE)", CancellationToken.None));
    }

    private static string ConnectionString(PostgreSqlContainer server, string database)
        => new DbConnectionStringBuilder
        {
            ConnectionString = server.GetConnectionString(),
            ["Host"] = "127.0.0.1",
            ["Database"] = database,
        }.ConnectionString;

    /// <summary>
    /// Runs one statement as the superuser on the <c>postgres</c> database. Every name interpolated
    /// by the callers is generated here or validated by <see cref="PostgresTemplate"/>.
    /// </summary>
    private static async Task ExecuteAsync(PostgreSqlContainer server, string sql, CancellationToken cancellationToken)
    {
        ExecResult result = await server.ExecAsync(
            ["psql", "--no-psqlrc", "--set", "ON_ERROR_STOP=1", "--username", Superuser, "--dbname", Superuser, "--command", sql],
            cancellationToken).ConfigureAwait(false);
        if (result.ExitCode != 0)
        {
            throw new InvalidOperationException($"psql failed ({result.ExitCode}) on '{sql}': {result.Stderr.Trim()}");
        }
    }
}
