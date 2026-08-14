using Autofac;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using NSubstitute;
using Nordstein.Core.Domain.Events;

namespace Nordstein.Core.Storage.Tests;

/// <summary>
/// Wires the storage foundation over an in-memory <see cref="TestDbContext"/> for the tests: the
/// discovery module, an explicit mapper registration, the in-memory provider options, and a stub
/// <see cref="IEntityEventService"/> (the foundation depends on it; the domain event pipeline is not
/// under test here).
/// </summary>
public sealed class Module : Autofac.Module
{
    protected override void Load(ContainerBuilder builder)
    {
        base.Load(builder);

        builder.RegisterInstance(Substitute.For<IEntityEventService>())
            .As<IEntityEventService>()
            .SingleInstance();

        builder.RegisterType<TestThingMapper>()
            .As<IMapper<ITestThing, TestThingEntity>>()
            .InstancePerDependency();

        builder.RegisterType<TestCachedThingMapper>()
            .As<IMapper<ITestCachedThing, TestCachedThingEntity>>()
            .InstancePerDependency();

        builder.RegisterType<TestDocMapper>()
            .As<IMapper<ITestDoc, TestDocEntity>>()
            .InstancePerDependency();

        builder.RegisterType<TestLockedDocMapper>()
            .As<IMapper<ITestLockedDoc, TestLockedDocEntity>>()
            .InstancePerDependency();

        builder.RegisterType<TestOwnerMapper>()
            .As<IMapper<ITestOwner, TestOwnerEntity>>()
            .InstancePerDependency();

        builder.Register<DbContextOptions<TestDbContext>>(_ =>
        {
            var options = new DbContextOptionsBuilder<TestDbContext>();
            options.UseInMemoryDatabase("core-storage-tests-" + Guid.NewGuid());
            // The in-memory provider has no real transactions; silence the warning so the single
            // EF transaction path (ITransaction.InvokeAsync) is a no-op.
            options.ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning));
            return options.Options;
        }).SingleInstance();

        builder.RegisterModule(new StorageFoundationModule<TestDbContext>(typeof(Module).Assembly));
    }
}
