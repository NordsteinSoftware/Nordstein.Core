using Autofac;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Nordstein.Core.Testing;
using Nordstein.Core.Testing.Postgres;
using Npgsql;

namespace Nordstein.Core.Search.Postgres.Tests.Internal;

/// <summary>
/// Helpers shared by the PostgreSQL search tests: a container bound to one test's database, a
/// ready index, and raw reads for assertions. Stateless: every value is built inside the test.
/// </summary>
public abstract class PostgresSearchTestBase : BaseTest<Module>
{
    private protected static readonly SearchIndexDefinition Notes = new("notes", "example-embed-1", 3, "english");

    private protected IServiceProvider ServicesFor(PostgresDatabase database)
        => GetServices(builder => builder
            .Register(_ => new DbContextOptionsBuilder<SearchTestDbContext>().UseNpgsql(database.ConnectionString).Options)
            .As<DbContextOptions<SearchTestDbContext>>()
            .SingleInstance());

    private protected async Task<ISearchIndex> EnsureIndexAsync(IServiceProvider services, SearchIndexDefinition definition)
    {
        await services.GetRequiredService<ISearchSchema>().EnsureAsync(definition, CancellationToken);
        return services.GetRequiredService<ISearchIndex.Open>()(definition);
    }

    private protected async Task<TValue> ScalarAsync<TValue>(PostgresDatabase database, string sql)
    {
        await using var connection = new NpgsqlConnection(database.ConnectionString);
        await connection.OpenAsync(CancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        object? value = await command.ExecuteScalarAsync(CancellationToken);
        return value is TValue typed
            ? typed
            : throw new InvalidOperationException($"'{sql}' answered {value ?? "null"}, not a {typeof(TValue).Name}.");
    }

    private protected async Task ExecuteAsync(PostgresDatabase database, string sql)
    {
        await using var connection = new NpgsqlConnection(database.ConnectionString);
        await connection.OpenAsync(CancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync(CancellationToken);
    }

    private protected static SearchEntry Entry(int number, string text, params string[] facets)
        => new(EntryId(number), OwnerId(number), text, facets);

    private protected static Guid EntryId(int number) => new($"00000000-0000-0000-0000-{number:D12}");

    private protected static Guid OwnerId(int number) => new($"10000000-0000-0000-0000-{number:D12}");

    private protected static ReadOnlyMemory<float> Vector(params float[] values) => values;

    private protected static SearchQuery TextQuery(string text, SearchFilter? filter = null)
        => new(text, null, filter ?? SearchFilter.None, SearchParameters.Default);
}
