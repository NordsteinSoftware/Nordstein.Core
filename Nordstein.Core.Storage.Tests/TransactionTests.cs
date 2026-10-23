using Autofac;
using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Nordstein.Core.Domain;
using Nordstein.Core.Testing;

namespace Nordstein.Core.Storage.Tests;

/// <summary>
/// The ambient-transaction seam (<see cref="ITransaction"/> + <see cref="AmbientDbContext"/>):
/// commit, exception propagation with cleanup, nested calls sharing the outer unit, and the
/// cancellation contract (honored before and during the operation, never during commit).
/// </summary>
[TestClass]
public sealed class TransactionTests : BaseTest<Module>
{
    [TestMethod]
    public async Task InvokeAsync_Commits_AndReturnsTheResult_AndClearsAmbientState()
    {
        IServiceProvider services = GetServices();
        var transaction = services.GetRequiredService<ITransaction>();

        transaction.IsActive.Should().BeFalse();
        int result = await transaction.InvokeAsync(() => Task.FromResult(42), CancellationToken);

        result.Should().Be(42);
        transaction.IsActive.Should().BeFalse();
    }

    [TestMethod]
    public async Task InvokeAsync_VoidOverload_Runs()
    {
        IServiceProvider services = GetServices();
        var transaction = services.GetRequiredService<ITransaction>();

        var ran = false;
        await transaction.InvokeAsync(() =>
        {
            ran = true;
            return Task.CompletedTask;
        }, CancellationToken);

        ran.Should().BeTrue();
        transaction.IsActive.Should().BeFalse();
    }

    [TestMethod]
    public async Task InvokeAsync_WhenTheOperationThrows_PropagatesAndClearsAmbientState()
    {
        IServiceProvider services = GetServices();
        var transaction = services.GetRequiredService<ITransaction>();

        await FluentActions
            .Invoking(() => transaction.InvokeAsync<int>(
                () => throw new InvalidOperationException("boom"), CancellationToken))
            .Should().ThrowAsync<InvalidOperationException>();

        // The rollback path ran and the ambient context was cleared in the finally.
        transaction.IsActive.Should().BeFalse();
    }

    [TestMethod]
    public async Task InvokeAsync_WhenNested_SharesTheOuterUnit()
    {
        IServiceProvider services = GetServices();
        var transaction = services.GetRequiredService<ITransaction>();
        var ambient = services.GetRequiredService<AmbientDbContext>();

        bool innerActive = false;
        object? outerContext = null;
        object? innerContext = null;

        bool hadTransaction = false;
        await transaction.InvokeAsync(async () =>
        {
            outerContext = ambient.Context;
            hadTransaction = ambient.Transaction is not null;
            await transaction.InvokeAsync(() =>
            {
                innerActive = transaction.IsActive;
                innerContext = ambient.Context;
                return Task.CompletedTask;
            }, CancellationToken);
        }, CancellationToken);

        innerActive.Should().BeTrue();
        hadTransaction.Should().BeTrue(); // the ambient EF transaction is exposed while active
        innerContext.Should().BeSameAs(outerContext); // one shared context/connection
        transaction.IsActive.Should().BeFalse();
    }

    [TestMethod]
    public async Task InvokeAsync_WhenTheTokenIsCancelledDuringTheOperation_StillCommitsAndReturnsTheResult()
    {
        // Regression for #19: the documented contract honors the token during the operation but
        // not during commit. A commit aborted by a late cancellation can surface as a failure for
        // a transaction the server did commit.
        IServiceProvider services = GetServices(UseTokenHonoringTransactions);
        var transaction = services.GetRequiredService<ITransaction>();
        var ambient = services.GetRequiredService<AmbientDbContext>();
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(CancellationToken);

        FakeTransaction? efTransaction = null;
        int result = await transaction.InvokeAsync(() =>
        {
            efTransaction = ambient.Transaction as FakeTransaction;
            cts.Cancel(); // the work is done; cancellation lands just before COMMIT
            return Task.FromResult(42);
        }, cts.Token);

        result.Should().Be(42);
        Assert.IsNotNull(efTransaction);
        efTransaction.Committed.Should().BeTrue();
        efTransaction.RolledBack.Should().BeFalse();
        transaction.IsActive.Should().BeFalse();
    }

