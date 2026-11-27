using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Nordstein.Core.Common.Time;
using Nordstein.Core.Domain;
using Nordstein.Core.Domain.Events;
using Nordstein.Core.Testing;

namespace Nordstein.Core.Storage.Tests;

/// <summary>
/// Every repository write hands the caller's cancellation token to the transaction seam, so a
/// cancelled caller is refused when the transaction begins, not only once the operation reaches the
/// database. The archive writes already did; these pin the six writes of the base repository.
/// </summary>
[TestClass]
public sealed class AbstractRepositoryCancellationTests : BaseTest<Module>
{
    // A token of its own, distinct from CancellationToken.None and from the test's token, so an
    // assertion cannot pass on a default.
    private readonly CancellationTokenSource callerSource = new();

    [TestCleanup]
    public void DisposeCallerSource() => callerSource.Dispose();

    [TestMethod]
    public async Task AddAsync_PassesTheCallersTokenToTheTransaction()
    {
        (TestThingRepository repository, RecordingTransaction transaction, CancellationToken token) = Arrange();

        await repository.AddAsync(NewThing("a"), token);

        transaction.Tokens.Should().Equal(token);
    }

    [TestMethod]
    public async Task AddRangeAsync_PassesTheCallersTokenToTheTransaction()
    {
        (TestThingRepository repository, RecordingTransaction transaction, CancellationToken token) = Arrange();

        await repository.AddRangeAsync([NewThing("a"), NewThing("b")], token);

        transaction.Tokens.Should().Equal(token);
    }

    [TestMethod]
    public async Task UpsertAsync_PassesTheCallersTokenToTheTransaction()
    {
        (TestThingRepository repository, RecordingTransaction transaction, CancellationToken token) = Arrange();

        await repository.UpsertAsync(NewThing("a"), token);

        transaction.Tokens.Should().Equal(token);
    }

    [TestMethod]
    public async Task UpdateAsync_PassesTheCallersTokenToTheTransaction()
    {
        (TestThingRepository repository, RecordingTransaction transaction, CancellationToken token) = Arrange();
        ITestThing added = await repository.AddAsync(NewThing("a"), CancellationToken);
        transaction.Tokens.Clear();

        await repository.UpdateAsync(added, token);

        transaction.Tokens.Should().Equal(token);
    }

    [TestMethod]
    public async Task RemoveAsync_PassesTheCallersTokenToTheTransaction()
    {
        (TestThingRepository repository, RecordingTransaction transaction, CancellationToken token) = Arrange();

        await repository.RemoveAsync(Guid.NewGuid(), token);

        transaction.Tokens.Should().Equal(token);
    }

    [TestMethod]
    public async Task RemoveAllAsync_PassesTheCallersTokenToTheTransaction()
    {
        (TestThingRepository repository, RecordingTransaction transaction, CancellationToken token) = Arrange();

        await repository.RemoveAllAsync(token);

        transaction.Tokens.Should().Equal(token);
    }

    private (TestThingRepository Repository, RecordingTransaction Transaction, CancellationToken Token) Arrange()
    {
        IServiceProvider services = GetServices();
        var transaction = new RecordingTransaction(services.GetRequiredService<ITransaction>());
        var repository = new TestThingRepository(
            services.GetRequiredService<IMapper<ITestThing, TestThingEntity>>(),
            services.GetRequiredService<Func<DbContext>>(),
            transaction,
            services.GetRequiredService<IEntityEventService>(),
            services.GetRequiredService<AmbientDbContext>(),
            services.GetRequiredService<IClock>());
        return (repository, transaction, callerSource.Token);
    }

    private static ITestThing NewThing(string name)
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        return new TestThing { Id = Guid.NewGuid(), CreatedAt = now, UpdatedAt = now, Name = name };
    }

    /// <summary>Runs every call on the real transaction and records the token it was given.</summary>
    private sealed class RecordingTransaction : ITransaction
    {
        private readonly ITransaction inner;

        public RecordingTransaction(ITransaction inner)
        {
            this.inner = inner;
        }

        public List<CancellationToken> Tokens { get; } = [];

        public bool IsActive => inner.IsActive;

        public Task<TResult> InvokeAsync<TResult>(Func<Task<TResult>> operation, CancellationToken cancellationToken = default)
        {
            Tokens.Add(cancellationToken);
            return inner.InvokeAsync(operation, cancellationToken);
        }

        public Task InvokeAsync(Func<Task> operation, CancellationToken cancellationToken = default)
        {
            Tokens.Add(cancellationToken);
            return inner.InvokeAsync(operation, cancellationToken);
        }
    }
}
