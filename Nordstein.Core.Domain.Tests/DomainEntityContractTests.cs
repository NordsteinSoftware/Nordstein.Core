using System.ComponentModel.DataAnnotations;
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
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

        var entity = new TestEntity(repository);

        entity.Id.Should().NotBe(Guid.Empty);
        entity.IsArchived.Should().BeFalse();
        entity.CreatedAt.Should().BeOnOrBefore(DateTimeOffset.UtcNow);
        entity.UpdatedAt.Should().BeOnOrAfter(entity.CreatedAt);
    }

    [TestMethod]
    public void Validate_ForNewEntity_ProducesNoValidationErrors()
    {
        IServiceProvider services = GetServices();
        var repository = services.GetRequiredService<IRepository<ITestEntity>>();
        var entity = new TestEntity(repository);

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
        DateTimeOffset past = DateTimeOffset.UtcNow.AddMinutes(-5);
        var entity = new TestEntity(new ExistingEntityData(Guid.Empty, past, past, false), repository);

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
        DateTimeOffset created = DateTimeOffset.UtcNow.AddMinutes(-5);
        DateTimeOffset updated = created.AddMinutes(-1);
        var entity = new TestEntity(new ExistingEntityData(Guid.NewGuid(), created, updated, false), repository);

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
        DateTimeOffset future = DateTimeOffset.UtcNow.AddMinutes(5);
        var entity = new TestEntity(new ExistingEntityData(Guid.NewGuid(), future, future, false), repository);

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
        var entity = new TestEntity(repository);

        ITestEntity added = await entity.AddAsync(CancellationToken);

        added.Id.Should().Be(entity.Id);
        (await repository.ContainsAsync(entity.Id, CancellationToken)).Should().BeTrue();
    }

    [TestMethod]
    public async Task ReloadAsync_AfterAdd_ReturnsPersistedEntity()
    {
        IServiceProvider services = GetServices();
        var repository = services.GetRequiredService<IRepository<ITestEntity>>();
        var entity = new TestEntity(repository);
        await entity.AddAsync(CancellationToken);

        ITestEntity reloaded = await entity.ReloadAsync(CancellationToken);

        reloaded.Id.Should().Be(entity.Id);
    }

    [TestMethod]
    public async Task UpdateAsync_DelegatesToRepository()
    {
        IServiceProvider services = GetServices();
        var repository = services.GetRequiredService<IRepository<ITestEntity>>();
        Guid id = Guid.NewGuid();
        DateTimeOffset past = DateTimeOffset.UtcNow.AddMinutes(-5);
        var original = new TestEntity(new ExistingEntityData(id, past, past, false), repository);
        var replacement = new TestEntity(new ExistingEntityData(id, past, past, false), repository);
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
        var entity = new TestEntity(repository);

        ITestEntity upserted = await entity.UpsertAsync(CancellationToken);

        upserted.Id.Should().Be(entity.Id);
        (await repository.ContainsAsync(entity.Id, CancellationToken)).Should().BeTrue();
    }

    [TestMethod]
    public async Task RemoveAsync_AfterAdd_DeletesThroughRepository()
    {
        IServiceProvider services = GetServices();
        var repository = services.GetRequiredService<IRepository<ITestEntity>>();
        var entity = new TestEntity(repository);
        await entity.AddAsync(CancellationToken);

        await entity.RemoveAsync(CancellationToken);

        (await repository.ContainsAsync(entity.Id, CancellationToken)).Should().BeFalse();
    }

    [TestMethod]
    public void Equals_ForSameId_ReturnsTrueWithMatchingHashCode()
    {
        IServiceProvider services = GetServices();
        var repository = services.GetRequiredService<IRepository<ITestEntity>>();
        Guid id = Guid.NewGuid();
        DateTimeOffset created = DateTimeOffset.UtcNow.AddMinutes(-5);
        var a = new TestEntity(new ExistingEntityData(id, created, created, false), repository);
        var b = new TestEntity(new ExistingEntityData(id, created, created, false), repository);

        a.Should().Be(b);
        a.GetHashCode().Should().Be(b.GetHashCode());
    }

    [TestMethod]
    public void Equals_ForSameIdDifferentMetadata_IgnoresMetadataAndRemainsEqual()
    {
        IServiceProvider services = GetServices();
        var repository = services.GetRequiredService<IRepository<ITestEntity>>();
        Guid id = Guid.NewGuid();
        DateTimeOffset first = DateTimeOffset.UtcNow.AddMinutes(-10);
        DateTimeOffset second = DateTimeOffset.UtcNow.AddMinutes(-5);
        var a = new TestEntity(new ExistingEntityData(id, first, first, false), repository);
        var b = new TestEntity(new ExistingEntityData(id, second, second, true), repository);

        a.Should().Be(b);
        a.GetHashCode().Should().Be(b.GetHashCode());
    }

    [TestMethod]
    public void Equals_ForDifferentId_ReturnsFalse()
    {
        IServiceProvider services = GetServices();
        var repository = services.GetRequiredService<IRepository<ITestEntity>>();
        DateTimeOffset created = DateTimeOffset.UtcNow.AddMinutes(-5);
        var a = new TestEntity(new ExistingEntityData(Guid.NewGuid(), created, created, false), repository);
        var b = new TestEntity(new ExistingEntityData(Guid.NewGuid(), created, created, false), repository);

        a.Should().NotBe(b);
    }

    [TestMethod]
    public void Equals_ForNull_ReturnsFalse()
    {
        IServiceProvider services = GetServices();
        var repository = services.GetRequiredService<IRepository<ITestEntity>>();
        var entity = new TestEntity(repository);

        entity.Equals((object?)null).Should().BeFalse();
    }
}
