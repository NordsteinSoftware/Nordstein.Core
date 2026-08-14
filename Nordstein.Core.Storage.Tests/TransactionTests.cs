using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Nordstein.Core.Domain;
using Nordstein.Core.Testing;

namespace Nordstein.Core.Storage.Tests;

/// <summary>
/// The ambient-transaction seam (<see cref="ITransaction"/> + <see cref="AmbientDbContext"/>):
/// commit, exception propagation with cleanup, and nested calls sharing the outer unit.
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
}
