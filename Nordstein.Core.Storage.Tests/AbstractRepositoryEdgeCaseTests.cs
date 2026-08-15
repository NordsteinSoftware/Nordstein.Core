using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Nordstein.Core.Domain;
using Nordstein.Core.Domain.Exceptions;
using Nordstein.Core.Domain.Paging;
using Nordstein.Core.Testing;

namespace Nordstein.Core.Storage.Tests;

/// <summary>
/// Adversarial coverage of the unhappy branches of
/// <see cref="AbstractRepository{TDomainEntity,TStoredEntity}"/> that the happy-path suite does not
/// reach: the already-exists guard on the single-entity add, the in-memory optimistic-concurrency
/// pre-check, empty/duplicate <c>GetMany</c> inputs, a page past the end, an empty
/// <c>RemoveAll</c>, and cancellation observed before the operation runs.
/// </summary>
[TestClass]
public sealed class AbstractRepositoryEdgeCaseTests : BaseTest<Module>
{
    // A deliberately old timestamp: it becomes the initial concurrency token, so an update that
    // bumps UpdatedAt to "now" is unambiguously a different (later) version — no reliance on the
    // wall clock ticking between two rapid calls.
    private static readonly DateTimeOffset Epoch = new(2020, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private static IRepository<ITestThing> Repo(IServiceProvider services)
        => services.GetRequiredService<IRepository<ITestThing>>();

    private static ITestThing NewThing(string name, DateTimeOffset? stamp = null)
    {
        DateTimeOffset now = stamp ?? DateTimeOffset.UtcNow;
        return new TestThing { Id = Guid.NewGuid(), CreatedAt = now, UpdatedAt = now, Name = name };
    }

    private static ITestThing WithToken(ITestThing thing, string name, DateTimeOffset token)
        => new TestThing { Id = thing.Id, CreatedAt = thing.CreatedAt, UpdatedAt = token, Name = name };

    [TestMethod]
    public async Task AddAsync_WhenIdAlreadyExists_Throws()
    {
        IServiceProvider services = GetServices();
        IRepository<ITestThing> repository = Repo(services);
        ITestThing existing = await repository.AddAsync(NewThing("first"), CancellationToken);

        // A second add of the same id hits the in-context existence guard in AddCoreAsync.
        ITestThing duplicate = WithToken(existing, "second", existing.UpdatedAt);

        await FluentActions
            .Invoking(() => repository.AddAsync(duplicate, CancellationToken))
            .Should().ThrowAsync<EntityAlreadyExistsException>();

        // The original row is untouched.
        (await repository.GetAsync(existing.Id, CancellationToken)).Name.Should().Be("first");
        (await repository.CountAsync(CancellationToken)).Should().Be(1);
    }

    [TestMethod]
    public async Task UpdateAsync_WithStaleConcurrencyToken_ThrowsOptimisticConcurrency()
    {
        IServiceProvider services = GetServices();
        IRepository<ITestThing> repository = Repo(services);
        ITestThing added = await repository.AddAsync(NewThing("v1", Epoch), CancellationToken);

        // First update succeeds and advances the concurrency token from Epoch to "now".
        await repository.UpdateAsync(WithToken(added, "v2", added.UpdatedAt), CancellationToken);

        // Re-using the original (now stale) token must fail the in-app pre-check before any DB save.
        await FluentActions
            .Invoking(() => repository.UpdateAsync(WithToken(added, "v3", Epoch), CancellationToken))
            .Should().ThrowAsync<OptimisticConcurrencyException>();

        // The losing writer's value never landed.
        (await repository.GetAsync(added.Id, CancellationToken)).Name.Should().Be("v2");
    }

    [TestMethod]
    public async Task UpsertAsync_WithStaleConcurrencyToken_ThrowsOptimisticConcurrency()
    {
        IServiceProvider services = GetServices();
        IRepository<ITestThing> repository = Repo(services);
        ITestThing inserted = await repository.UpsertAsync(NewThing("v1", Epoch), CancellationToken);

        // Advance the token via a legitimate upsert-update.
        await repository.UpsertAsync(WithToken(inserted, "v2", inserted.UpdatedAt), CancellationToken);

        // Upsert routes an existing id through UpdateCoreAsync, so the stale token conflicts there too.
        await FluentActions
            .Invoking(() => repository.UpsertAsync(WithToken(inserted, "v3", Epoch), CancellationToken))
            .Should().ThrowAsync<OptimisticConcurrencyException>();
    }

    [TestMethod]
    public async Task GetManyAsync_WithEmptyInput_ReturnsEmpty()
    {
        IServiceProvider services = GetServices();
        IRepository<ITestThing> repository = Repo(services);
        await repository.AddAsync(NewThing("a"), CancellationToken);

        IReadOnlyList<ITestThing> many = await repository.GetManyAsync([], cancellationToken: CancellationToken);

        many.Should().BeEmpty();
    }

    [TestMethod]
    public async Task GetManyAsync_WithDuplicateIds_DeduplicatesToOneRow()
    {
        IServiceProvider services = GetServices();
        IRepository<ITestThing> repository = Repo(services);
        ITestThing a = await repository.AddAsync(NewThing("a"), CancellationToken);

        // Distinct() collapses the repeated id; the result carries the entity exactly once and the
        // missing-count guard (count == distinct count) does not spuriously trip.
        IReadOnlyList<ITestThing> many = await repository.GetManyAsync(
            [a.Id, a.Id, a.Id], cancellationToken: CancellationToken);

        many.Should().ContainSingle();
        many[0].Name.Should().Be("a");
    }

    [TestMethod]
    public async Task GetManyAsync_WithEmptyInputAndIgnoreMissing_ReturnsEmpty()
    {
        IServiceProvider services = GetServices();
        IRepository<ITestThing> repository = Repo(services);

        IReadOnlyList<ITestThing> many = await repository.GetManyAsync(
            [], ignoreMissing: true, CancellationToken);

        many.Should().BeEmpty();
    }

    [TestMethod]
    public async Task GetPagedAsync_PagePastTheEnd_ReturnsEmptyPageWithTrueTotal()
    {
        IServiceProvider services = GetServices();
        IRepository<ITestThing> repository = Repo(services);
        await repository.AddAsync(NewThing("only"), CancellationToken);

        // Page 3 of a single-row table: the offset skips past every row, but the total still counts it.
        PagedResult<ITestThing> page = await repository.GetPagedAsync(3, 2, CancellationToken);

        page.Items.Should().BeEmpty();
        page.Total.Should().Be(1);
        page.Page.Should().Be(3);
        page.PageSize.Should().Be(2);
    }

    [TestMethod]
    public async Task RemoveAllAsync_OnEmptyTable_IsANoOp()
    {
        IServiceProvider services = GetServices();
        IRepository<ITestThing> repository = Repo(services);

        await repository.RemoveAllAsync(CancellationToken);

        (await repository.CountAsync(CancellationToken)).Should().Be(0);
    }

    [TestMethod]
    public async Task CountAsync_WithAlreadyCancelledToken_ThrowsOperationCanceled()
    {
        IServiceProvider services = GetServices();
        IRepository<ITestThing> repository = Repo(services);
        await repository.AddAsync(NewThing("a"), CancellationToken);

        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await FluentActions
            .Invoking(() => repository.CountAsync(cts.Token))
            .Should().ThrowAsync<OperationCanceledException>();
    }
}
