using System.ComponentModel.DataAnnotations;
using JetBrains.Annotations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nordstein.Core.Domain;
using Nordstein.Core.Domain.Events;

namespace Nordstein.Core.Storage.Tests;

// A minimal, self-contained domain + storage stack the foundation tests exercise. Kept plain (no
// repository back-reference on the domain object) so the mappers have no dependency cycle. Four
// entities give the foundation full coverage:
//   TestThing        — plain entity (base CRUD, paging, bulk, transactions)
//   TestCachedThing  — [Cacheable] (the read-through cache path + module cache wiring)
//   TestDoc          — archivable, hard-delete allowed (archive/unarchive/exclude/hard-delete)
//   TestLockedDoc    — archivable, archive-only (the hard-delete refusal)

// ---------------------------------------------------------------------------------------------
// TestThing — plain entity
// ---------------------------------------------------------------------------------------------

internal interface ITestThing : IDomainEntity
{
    string Name { get; }
}

internal sealed record TestThing : ITestThing
{
    public required Guid Id { get; init; }
    public required DateTimeOffset CreatedAt { get; init; }
    public required DateTimeOffset UpdatedAt { get; init; }
    public string Name { get; init; } = string.Empty;

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext) => [];
}

[StoredDomainEntity(typeof(ITestThing))]
internal sealed record TestThingEntity : Entity
{
    public required string Name { get; init; }
}

[UsedImplicitly]
internal sealed class TestThingConfiguration : AbstractEntityConfiguration<TestThingEntity>
{
    public override void Configure(EntityTypeBuilder<TestThingEntity> builder)
    {
        builder.ToTable("TestThings");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Name).IsRequired();
    }
}

[UsedImplicitly]
internal sealed class TestThingMapper : IMapper<ITestThing, TestThingEntity>
{
    public Task<ITestThing> Map(TestThingEntity storedEntity, CancellationToken cancellationToken = default)
        => Task.FromResult<ITestThing>(new TestThing
        {
            Id = storedEntity.Id,
            CreatedAt = storedEntity.CreatedAt,
            UpdatedAt = storedEntity.UpdatedAt,
            Name = storedEntity.Name,
        });

    public Task<TestThingEntity> Map(ITestThing domainEntity, CancellationToken cancellationToken = default)
        => Task.FromResult(new TestThingEntity
        {
            Id = domainEntity.Id,
            CreatedAt = domainEntity.CreatedAt,
            UpdatedAt = domainEntity.UpdatedAt,
            Name = domainEntity.Name,
        });
}

[UsedImplicitly]
internal sealed class TestThingRepository : AbstractRepository<ITestThing, TestThingEntity>
{
    public TestThingRepository(
        IMapper<ITestThing, TestThingEntity> mapper,
        Func<DbContext> contextFactory,
        ITransaction transaction,
        IEntityEventService entityEvents,
        AmbientDbContext ambient,
        IEntityCache<ITestThing>? cache = null)
        : base(mapper, contextFactory, transaction, entityEvents, ambient, cache)
    {
    }
}

// ---------------------------------------------------------------------------------------------
// TestCachedThing — [Cacheable]
// ---------------------------------------------------------------------------------------------

internal interface ITestCachedThing : IDomainEntity
{
    string Name { get; }
}

internal sealed record TestCachedThing : ITestCachedThing
{
    public required Guid Id { get; init; }
    public required DateTimeOffset CreatedAt { get; init; }
    public required DateTimeOffset UpdatedAt { get; init; }
    public string Name { get; init; } = string.Empty;

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext) => [];
}

[StoredDomainEntity(typeof(ITestCachedThing))]
[Cacheable]
internal sealed record TestCachedThingEntity : Entity
{
    public required string Name { get; init; }
}

[UsedImplicitly]
internal sealed class TestCachedThingConfiguration : AbstractEntityConfiguration<TestCachedThingEntity>
{
    public override void Configure(EntityTypeBuilder<TestCachedThingEntity> builder)
    {
        builder.ToTable("TestCachedThings");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Name).IsRequired();
    }
}

[UsedImplicitly]
internal sealed class TestCachedThingMapper : IMapper<ITestCachedThing, TestCachedThingEntity>
{
    public Task<ITestCachedThing> Map(TestCachedThingEntity storedEntity, CancellationToken cancellationToken = default)
        => Task.FromResult<ITestCachedThing>(new TestCachedThing
        {
            Id = storedEntity.Id,
            CreatedAt = storedEntity.CreatedAt,
            UpdatedAt = storedEntity.UpdatedAt,
            Name = storedEntity.Name,
        });

    public Task<TestCachedThingEntity> Map(ITestCachedThing domainEntity, CancellationToken cancellationToken = default)
        => Task.FromResult(new TestCachedThingEntity
        {
            Id = domainEntity.Id,
            CreatedAt = domainEntity.CreatedAt,
            UpdatedAt = domainEntity.UpdatedAt,
            Name = domainEntity.Name,
        });
}

