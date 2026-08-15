using System.ComponentModel.DataAnnotations;
using Autofac;
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Nordstein.Core.Testing;

namespace Nordstein.Core.Domain.Tests;

[TestClass]
public sealed class ModuleDiscoveryTests : BaseTest<Module>
{
    [TestMethod]
    public void Module_ResolvingDiscoveredEntity_ResolvesFromAssemblyScan()
    {
        IServiceProvider services = GetServices();

        var entity = services.GetRequiredService<ITestEntity>();

        entity.Id.Should().NotBe(Guid.Empty);
    }

    [TestMethod]
    public void Module_ResolvingEntityFailingActivationValidation_Throws()
    {
        // The module registers each discovered entity with an OnActivated hook that runs
        // Validator.ValidateObject on the resolved instance. Resolving an entity whose Validate
        // always fails must therefore throw, with the ValidationException surfaced in the chain
        // (Autofac wraps activation failures in a DependencyResolutionException). If the hook were
        // removed, this resolution would succeed and the test would fail.
        IServiceProvider services = GetServices(builder =>
            builder.RegisterInstance(Substitute.For<IRepository<IAlwaysInvalidEntity>>())
                .As<IRepository<IAlwaysInvalidEntity>>());

        var act = () => services.GetRequiredService<IAlwaysInvalidEntity>();

        act.Should().Throw<Exception>().Where(exception => ContainsValidationException(exception));
    }

    [TestMethod]
    public async Task Module_DiscoversDomainObjectGenerator_ResolvesAndCreates()
    {
        IServiceProvider services = GetServices();

        var generator = services.GetRequiredService<IDomainObjectGenerator<ITestValueObject>>();
        ITestValueObject created = await generator.CreateAsync(CancellationToken);

        created.Should().NotBeNull();
    }

    private static bool ContainsValidationException(Exception exception)
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            if (current is ValidationException)
            {
                return true;
            }
        }

        return false;
    }
}
