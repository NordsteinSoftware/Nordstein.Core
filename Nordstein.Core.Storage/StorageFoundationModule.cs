using System.Reflection;
using Autofac;
using Microsoft.EntityFrameworkCore;
using Nordstein.Core.Domain;

namespace Nordstein.Core.Storage;

/// <summary>
/// Registers the product-agnostic storage foundation and discovers a product's stored entities, EF
/// configurations, repositories and caches from an explicitly supplied assembly. Mirrors
/// <c>Nordstein.Core.Domain.Module</c>, which takes the consuming assembly the same way.
/// </summary>
/// <remarks>
/// <para>Registers, for the whole graph:</para>
/// <list type="bullet">
/// <item><typeparamref name="TContext"/> itself (a fresh instance per dependency);</item>
/// <item>the ambient-transaction seam — <see cref="AmbientDbContext"/>, <see cref="ITransaction"/>,
/// and an ambient-aware <c>Func&lt;DbContext&gt;</c> that hands out the active transactional context
/// when one exists and a fresh context otherwise;</item>
/// <item>each discovered stored entity with its <see cref="AbstractEntityConfiguration{TEntity}"/>
/// (as <see cref="IModelConfiguration"/>), its <see cref="AbstractRepository{TDomainEntity,TStoredEntity}"/>
/// (as every interface it implements), and — for entities marked <see cref="CacheableAttribute"/> —
/// its <see cref="IEntityCache{TDomainEntity}"/> and singleton <see cref="EntityCacheVersions{TDomainEntity}"/>.</item>
/// </list>
/// <para>
/// The product still owns everything provider-specific: the concrete <typeparamref name="TContext"/>,
/// its <c>DbContextOptions&lt;TContext&gt;</c> registration (the provider/connection choice), its
/// migrations assembly, and any product-specific stores/services. This module never references a
/// provider package.
/// </para>
/// </remarks>
/// <typeparam name="TContext">The product's concrete <see cref="NordsteinDbContext"/>.</typeparam>
public sealed class StorageFoundationModule<TContext> : Autofac.Module
    where TContext : NordsteinDbContext
{
    private readonly Assembly productAssembly;
    private readonly IReadOnlyCollection<Type> additionalEntities;

    /// <param name="productAssembly">
    /// The assembly scanned for stored entities (types implementing <see cref="IEntity"/>), their EF
    /// configurations and their repositories.
    /// </param>
    /// <param name="additionalEntities">
    /// Storage-only entity types to configure that the <see cref="IEntity"/> scan would miss — e.g.
    /// join/junction records that carry no <c>Id</c> and so do not implement <see cref="IEntity"/>.
    /// Duplicates of already-discovered types are ignored.
    /// </param>
    public StorageFoundationModule(Assembly productAssembly, params Type[] additionalEntities)
    {
        ArgumentNullException.ThrowIfNull(productAssembly);
        this.productAssembly = productAssembly;
        this.additionalEntities = additionalEntities ?? [];
    }

    /// <inheritdoc />
    protected override void Load(ContainerBuilder builder)
    {
        base.Load(builder);

        builder.RegisterType<TContext>()
            .AsSelf()
            .InstancePerDependency();

        // Ambient-aware context factory: while a logical transaction is active, every repository,
        // mapper and query resolves the single shared transactional context (read-your-writes on
        // one connection). Outside a transaction it hands out a fresh context per call. Repositories
        // depend on this Func<DbContext>; a product service that needs the concrete type registers
        // its own Func<TContext> the same way.
        //
        // Note the fresh-resolve branch tracks the context on the *resolving* scope until that scope
        // disposes — fine on a short-lived request scope, but a singleton resolved from the root
        // container would accumulate one per call until process shutdown. A non-transactional
        // batch/read loop in a singleton hosted service must therefore take a disposable context via
        // Autofac's auto-provided Func<Owned<TContext>> and dispose it per batch instead.
        builder.Register<Func<DbContext>>(ct =>
        {
            var scope = ct.Resolve<ILifetimeScope>();
            return () =>
            {
                var ambient = scope.Resolve<AmbientDbContext>();
                return ambient.Context ?? scope.Resolve<TContext>();
            };
        }).InstancePerLifetimeScope();

        builder.RegisterType<AmbientDbContext>()
            .AsSelf()
            .InstancePerLifetimeScope();

        builder.RegisterType<Transaction>()
            .As<ITransaction>();

        foreach (Type entityType in DiscoverEntityTypes())
        {
            ConfigureEntity(entityType, builder);
        }
    }

    private IEnumerable<Type> DiscoverEntityTypes()
    {
        IEnumerable<Type> discovered = productAssembly
            .GetTypes()
            .Where(t => typeof(IEntity).IsAssignableFrom(t) && t is { IsInterface: false, IsAbstract: false });

        return discovered.Concat(additionalEntities).Distinct();
    }

    private void ConfigureEntity(Type storedEntityType, ContainerBuilder builder)
    {
        builder.RegisterType(storedEntityType)
            .AsSelf();

        var configurationBaseType = typeof(AbstractEntityConfiguration<>).MakeGenericType(storedEntityType);

        // find the type that derives from configurationBaseType
        Type configurationType = productAssembly
                                     .GetTypes()
                                     .SingleOrDefault(t => t.IsSubclassOf(configurationBaseType))
                                 ?? throw new InvalidOperationException(
                                     $"No configuration type found for entity type {storedEntityType.Name}");

        builder
            .RegisterType(configurationType)
            .AsImplementedInterfaces()
            .InstancePerLifetimeScope();

        // get the StoredDomainEntity attribute to locate the associated Domain Entity Type
        var domainEntityType = storedEntityType.GetDomainEntityType();
        if (domainEntityType != null)
        {
            var repositoryBaseType = typeof(AbstractRepository<,>).MakeGenericType(domainEntityType, storedEntityType);
            // find the type that derives from repositoryBaseType
            Type repositoryType = productAssembly
                                      .GetTypes()
                                      .SingleOrDefault(t => t.IsSubclassOf(repositoryBaseType))
                                  ?? throw new InvalidOperationException(
                                      $"No repository type found for entity type {storedEntityType.Name}");

            // register repository type as all registered interfaces
            foreach (Type interfaceType in repositoryType.GetInterfaces())
            {
                builder.RegisterType(repositoryType).As(interfaceType);
            }

            // opt-in in-memory cache for slow-changing reference data
            if (storedEntityType.GetCustomAttribute<CacheableAttribute>() != null)
            {
                // The invalidation registry is a singleton; the cache that consults it is NOT.
                // Cached domain entities hold the repository they were materialized from, which
                // closes over its resolving lifetime scope — caching them in the root container
                // would hand out entities bound to a disposed request scope. Keeping the entries
                // scope-local and the *versions* process-wide gives cross-scope write-through
                // invalidation without reintroducing that. See EntityCacheVersions.
                Type versionsType = typeof(EntityCacheVersions<>).MakeGenericType(domainEntityType);
                builder.RegisterType(versionsType).AsSelf().SingleInstance();

                Type cacheImpl = typeof(EntityCache<>).MakeGenericType(domainEntityType);
                Type cacheInterface = typeof(IEntityCache<>).MakeGenericType(domainEntityType);
                builder.RegisterType(cacheImpl).As(cacheInterface).InstancePerLifetimeScope();
            }
        }
    }
}
