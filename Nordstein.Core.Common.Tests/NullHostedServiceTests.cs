using AwesomeAssertions;
using Nordstein.Core.Common.Hosting;

namespace Nordstein.Core.Common.Tests;

[TestClass]
public sealed class NullHostedServiceTests
{
    [TestMethod]
    public async Task StartAsync_CompletesSuccessfully()
    {
        var service = new NullHostedService();

        Task task = service.StartAsync(CancellationToken.None);

        task.IsCompletedSuccessfully.Should().BeTrue();
        await task;
    }

    [TestMethod]
    public async Task StopAsync_CompletesSuccessfully()
    {
        var service = new NullHostedService();

        Task task = service.StopAsync(CancellationToken.None);

        task.IsCompletedSuccessfully.Should().BeTrue();
        await task;
    }

    [TestMethod]
    public async Task StartAsync_WithAlreadyCancelledToken_StillCompletes()
    {
        // The no-op service ignores the token — it never observes cancellation.
        var service = new NullHostedService();
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        Task task = service.StartAsync(cts.Token);

        task.IsCompletedSuccessfully.Should().BeTrue();
        await task;
    }

    [TestMethod]
    public async Task StopAsync_WithAlreadyCancelledToken_StillCompletes()
    {
        var service = new NullHostedService();
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        Task task = service.StopAsync(cts.Token);

        task.IsCompletedSuccessfully.Should().BeTrue();
        await task;
    }
}
