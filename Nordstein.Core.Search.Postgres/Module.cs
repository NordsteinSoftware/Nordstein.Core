using Autofac;
using Nordstein.Core.Search.Postgres.Internal;

namespace Nordstein.Core.Search.Postgres;

/// <summary>
/// Registers the PostgreSQL implementation of <see cref="ISearchSchema"/> and
/// <see cref="ISearchIndex.Open"/>. The host must also register the storage foundation
/// (<c>StorageFoundationModule&lt;TContext&gt;</c>) with a PostgreSQL provider: every call runs on
/// its ambient transaction and its <c>Func&lt;DbContext&gt;</c>.
/// </summary>
/// <remarks>
/// The server needs the <c>vector</c> extension (pgvector) in version 0.8.0 or newer, and the role
/// that calls <see cref="ISearchSchema.EnsureAsync"/> needs the right to create it or find it
/// installed. See <c>docs/search.md</c>.
/// </remarks>
public sealed class Module : Autofac.Module
{
    /// <inheritdoc />
    protected override void Load(ContainerBuilder builder)
    {
        base.Load(builder);

        // Per dependency: each instance holds the Func<DbContext> and ITransaction of the lifetime
        // scope that resolved it, and no state of its own.
        builder.RegisterType<SearchSession>().AsSelf().InstancePerDependency();
        builder.RegisterType<PostgresSearchSchema>().As<ISearchSchema>().InstancePerDependency();
        builder.Register<ISearchIndex.Open>(context =>
        {
            SearchSession session = context.Resolve<SearchSession>();
            return definition => new PostgresSearchIndex(definition, session);
        }).InstancePerDependency();
    }
}
