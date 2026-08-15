using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Nordstein.Core.Testing;

namespace Nordstein.Core.Storage.Tests;

/// <summary>
/// Adversarial coverage of the one <see cref="ArchivableRepository{TDomainEntity,TStoredEntity}"/>
/// branch the happy-path suite does not reach: unarchiving an id that is not present at all — the
/// <c>existing is null</c> half of the unarchive short-circuit guard, distinct from the
/// already-covered "exists but not archived" case. It must be a silent no-op that never creates a
/// row or emits a change notification.
/// </summary>
[TestClass]
public sealed class ArchivableRepositoryEdgeCaseTests : BaseTest<Module>
{
    [TestMethod]
    public async Task Unarchive_WhenIdIsMissing_IsASilentNoOp()
    {
        IServiceProvider services = GetServices();
        var repository = services.GetRequiredService<ITestDocRepository>();
        Guid ghost = Guid.NewGuid();

        // Hits the `existing is null` half of the unarchive guard — no throw, no resurrection.
        await repository.Unarchive(ghost, CancellationToken);

        (await repository.FindAsync(ghost, CancellationToken)).Should().BeNull();
        (await repository.GetAllAsync(CancellationToken)).Should().BeEmpty();
        (await repository.CountAsync(CancellationToken)).Should().Be(0);
    }
}
