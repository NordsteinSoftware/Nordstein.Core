using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Nordstein.Core.Domain;

namespace Nordstein.Core.Search.Postgres.Internal;

/// <summary>
/// Runs statements on the connection of the ambient transaction, opening one when the caller has
/// none. Everything this package sends goes through here, so every call joins the caller's unit of
/// work and none leaves a context behind on a long-lived scope.
/// </summary>
internal sealed class SearchSession
{
    private readonly Func<DbContext> contextFactory;
    private readonly ITransaction transaction;

    public SearchSession(Func<DbContext> contextFactory, ITransaction transaction)
    {
        this.contextFactory = contextFactory;
        this.transaction = transaction;
    }

    public Task<TResult> RunAsync<TResult>(
        Func<SearchCommands, Task<TResult>> operation,
        CancellationToken cancellationToken)
        => transaction.InvokeAsync(
            async () =>
            {
                // Inside InvokeAsync the factory hands out the transactional context, which the
                // transaction seam owns and disposes.
                DbContext context = contextFactory();
                await context.Database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
                try
                {
                    var commands = new SearchCommands(
                        context.Database.GetDbConnection(),
                        context.Database.CurrentTransaction?.GetDbTransaction());
                    return await operation(commands).ConfigureAwait(false);
                }
                finally
                {
                    await context.Database.CloseConnectionAsync().ConfigureAwait(false);
                }
            },
            cancellationToken);
}