    [TestMethod]
    public async Task InvokeAsync_VoidOverload_WhenTheTokenIsCancelledDuringTheOperation_StillCommits()
    {
        IServiceProvider services = GetServices(UseTokenHonoringTransactions);
        var transaction = services.GetRequiredService<ITransaction>();
        var ambient = services.GetRequiredService<AmbientDbContext>();
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(CancellationToken);

        FakeTransaction? efTransaction = null;
        await transaction.InvokeAsync(() =>
        {
            efTransaction = ambient.Transaction as FakeTransaction;
            cts.Cancel();
            return Task.CompletedTask;
        }, cts.Token);

        Assert.IsNotNull(efTransaction);
        efTransaction.Committed.Should().BeTrue();
        transaction.IsActive.Should().BeFalse();
    }

    [TestMethod]
    public async Task InvokeAsync_WhenTheOperationIsCancelledAndThrows_RollsBackAndPropagatesTheOriginalException()
    {
        // Rollback, too, runs uncancelled: the operation's own exception must reach the caller,
        // not an OperationCanceledException from an aborted rollback.
        IServiceProvider services = GetServices(UseTokenHonoringTransactions);
        var transaction = services.GetRequiredService<ITransaction>();
        var ambient = services.GetRequiredService<AmbientDbContext>();
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(CancellationToken);

        FakeTransaction? efTransaction = null;
        await FluentActions
            .Invoking(() => transaction.InvokeAsync<int>(() =>
            {
                efTransaction = ambient.Transaction as FakeTransaction;
                cts.Cancel();
                throw new InvalidOperationException("boom");
            }, cts.Token))
            .Should().ThrowExactlyAsync<InvalidOperationException>().WithMessage("boom");

        Assert.IsNotNull(efTransaction);
        efTransaction.RolledBack.Should().BeTrue();
        efTransaction.Committed.Should().BeFalse();
        transaction.IsActive.Should().BeFalse();
    }

    [TestMethod]
    public async Task InvokeAsync_WhenTheTokenIsAlreadyCancelled_ThrowsWithoutRunningTheOperation()
    {
        IServiceProvider services = GetServices(UseTokenHonoringTransactions);
        var transaction = services.GetRequiredService<ITransaction>();
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(CancellationToken);
        await cts.CancelAsync();

        var ran = false;
        await FluentActions
            .Invoking(() => transaction.InvokeAsync(() =>
            {
                ran = true;
                return Task.FromResult(42);
            }, cts.Token))
            .Should().ThrowAsync<OperationCanceledException>();

        ran.Should().BeFalse();
        transaction.IsActive.Should().BeFalse();
    }

    /// <summary>
    /// The in-memory provider ignores tokens on begin and commit, so these tests swap in a
    /// transaction manager that behaves like a relational provider: it throws when the token it is
    /// handed is cancelled.
    /// </summary>
    private static void UseTokenHonoringTransactions(ContainerBuilder builder)
        => builder.Register<DbContextOptions<TestDbContext>>(_ => new DbContextOptionsBuilder<TestDbContext>()
                .UseInMemoryDatabase("core-storage-tests-" + Guid.NewGuid())
                .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
                .ReplaceService<IDbContextTransactionManager, TokenHonoringTransactionManager>()
                .Options)
            .SingleInstance();

    private sealed class TokenHonoringTransactionManager : IDbContextTransactionManager
    {
        public IDbContextTransaction? CurrentTransaction { get; private set; }

        public IDbContextTransaction BeginTransaction() => CurrentTransaction = new FakeTransaction();

        public Task<IDbContextTransaction> BeginTransactionAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(BeginTransaction());
        }

        public void CommitTransaction() => Current.Commit();

        public void RollbackTransaction() => Current.Rollback();

        public Task CommitTransactionAsync(CancellationToken cancellationToken = default)
            => Current.CommitAsync(cancellationToken);

        public Task RollbackTransactionAsync(CancellationToken cancellationToken = default)
            => Current.RollbackAsync(cancellationToken);

        public void ResetState() => CurrentTransaction = null;

        public Task ResetStateAsync(CancellationToken cancellationToken = default)
        {
            ResetState();
            return Task.CompletedTask;
        }

        private IDbContextTransaction Current
            => CurrentTransaction ?? throw new InvalidOperationException("No transaction has been started.");
    }

    private sealed class FakeTransaction : IDbContextTransaction
    {
        public bool Committed { get; private set; }

        public bool RolledBack { get; private set; }

        public Guid TransactionId { get; } = Guid.NewGuid();

        public void Commit() => Committed = true;

        public void Rollback() => RolledBack = true;

        public Task CommitAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Commit();
            return Task.CompletedTask;
        }

        public Task RollbackAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Rollback();
            return Task.CompletedTask;
        }

        public void Dispose()
        {
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
