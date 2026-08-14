using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Metadata;

namespace Nordstein.Core.Storage;

/// <summary>
/// Reusable EF Core context base for Nordstein products. It owns the two behaviours every product
/// context shares — applying the discovered <see cref="IModelConfiguration"/> slices and enforcing
/// the <see cref="Entity.UpdatedAt"/> optimistic-concurrency-token convention — while each product
/// derives a concrete context that carries its own identity (for <c>DbContextOptions&lt;TContext&gt;</c>
/// and its migrations assembly) and its provider wiring.
/// </summary>
/// <remarks>
/// A product context is expected to look like:
/// <code>
/// internal sealed class StorageDbContext : NordsteinDbContext
/// {
///     public StorageDbContext(IEnumerable&lt;IModelConfiguration&gt; configurations,
///         DbContextOptions&lt;StorageDbContext&gt; options) : base(configurations, options) { }
/// }
/// </code>
/// </remarks>
public abstract class NordsteinDbContext : DbContext
{
    private readonly IReadOnlyCollection<IModelConfiguration> configurations;

    protected NordsteinDbContext(IEnumerable<IModelConfiguration> configurations, DbContextOptions options)
        : base(options)
    {
        this.configurations = configurations.ToArray();
    }

    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        foreach (IModelConfiguration configuration in configurations)
        {
            configuration.CreateModel(modelBuilder);
        }

        // Every persisted entity derives from Entity and carries an UpdatedAt timestamp that the
        // repositories stamp on each write. Marking it as a concurrency token makes EF emit
        // `UPDATE/DELETE ... WHERE UpdatedAt = @original` and check the affected row count, so a
        // concurrent writer that already moved the row on causes a DbUpdateConcurrencyException
        // instead of a silent lost update. This enforces optimistic concurrency at the database —
        // the in-app pre-check in AbstractRepository.UpdateCoreAsync is only a fast-fail. (The
        // in-memory provider ignores concurrency tokens, so unit tests are unaffected.)
        foreach (IMutableEntityType entityType in modelBuilder.Model.GetEntityTypes())
        {
            IMutableProperty? updatedAt = entityType.FindProperty(nameof(Entity.UpdatedAt));
            if (updatedAt is not null && updatedAt.ClrType == typeof(DateTimeOffset))
            {
                updatedAt.IsConcurrencyToken = true;
            }
        }
    }

    /// <inheritdoc />
    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        base.OnConfiguring(optionsBuilder);

        // Tests construct multiple contexts with independent service providers.
        optionsBuilder.ConfigureWarnings(config => config.Ignore(CoreEventId.ManyServiceProvidersCreatedWarning));
    }
}
