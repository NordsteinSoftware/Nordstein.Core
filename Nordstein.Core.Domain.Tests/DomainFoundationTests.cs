using AwesomeAssertions;
using Autofac;
using Microsoft.Extensions.DependencyInjection;
using Nordstein.Core.Domain.Events;
using Nordstein.Core.Domain.Exceptions;
using Nordstein.Core.Domain.Paging;
using Nordstein.Core.Testing;

namespace Nordstein.Core.Domain.Tests;

[TestClass]
public sealed class DomainFoundationTests : BaseTest<Module>
{
    [TestMethod]
    public async Task Module_WithConsumerAssembly_DiscoversInternalEntityAndGenerator()
    {
        IServiceProvider services = GetServices();
        var generator = services.GetRequiredService<IDomainEntityGenerator<ITestEntity>>();

        ITestEntity generated = await generator.GenerateAsync(CancellationToken);
        ITestEntity persisted = await generator.CreateAsync(CancellationToken);

        generated.Id.Should().NotBe(Guid.Empty);
        (await services.GetRequiredService<IRepository<ITestEntity>>()
            .ContainsAsync(persisted.Id, CancellationToken)).Should().BeTrue();
    }

    [TestMethod]
    public void Module_WithoutConsumerAssembly_Throws()
    {
        Action action = () => new Domain.Module();
        action.Should().Throw<ArgumentException>();
    }

    [TestMethod]
    public void Module_WhenRegisteredTwice_RegistersServicesOnce()
    {
        var builder = new ContainerBuilder();
        builder.RegisterType<TestEntityRepository>().As<IRepository<ITestEntity>>().SingleInstance();
        builder.RegisterModule(new Domain.Module(typeof(Module).Assembly));
        builder.RegisterModule(new Domain.Module(typeof(Module).Assembly));
        using var container = builder.Build();

        container.Resolve<IEnumerable<IEntityEventService>>().Should().ContainSingle();
        container.Resolve<IEnumerable<IDomainEntityGenerator<ITestEntity>>>().Should().ContainSingle();
    }

    [TestMethod]
    public void Module_WithOverlappingAssemblySets_RegistersServicesOnce()
    {
        var builder = new ContainerBuilder();
        builder.RegisterType<TestEntityRepository>().As<IRepository<ITestEntity>>().SingleInstance();
        builder.RegisterModule(new Domain.Module(typeof(Module).Assembly));
        builder.RegisterModule(new Domain.Module(typeof(Module).Assembly, typeof(string).Assembly));
        using var container = builder.Build();

        container.Resolve<IEnumerable<IEntityEventService>>().Should().ContainSingle();
        container.Resolve<IEnumerable<IDomainEntityGenerator<ITestEntity>>>().Should().ContainSingle();
    }

    [TestMethod]
    public void DomainEntity_WithExistingData_PreservesMetadata()
    {
        IServiceProvider services = GetServices();
        var repository = services.GetRequiredService<IRepository<ITestEntity>>();
        var data = new ExistingEntityData(
            Guid.NewGuid(),
            DateTimeOffset.UtcNow.AddHours(-2),
            DateTimeOffset.UtcNow.AddHours(-1),
            true);

        var entity = new TestEntity(data, repository);

        entity.Should().Match<ITestEntity>(candidate =>
            candidate.Id == data.Id
            && candidate.CreatedAt == data.CreatedAt
            && candidate.UpdatedAt == data.UpdatedAt
            && candidate.IsArchived);
    }

    [TestMethod]
    public async Task RepositoryGetAsync_WhenMissing_ThrowsEntityNotFoundException()
    {
        IServiceProvider services = GetServices();
        var repository = services.GetRequiredService<IRepository<ITestEntity>>();

        await FluentActions.Invoking(() => repository.GetAsync(Guid.NewGuid(), CancellationToken))
            .Should().ThrowAsync<EntityNotFoundException>();
    }

    [TestMethod]
    public void Paging_ClampsInputsAndSaturatesOffset()
    {
        Nordstein.Core.Domain.Paging.Paging.Clamp(0, 1000).Should().Be((1, 100));
        Nordstein.Core.Domain.Paging.Paging.Offset(int.MaxValue, 100).Should().Be(int.MaxValue);
    }

    [TestMethod]
    public async Task EntityEvents_FilterAndCompleteOnCancellation()
    {
        IServiceProvider services = GetServices();
        var events = services.GetRequiredService<IEntityEventService>();
        using var cancellation = new CancellationTokenSource();
        var reader = events.Subscribe(cancellation.Token, typeof(ITestEntity));
        var expected = new EntityChangedEvent(Guid.NewGuid(), typeof(ITestEntity), EntityChangeType.Added);

        events.Notify(new EntityChangedEvent(Guid.NewGuid(), typeof(string), EntityChangeType.Added));
        events.Notify(expected);

        (await reader.ReadAsync(CancellationToken)).Should().Be(expected);
        cancellation.Cancel();
        await reader.Completion;
    }
}
