using System.ComponentModel.DataAnnotations;
using Autofac;
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Nordstein.Core.Common.Time;
using Nordstein.Core.Testing;

namespace Nordstein.Core.Domain.Tests;

[TestClass]
public sealed class DomainEntityContractTests : BaseTest<Module>
{
    [TestMethod]
    public void Constructor_ForNewEntity_AssignsFreshIdentityAndTimestamps()
    {
        IServiceProvider services = GetServices();
        var repository = services.GetRequiredService<IRepository<ITestEntity>>();
        IClock clock = services.GetRequiredService<IClock>();

        var entity = new TestEntity(repository, clock);

        entity.Id.Should().NotBe(Guid.Empty);
        entity.IsArchived.Should().BeFalse();
        entity.CreatedAt.Should().BeOnOrBefore(DateTimeOffset.UtcNow);
        entity.UpdatedAt.Should().BeOnOrAfter(entity.CreatedAt);
    }

    [TestMethod]
    public void Constructor_ForNewEntity_StampsBothTimestampsFromTheClock()
    {
        var pinned = new MutableClock(new DateTimeOffset(2031, 3, 4, 5, 6, 7, TimeSpan.Zero));
        IServiceProvider services = GetServices(builder => builder.RegisterInstance(pinned).As<IClock>());
        var repository = services.GetRequiredService<IRepository<ITestEntity>>();

        var entity = new TestEntity(repository, pinned);

        entity.CreatedAt.Should().Be(pinned.UtcNow);
        entity.UpdatedAt.Should().Be(pinned.UtcNow);
    }

    [TestMethod]
    public void Validate_UnderAClockAheadOfTheSystemClock_PassesForAnEntityStampedByThatClock()
    {
        // The timestamps are judged against the entity's clock, never the machine's: a host running
        // ahead of real time (a demonstration clock) still constructs valid entities.
        var ahead = new MutableClock(DateTimeOffset.UtcNow.AddYears(5));
        IServiceProvider services = GetServices(builder => builder.RegisterInstance(ahead).As<IClock>());
        var repository = services.GetRequiredService<IRepository<ITestEntity>>();
        var entity = new TestEntity(repository, ahead);

        var results = new List<ValidationResult>();
        bool valid = Validator.TryValidateObject(entity, new ValidationContext(entity), results, validateAllProperties: true);

        valid.Should().BeTrue();
        results.Should().BeEmpty();
    }

    [TestMethod]
    public void Validate_ForTimestampsAfterTheClock_ProducesValidationError()
    {
        var pinned = new MutableClock(new DateTimeOffset(2031, 3, 4, 5, 6, 7, TimeSpan.Zero));
        IServiceProvider services = GetServices(builder => builder.RegisterInstance(pinned).As<IClock>());
        var repository = services.GetRequiredService<IRepository<ITestEntity>>();
        DateTimeOffset later = pinned.UtcNow.AddSeconds(1);
        var entity = new TestEntity(new ExistingEntityData(Guid.NewGuid(), later, later, false), repository, pinned);

        var results = new List<ValidationResult>();
        bool valid = Validator.TryValidateObject(entity, new ValidationContext(entity), results, validateAllProperties: true);

        valid.Should().BeFalse();
        results.Should().Contain(result => result.ErrorMessage == "CreatedAt must be in the past");
    }

    [TestMethod]
    public void Constructor_NullClock_Throws()
    {
        IServiceProvider services = GetServices();
        var repository = services.GetRequiredService<IRepository<ITestEntity>>();

        // guard test
        Action act = () => _ = new TestEntity(repository, null!);

        act.Should().Throw<ArgumentNullException>().WithParameterName("clock");
    }

    [TestMethod]
    public void Constructor_ForExistingEntityWithNullClock_Throws()
    {
        IServiceProvider services = GetServices();
        var repository = services.GetRequiredService<IRepository<ITestEntity>>();
        DateTimeOffset past = DateTimeOffset.UtcNow.AddMinutes(-5);

        // guard test
        Action act = () => _ = new TestEntity(new ExistingEntityData(Guid.NewGuid(), past, past, false), repository, null!);

        act.Should().Throw<ArgumentNullException>().WithParameterName("clock");
    }

