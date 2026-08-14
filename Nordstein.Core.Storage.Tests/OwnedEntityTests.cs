using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Nordstein.Core.Domain;
using Nordstein.Core.Testing;

namespace Nordstein.Core.Storage.Tests;

/// <summary>
/// Updates must rewrite EF owned navigations (which <c>SetValues</c> does not touch) — covering the
/// three cases in <c>AbstractRepository.UpdateOwnedEntities</c>: replace, clear, and add.
/// </summary>
[TestClass]
public sealed class OwnedEntityTests : BaseTest<Module>
{
    private static IRepository<ITestOwner> Repo(IServiceProvider services)
        => services.GetRequiredService<IRepository<ITestOwner>>();

    private static ITestOwner NewOwner(string label, string? note)
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        return new TestOwner { Id = Guid.NewGuid(), CreatedAt = now, UpdatedAt = now, Label = label, Note = note };
    }

    private static ITestOwner With(ITestOwner owner, string? note)
        => new TestOwner { Id = owner.Id, CreatedAt = owner.CreatedAt, UpdatedAt = owner.UpdatedAt, Label = owner.Label, Note = note };

    [TestMethod]
    public async Task Update_ReplacesAnExistingOwnedValue()
    {
        IServiceProvider services = GetServices();
        IRepository<ITestOwner> repository = Repo(services);
        ITestOwner owner = await repository.AddAsync(NewOwner("o", "first"), CancellationToken);

        await repository.UpdateAsync(With(owner, "second"), CancellationToken);

        (await repository.FindAsync(owner.Id, CancellationToken))!.Note.Should().Be("second");
    }

    [TestMethod]
    public async Task Update_ClearsAnOwnedValue()
    {
        IServiceProvider services = GetServices();
        IRepository<ITestOwner> repository = Repo(services);
        ITestOwner owner = await repository.AddAsync(NewOwner("o", "present"), CancellationToken);

        await repository.UpdateAsync(With(owner, null), CancellationToken);

        (await repository.FindAsync(owner.Id, CancellationToken))!.Note.Should().BeNull();
    }

    [TestMethod]
    public async Task Update_AddsAnOwnedValueWhereThereWasNone()
    {
        IServiceProvider services = GetServices();
        IRepository<ITestOwner> repository = Repo(services);
        ITestOwner owner = await repository.AddAsync(NewOwner("o", note: null), CancellationToken);

        await repository.UpdateAsync(With(owner, "added"), CancellationToken);

        (await repository.FindAsync(owner.Id, CancellationToken))!.Note.Should().Be("added");
    }
}
