using AwesomeAssertions;
using Autofac;
using Microsoft.Extensions.DependencyInjection;
using Nordstein.Core.Domain.Events;
using Nordstein.Core.Testing;

namespace Nordstein.Core.Domain.Tests;

[TestClass]
public sealed class EntityEventServiceTests : BaseTest<Module>
{
    [TestMethod]
    public void Subscribe_WithNonCancellableToken_ThrowsArgumentException()
    {
        IServiceProvider services = GetServices();
        var events = services.GetRequiredService<IEntityEventService>();

        FluentActions.Invoking(() => events.Subscribe(CancellationToken.None))
            .Should().Throw<ArgumentException>()
            .Which.ParamName.Should().Be("cancellationToken");
    }

    [TestMethod]
    public void Notify_WithNoSubscribers_DoesNotThrow()
    {
        IServiceProvider services = GetServices();
        var events = services.GetRequiredService<IEntityEventService>();

        FluentActions.Invoking(() =>
                events.Notify(new EntityChangedEvent(Guid.NewGuid(), typeof(ITestEntity), EntityChangeType.Added)))
            .Should().NotThrow();
    }

    [TestMethod]
    public async Task Notify_WithMultipleUnfilteredSubscribers_DeliversToAll()
    {
        IServiceProvider services = GetServices();
        var events = services.GetRequiredService<IEntityEventService>();
        using var cancellation = new CancellationTokenSource();
        var first = events.Subscribe(cancellation.Token);
        var second = events.Subscribe(cancellation.Token);
        var expected = new EntityChangedEvent(Guid.NewGuid(), typeof(ITestEntity), EntityChangeType.Updated);

        events.Notify(expected);

        (await first.ReadAsync(CancellationToken)).Should().Be(expected);
        (await second.ReadAsync(CancellationToken)).Should().Be(expected);
    }

    [TestMethod]
    public void Dispose_WithActiveSubscriber_CompletesReader()
    {
        var builder = new ContainerBuilder();
        builder.RegisterType<TestEntityRepository>().As<IRepository<ITestEntity>>().SingleInstance();
        builder.RegisterModule(new Domain.Module(typeof(Module).Assembly));
        var container = builder.Build();
        var events = container.Resolve<IEntityEventService>();
        using var cancellation = new CancellationTokenSource();
        var reader = events.Subscribe(cancellation.Token);

        container.Dispose();

        reader.Completion.IsCompleted.Should().BeTrue();
    }
}
