using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Nordstein.Core.Testing;

namespace Nordstein.Core.Domain.Tests;

[TestClass]
public sealed class DomainEntityGeneratorGetOrCreateTests : BaseTest<Module>
{
    [TestMethod]
    public async Task GetOrCreateAsync_WhenRepositoryEmpty_CreatesAndPersistsEntity()
    {
        IServiceProvider services = GetServices();
        var generator = services.GetRequiredService<IDomainEntityGenerator<ITestEntity>>();
        var repository = services.GetRequiredService<IRepository<ITestEntity>>();

        ITestEntity created = await generator.GetOrCreateAsync(CancellationToken);

        (await repository.ContainsAsync(created.Id, CancellationToken)).Should().BeTrue();
        (await repository.CountAsync(CancellationToken)).Should().Be(1);
    }

    [TestMethod]
    public async Task GetOrCreateAsync_WhenEntityExists_ReturnsExistingWithoutCreatingAnother()
    {
        IServiceProvider services = GetServices();
        var generator = services.GetRequiredService<IDomainEntityGenerator<ITestEntity>>();
        var repository = services.GetRequiredService<IRepository<ITestEntity>>();

        ITestEntity first = await generator.GetOrCreateAsync(CancellationToken);
        ITestEntity second = await generator.GetOrCreateAsync(CancellationToken);

        second.Id.Should().Be(first.Id);
        (await repository.CountAsync(CancellationToken)).Should().Be(1);
    }
}
