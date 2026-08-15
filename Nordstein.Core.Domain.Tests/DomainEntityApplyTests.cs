using System.ComponentModel.DataAnnotations;
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Nordstein.Core.Testing;

namespace Nordstein.Core.Domain.Tests;

[TestClass]
public sealed class DomainEntityApplyTests : BaseTest<Module>
{
    [TestMethod]
    public async Task ApplyAsync_WithValidEntity_UpdatesThroughRepository()
    {
        IServiceProvider services = GetServices();
        var repository = services.GetRequiredService<IRepository<ITestEntity>>();
        var probe = new ProbeEntity(repository);
        Guid id = Guid.NewGuid();
        DateTimeOffset past = DateTimeOffset.UtcNow.AddMinutes(-5);
        var original = new TestEntity(new ExistingEntityData(id, past, past, false), repository);
        var replacement = new TestEntity(new ExistingEntityData(id, past, past, false), repository);
        await repository.AddAsync(original, CancellationToken);

        ITestEntity result = await probe.InvokeApplyAsync(replacement, CancellationToken);

        // `original` and `replacement` are equal by identity (same Id) but are distinct references,
        // so only reference comparison can tell whether the repository was actually written. If
        // ApplyAsync stopped routing through repository.UpdateAsync, the store would still hold
        // `original` and FindAsync would return it, failing this assertion.
        result.Should().BeSameAs(replacement);
        (await repository.FindAsync(id, CancellationToken)).Should().BeSameAs(replacement);
    }

    [TestMethod]
    public async Task ApplyAsync_WithInvalidEntity_ThrowsBeforeUpdating()
    {
        IServiceProvider services = GetServices();
        var repository = services.GetRequiredService<IRepository<ITestEntity>>();
        var probe = new ProbeEntity(repository);
        DateTimeOffset past = DateTimeOffset.UtcNow.AddMinutes(-5);
        var invalid = new TestEntity(new ExistingEntityData(Guid.Empty, past, past, false), repository);

        await FluentActions.Invoking(() => probe.InvokeApplyAsync(invalid, CancellationToken))
            .Should().ThrowAsync<ValidationException>();

        (await repository.ContainsAsync(Guid.Empty, CancellationToken)).Should().BeFalse();
    }
}