[UsedImplicitly]
internal sealed class TestCachedThingRepository : AbstractRepository<ITestCachedThing, TestCachedThingEntity>
{
    public TestCachedThingRepository(
        IMapper<ITestCachedThing, TestCachedThingEntity> mapper,
        Func<DbContext> contextFactory,
        ITransaction transaction,
        IEntityEventService entityEvents,
        AmbientDbContext ambient,
        IEntityCache<ITestCachedThing>? cache = null)
        : base(mapper, contextFactory, transaction, entityEvents, ambient, cache)
    {
    }
}

// ---------------------------------------------------------------------------------------------
// TestDoc — archivable, hard-delete allowed
// ---------------------------------------------------------------------------------------------

internal interface ITestDocRepository : IArchivableRepository<ITestDoc>
{
    /// <summary>Exposes the base <c>UnarchiveAsync</c> for the tests.</summary>
    Task Unarchive(Guid id, CancellationToken cancellationToken = default);
}

internal interface ITestDoc : IArchivable
{
    string Title { get; }
}

internal sealed record TestDoc : ITestDoc
{
    public required Guid Id { get; init; }
    public required DateTimeOffset CreatedAt { get; init; }
    public required DateTimeOffset UpdatedAt { get; init; }
    public bool IsArchived { get; init; }
    public string Title { get; init; } = string.Empty;

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext) => [];
}

[StoredDomainEntity(typeof(ITestDoc))]
internal sealed record TestDocEntity : Entity, IArchivableEntity
{
    public required bool IsArchived { get; init; }
    public required string Title { get; init; }
}

[UsedImplicitly]
internal sealed class TestDocConfiguration : AbstractEntityConfiguration<TestDocEntity>
{
    public override void Configure(EntityTypeBuilder<TestDocEntity> builder)
    {
        builder.ToTable("TestDocs");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Title).IsRequired();
    }
}

[UsedImplicitly]
internal sealed class TestDocMapper : IMapper<ITestDoc, TestDocEntity>
{
    public Task<ITestDoc> Map(TestDocEntity storedEntity, CancellationToken cancellationToken = default)
        => Task.FromResult<ITestDoc>(new TestDoc
        {
            Id = storedEntity.Id,
            CreatedAt = storedEntity.CreatedAt,
            UpdatedAt = storedEntity.UpdatedAt,
            IsArchived = storedEntity.IsArchived,
            Title = storedEntity.Title,
        });

    public Task<TestDocEntity> Map(ITestDoc domainEntity, CancellationToken cancellationToken = default)
        => Task.FromResult(new TestDocEntity
        {
            Id = domainEntity.Id,
            CreatedAt = domainEntity.CreatedAt,
            UpdatedAt = domainEntity.UpdatedAt,
            IsArchived = domainEntity.IsArchived,
            Title = domainEntity.Title,
        });
}

[UsedImplicitly]
internal sealed class TestDocRepository : ArchivableRepository<ITestDoc, TestDocEntity>, ITestDocRepository
{
    public TestDocRepository(
        IMapper<ITestDoc, TestDocEntity> mapper,
        Func<DbContext> contextFactory,
        ITransaction transaction,
        IEntityEventService entityEvents,
        AmbientDbContext ambient,
        IEntityCache<ITestDoc>? cache = null)
        : base(mapper, contextFactory, transaction, entityEvents, ambient, cache)
    {
    }

    public Task Unarchive(Guid id, CancellationToken cancellationToken = default)
        => UnarchiveAsync(id, cancellationToken);
}

// ---------------------------------------------------------------------------------------------
// TestLockedDoc — archivable, archive-only (no hard delete)
// ---------------------------------------------------------------------------------------------

internal interface ITestLockedDoc : IArchivable
{
    string Title { get; }
}

internal sealed record TestLockedDoc : ITestLockedDoc
{
    public required Guid Id { get; init; }
    public required DateTimeOffset CreatedAt { get; init; }
    public required DateTimeOffset UpdatedAt { get; init; }
    public bool IsArchived { get; init; }
    public string Title { get; init; } = string.Empty;

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext) => [];
}

[StoredDomainEntity(typeof(ITestLockedDoc))]
internal sealed record TestLockedDocEntity : Entity, IArchivableEntity
{
    public required bool IsArchived { get; init; }
    public required string Title { get; init; }
}

[UsedImplicitly]
internal sealed class TestLockedDocConfiguration : AbstractEntityConfiguration<TestLockedDocEntity>
{
    public override void Configure(EntityTypeBuilder<TestLockedDocEntity> builder)
    {
        builder.ToTable("TestLockedDocs");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Title).IsRequired();
    }
}

