using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Nordstein.Core.Storage;

/// <summary>
/// Base class for per-entity EF Core configuration. Derive one per stored entity and implement
/// <see cref="Configure"/>; the discovery in <see cref="StorageFoundationModule{TContext}"/>
/// registers it as an <see cref="IModelConfiguration"/> so <see cref="NordsteinDbContext"/> applies it.
/// </summary>
public abstract class AbstractEntityConfiguration<TEntity> :
    IEntityTypeConfiguration<TEntity>,
    IModelConfiguration
    where TEntity : class
{
    /// <inheritdoc />
    public abstract void Configure(EntityTypeBuilder<TEntity> builder);

    /// <inheritdoc />
    public void CreateModel(ModelBuilder builder)
        => builder.ApplyConfiguration(this);
}