    [TestMethod]
    public void Validate_ForNewEntity_ProducesNoValidationErrors()
    {
        IServiceProvider services = GetServices();
        var repository = services.GetRequiredService<IRepository<ITestEntity>>();
        IClock clock = services.GetRequiredService<IClock>();
        var entity = new TestEntity(repository, clock);

        var results = new List<ValidationResult>();
        bool valid = Validator.TryValidateObject(
            entity, new ValidationContext(entity), results, validateAllProperties: true);

        valid.Should().BeTrue();
        results.Should().BeEmpty();
    }

    [TestMethod]
    public void Validate_ForDefaultId_ProducesValidationError()
    {
        IServiceProvider services = GetServices();
        var repository = services.GetRequiredService<IRepository<ITestEntity>>();
        IClock clock = services.GetRequiredService<IClock>();
        DateTimeOffset past = DateTimeOffset.UtcNow.AddMinutes(-5);
        var entity = new TestEntity(new ExistingEntityData(Guid.Empty, past, past, false), repository, clock);

        var results = new List<ValidationResult>();
        bool valid = Validator.TryValidateObject(
            entity, new ValidationContext(entity), results, validateAllProperties: true);

        valid.Should().BeFalse();
        results.Should().NotBeEmpty();
    }

    [TestMethod]
    public void Validate_WhenUpdatedBeforeCreated_ProducesValidationError()
    {
        IServiceProvider services = GetServices();
        var repository = services.GetRequiredService<IRepository<ITestEntity>>();
        IClock clock = services.GetRequiredService<IClock>();
        DateTimeOffset created = DateTimeOffset.UtcNow.AddMinutes(-5);
        DateTimeOffset updated = created.AddMinutes(-1);
        var entity = new TestEntity(new ExistingEntityData(Guid.NewGuid(), created, updated, false), repository, clock);

        var results = new List<ValidationResult>();
        bool valid = Validator.TryValidateObject(
            entity, new ValidationContext(entity), results, validateAllProperties: true);

        valid.Should().BeFalse();
        results.Should().NotBeEmpty();
    }

    [TestMethod]
    public void Validate_ForFutureTimestamps_ProducesValidationError()
    {
        IServiceProvider services = GetServices();
        var repository = services.GetRequiredService<IRepository<ITestEntity>>();
        IClock clock = services.GetRequiredService<IClock>();
        DateTimeOffset future = DateTimeOffset.UtcNow.AddMinutes(5);
        var entity = new TestEntity(new ExistingEntityData(Guid.NewGuid(), future, future, false), repository, clock);

        var results = new List<ValidationResult>();
        bool valid = Validator.TryValidateObject(
            entity, new ValidationContext(entity), results, validateAllProperties: true);

        valid.Should().BeFalse();
        results.Should().NotBeEmpty();
    }

    [TestMethod]
    public async Task AddAsync_ForNewEntity_PersistsThroughRepository()
    {
        IServiceProvider services = GetServices();
        var repository = services.GetRequiredService<IRepository<ITestEntity>>();
        IClock clock = services.GetRequiredService<IClock>();
        var entity = new TestEntity(repository, clock);

        ITestEntity added = await entity.AddAsync(CancellationToken);

        added.Id.Should().Be(entity.Id);
        (await repository.ContainsAsync(entity.Id, CancellationToken)).Should().BeTrue();
    }

    [TestMethod]
    public async Task ReloadAsync_AfterAdd_ReturnsPersistedEntity()
    {
        IServiceProvider services = GetServices();
        var repository = services.GetRequiredService<IRepository<ITestEntity>>();
        IClock clock = services.GetRequiredService<IClock>();
        var entity = new TestEntity(repository, clock);
        await entity.AddAsync(CancellationToken);

        ITestEntity reloaded = await entity.ReloadAsync(CancellationToken);

        reloaded.Id.Should().Be(entity.Id);
    }

