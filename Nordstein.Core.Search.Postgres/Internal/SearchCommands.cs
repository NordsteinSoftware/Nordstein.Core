using System.Data.Common;

namespace Nordstein.Core.Search.Postgres.Internal;

/// <summary>
/// Plain ADO.NET commands on one open connection and its transaction. Parameters are named without
/// a prefix and referenced as <c>@name</c>; array values (<c>string[]</c>, <c>Guid[]</c>) rely on
/// the driver's mapping of CLR arrays to PostgreSQL arrays.
/// </summary>
internal sealed class SearchCommands
{
    private readonly DbConnection connection;
    private readonly DbTransaction? transaction;

    public SearchCommands(DbConnection connection, DbTransaction? transaction)
    {
        this.connection = connection;
        this.transaction = transaction;
    }

    public async Task<int> ExecuteAsync(
        string sql,
        CancellationToken cancellationToken,
        params (string Name, object Value)[] parameters)
    {
        DbCommand command = Create(sql, parameters);
        await using (command.ConfigureAwait(false))
        {
            return await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    public async Task<IReadOnlyList<TRow>> QueryAsync<TRow>(
        string sql,
        Func<DbDataReader, TRow> read,
        CancellationToken cancellationToken,
        params (string Name, object Value)[] parameters)
    {
        DbCommand command = Create(sql, parameters);
        await using (command.ConfigureAwait(false))
        {
            DbDataReader reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            await using (reader.ConfigureAwait(false))
            {
                var rows = new List<TRow>();
                while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                {
                    rows.Add(read(reader));
                }

                return rows;
            }
        }
    }

    private DbCommand Create(string sql, (string Name, object Value)[] parameters)
    {
        DbCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        foreach ((string name, object value) in parameters)
        {
            DbParameter parameter = command.CreateParameter();
            parameter.ParameterName = name;
            parameter.Value = value;
            command.Parameters.Add(parameter);
        }

        return command;
    }
}