[UsedImplicitly]
internal sealed class TestLockedDocMapper : IMapper<ITestLockedDoc, TestLockedDocEntity>
{
    public Task<ITestLockedDoc> Map(TestLockedDocEntity storedEntity, CancellationToken cancellationToken = default)
        => Task.FromResult<ITestLockedDoc>(new TestLockedDoc
        {
            Id = storedEntity.Id,
            CreatedAt = storedEntity.CreatedAt,
            UpdatedAt = storedEntity.UpdatedAt,
            IsArchived = storedEntity.IsArchived,
            Title = storedEntity.Title,
        });

    public Task<TestLockedDocEntity> Map(ITestLockedDoc domainEntity, CancellationToken cancellationToken = default)
        => Task.FromResult(new TestLockedDocEntity
        {
            Id = domainEntity.Id,
            CreatedAt = domainEntity.CreatedAt,
            UpdatedAt = domainEntity.UpdatedAt,
            IsArchived = domainEntity.IsArchived,
            Title = domainEntity.Title,
        });
}

[UsedImplicitly]
internal sealed class TestLockedDocRepository : ArchivableRepository<ITestLockedDoc, TestLockedDocEntity>
{
    public TestLockedDocRepository(
        IMapper<ITestLockedDoc, TestLockedDocEntity> mapper,
        Func<DbContext> contextFactory,
        ITransaction transaction,
        IEntityEventService entityEvents,
        AmbientDbContext ambient,
        IEntityCache<ITestLockedDoc>? cache = null)
        : base(mapper, contextFactory, transaction, entityEvents, ambient, cache)
    {
    }

    // Archive-only: a hard delete would cascade-remove history, so it is refused.
    protected override bool SupportsHardDelete => false;
}

// ---------------------------------------------------------------------------------------------
// TestOwner — carries an EF owned type (exercises the owned-navigation update path)
// ---------------------------------------------------------------------------------------------

internal interface ITestOwner : IDomainEntity
{
    string Label { get; }
    string? Note { get; }
}

internal sealed record TestOwner : ITestOwner
{
    public required Guid Id { get; init; }
    public required DateTimeOffset CreatedAt { get; init; }
    public required DateTimeOffset UpdatedAt { get; init; }
    public string Label { get; init; } = string.Empty;
    public string? Note { get; init; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext) => [];
}

internal sealed record OwnedNote
{
    public required string Text { get; init; }
}

[StoredDomainEntity(typeof(ITestOwner))]
internal sealed record TestOwnerEntity : Entity
{
    public required string Label { get; init; }
    public OwnedNote? Note { get; init; }
}

[UsedImplicitly]
internal sealed class TestOwnerConfiguration : AbstractEntityConfiguration<TestOwnerEntity>
{
    public override void Configure(EntityTypeBuilder<TestOwnerEntity> builder)
    {
        builder.ToTable("TestOwners");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Label).IsRequired();
        builder.OwnsOne(e => e.Note);
    }
}

[UsedImplicitly]
internal sealed class TestOwnerMapper : IMapper<ITestOwner, TestOwnerEntity>
{
    public Task<ITestOwner> Map(TestOwnerEntity storedEntity, CancellationToken cancellationToken = default)
        => Task.FromResult<ITestOwner>(new TestOwner
        {
            Id = storedEntity.Id,
            CreatedAt = storedEntity.CreatedAt,
            UpdatedAt = storedEntity.UpdatedAt,
            Label = storedEntity.Label,
            Note = storedEntity.Note?.Text,
        });

    public Task<TestOwnerEntity> Map(ITestOwner domainEntity, CancellationToken cancellationToken = default)
        => Task.FromResult(new TestOwnerEntity
        {
            Id = domainEntity.Id,
            CreatedAt = domainEntity.CreatedAt,
            UpdatedAt = domainEntity.UpdatedAt,
            Label = domainEntity.Label,
            Note = domainEntity.Note is null ? null : new OwnedNote { Text = domainEntity.Note },
        });
}

[UsedImplicitly]
internal sealed class TestOwnerRepository : AbstractRepository<ITestOwner, TestOwnerEntity>
{
    public TestOwnerRepository(
        IMapper<ITestOwner, TestOwnerEntity> mapper,
        Func<DbContext> contextFactory,
        ITransaction transaction,
        IEntityEventService entityEvents,
        AmbientDbContext ambient,
        IEntityCache<ITestOwner>? cache = null)
        : base(mapper, contextFactory, transaction, entityEvents, ambient, cache)
    {
    }
}

// ---------------------------------------------------------------------------------------------
// Context
// ---------------------------------------------------------------------------------------------

internal sealed class TestDbContext : NordsteinDbContext
{
    public TestDbContext(IEnumerable<IModelConfiguration> configurations, DbContextOptions<TestDbContext> options)
        : base(configurations, options)
    {
    }
}