    [TestMethod]
    public async Task UpdateAsync_DelegatesToRepository()
    {
        IServiceProvider services = GetServices();
        var repository = services.GetRequiredService<IRepository<ITestEntity>>();
        IClock clock = services.GetRequiredService<IClock>();
        Guid id = Guid.NewGuid();
        DateTimeOffset past = DateTimeOffset.UtcNow.AddMinutes(-5);
        var original = new TestEntity(new ExistingEntityData(id, past, past, false), repository, clock);
        var replacement = new TestEntity(new ExistingEntityData(id, past, past, false), repository, clock);
        await repository.AddAsync(original, CancellationToken);

        ITestEntity updated = await replacement.UpdateAsync(CancellationToken);

        // DomainEntity records compare equal by Id alone, so only reference comparison proves the
        // delegation ran: repository.UpdateAsync replaces the stored `original` with the calling
        // instance, so FindAsync must now return the exact `replacement` reference.
        updated.Should().BeSameAs(replacement);
        (await repository.FindAsync(id, CancellationToken)).Should().BeSameAs(replacement);
    }

    [TestMethod]
    public async Task UpsertAsync_ForNewEntity_PersistsThroughRepository()
    {
        IServiceProvider services = GetServices();
        var repository = services.GetRequiredService<IRepository<ITestEntity>>();
        IClock clock = services.GetRequiredService<IClock>();
        var entity = new TestEntity(repository, clock);

        ITestEntity upserted = await entity.UpsertAsync(CancellationToken);

        upserted.Id.Should().Be(entity.Id);
        (await repository.ContainsAsync(entity.Id, CancellationToken)).Should().BeTrue();
    }

    [TestMethod]
    public async Task RemoveAsync_AfterAdd_DeletesThroughRepository()
    {
        IServiceProvider services = GetServices();
        var repository = services.GetRequiredService<IRepository<ITestEntity>>();
        IClock clock = services.GetRequiredService<IClock>();
        var entity = new TestEntity(repository, clock);
        await entity.AddAsync(CancellationToken);

        await entity.RemoveAsync(CancellationToken);

        (await repository.ContainsAsync(entity.Id, CancellationToken)).Should().BeFalse();
    }

    [TestMethod]
    public void Equals_ForSameId_ReturnsTrueWithMatchingHashCode()
    {
        IServiceProvider services = GetServices();
        var repository = services.GetRequiredService<IRepository<ITestEntity>>();
        IClock clock = services.GetRequiredService<IClock>();
        Guid id = Guid.NewGuid();
        DateTimeOffset created = DateTimeOffset.UtcNow.AddMinutes(-5);
        var a = new TestEntity(new ExistingEntityData(id, created, created, false), repository, clock);
        var b = new TestEntity(new ExistingEntityData(id, created, created, false), repository, clock);

        a.Should().Be(b);
        a.GetHashCode().Should().Be(b.GetHashCode());
    }

    [TestMethod]
    public void Equals_ForSameIdDifferentMetadata_IgnoresMetadataAndRemainsEqual()
    {
        IServiceProvider services = GetServices();
        var repository = services.GetRequiredService<IRepository<ITestEntity>>();
        IClock clock = services.GetRequiredService<IClock>();
        Guid id = Guid.NewGuid();
        DateTimeOffset first = DateTimeOffset.UtcNow.AddMinutes(-10);
        DateTimeOffset second = DateTimeOffset.UtcNow.AddMinutes(-5);
        var a = new TestEntity(new ExistingEntityData(id, first, first, false), repository, clock);
        var b = new TestEntity(new ExistingEntityData(id, second, second, true), repository, clock);

        a.Should().Be(b);
        a.GetHashCode().Should().Be(b.GetHashCode());
    }

    [TestMethod]
    public void Equals_ForDifferentId_ReturnsFalse()
    {
        IServiceProvider services = GetServices();
        var repository = services.GetRequiredService<IRepository<ITestEntity>>();
        IClock clock = services.GetRequiredService<IClock>();
        DateTimeOffset created = DateTimeOffset.UtcNow.AddMinutes(-5);
        var a = new TestEntity(new ExistingEntityData(Guid.NewGuid(), created, created, false), repository, clock);
        var b = new TestEntity(new ExistingEntityData(Guid.NewGuid(), created, created, false), repository, clock);

        a.Should().NotBe(b);
    }

    [TestMethod]
    public void Equals_ForNull_ReturnsFalse()
    {
        IServiceProvider services = GetServices();
        var repository = services.GetRequiredService<IRepository<ITestEntity>>();
        IClock clock = services.GetRequiredService<IClock>();
        var entity = new TestEntity(repository, clock);

        entity.Equals((object?)null).Should().BeFalse();
    }
}
