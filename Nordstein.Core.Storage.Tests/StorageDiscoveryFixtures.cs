using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nordstein.Core.Common.Time;
using Nordstein.Core.Domain;
using Nordstein.Core.Domain.Events;

namespace Nordstein.Core.Storage.Tests;

// Fixtures for StorageEdgePathTests. Discovery must skip the abstract and interface stored
// entities below; the join row and the unconfigured row reach the module only as additional
// entities; the generic repository probe is an open generic type, so the repository lookup of
// discovery never matches it.

/// <summary>An abstract stored entity: no table of its own, never discovered.</summary>
internal abstract record TestAbstractEntity : Entity;

/// <summary>An interface over stored entities: never discovered.</summary>
internal interface ITestStoredContract : IEntity;

/// <summary>A join row: no id, no domain type, configured only when handed in as an additional entity.</summary>
internal sealed record TestJoinRow
{
    public required Guid LeftId { get; init; }

    public required Guid RightId { get; init; }
}

/// <summary>The join row's configuration, with its composite key.</summary>
internal sealed class TestJoinRowConfiguration : AbstractEntityConfiguration<TestJoinRow>
{
    public override void Configure(EntityTypeBuilder<TestJoinRow> builder)
    {
        builder.ToTable("TestJoinRows");
        builder.HasKey(row => new { row.LeftId, row.RightId });
    }
}

/// <summary>A row handed in as an additional entity without any configuration.</summary>
internal sealed record TestUnconfiguredRow
{
    public required Guid Key { get; init; }
}

/// <summary>
/// A repository over any stored entity that exposes the base class's protected helpers. Open
/// generic, so discovery's search for a repository of a stored entity never finds it.
/// </summary>
internal sealed class RepositoryProbe<TDomainEntity, TStoredEntity> : AbstractRepository<TDomainEntity, TStoredEntity>
    where TDomainEntity : class, IDomainEntity
    where TStoredEntity : Entity
{
    public RepositoryProbe(
        IMapper<TDomainEntity, TStoredEntity> mapper,
        Func<DbContext> contextFactory,
        ITransaction transaction,
        IEntityEventService entityEvents,
        AmbientDbContext ambient,
        IClock clock,
        IEntityCache<TDomainEntity>? cache = null)
        : base(mapper, contextFactory, transaction, entityEvents, ambient, clock, cache)
    {
    }

    public void DropCache() => InvalidateCache();

    public Task<TStoredEntity?> MapToStored(TDomainEntity? domain) => Map(domain);
}
