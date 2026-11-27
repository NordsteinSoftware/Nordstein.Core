using System.ComponentModel.DataAnnotations;
using AwesomeAssertions;

namespace Nordstein.Core.Storage.Tests;

/// <summary>
/// The stored-entity base validates its identity/timestamp trio.
/// </summary>
[TestClass]
public sealed class EntityTests
{
    [TestMethod]
    public void Validate_WithAValidEntity_YieldsNoFailures()
    {
        DateTimeOffset past = DateTimeOffset.UtcNow.AddMinutes(-1);
        var entity = new TestThingEntity { Id = Guid.NewGuid(), CreatedAt = past, UpdatedAt = past, Name = "ok" };

        IEnumerable<ValidationResult> results = entity.Validate(new ValidationContext(entity));

        results.Where(r => r != ValidationResult.Success).Should().BeEmpty();
    }

    [TestMethod]
    public void Validate_WithDefaultIdAndTimestamps_YieldsFailures()
    {
        var entity = new TestThingEntity { Id = Guid.Empty, CreatedAt = default, UpdatedAt = default, Name = "bad" };

        IEnumerable<ValidationResult> results = entity.Validate(new ValidationContext(entity));

        results.Where(r => r != ValidationResult.Success).Should().HaveCount(3);
    }

    [TestMethod]
    public void Validate_WithFutureTimestamps_YieldsNoFailure()
    {
        // A stored row has no clock: whether a stamp lies in the future is the domain entity's check.
        DateTimeOffset future = DateTimeOffset.UtcNow.AddYears(5);
        var entity = new TestThingEntity { Id = Guid.NewGuid(), CreatedAt = future, UpdatedAt = future, Name = "ahead" };

        IEnumerable<ValidationResult> results = entity.Validate(new ValidationContext(entity));

        results.Where(r => r != ValidationResult.Success).Should().BeEmpty();
    }

    [TestMethod]
    public void Validate_WithUpdatedAtBeforeCreatedAt_YieldsOneFailure()
    {
        DateTimeOffset created = DateTimeOffset.UtcNow.AddMinutes(-1);
        var entity = new TestThingEntity { Id = Guid.NewGuid(), CreatedAt = created, UpdatedAt = created.AddSeconds(-1), Name = "reversed" };

        IEnumerable<ValidationResult> results = entity.Validate(new ValidationContext(entity));

        results.Where(r => r != ValidationResult.Success).Should().ContainSingle()
            .Which.MemberNames.Should().Equal("UpdatedAt");
    }
}
