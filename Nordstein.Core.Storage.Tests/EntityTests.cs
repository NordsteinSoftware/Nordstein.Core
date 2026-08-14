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
    public void Validate_WithDefaultIdAndFutureTimestamps_YieldsFailures()
    {
        DateTimeOffset future = DateTimeOffset.UtcNow.AddHours(1);
        var entity = new TestThingEntity { Id = Guid.Empty, CreatedAt = future, UpdatedAt = future, Name = "bad" };

        IEnumerable<ValidationResult> results = entity.Validate(new ValidationContext(entity));

        results.Where(r => r != ValidationResult.Success).Should().NotBeEmpty();
    }
}
