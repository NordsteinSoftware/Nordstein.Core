using Autofac;
using NSubstitute;
using Nordstein.Core.Domain.Events;
using Nordstein.Core.Search.Postgres.Tests.Internal;
using Nordstein.Core.Storage;

namespace Nordstein.Core.Search.Postgres.Tests;

/// <summary>
/// Wires the package under test over the storage foundation with an empty EF model. A test adds
/// the <c>DbContextOptions</c> of its own database.
/// </summary>
public sealed class Module : Autofac.Module
{
    protected override void Load(ContainerBuilder builder)
    {
        base.Load(builder);
        builder.RegisterInstance(Substitute.For<IEntityEventService>()).As<IEntityEventService>().SingleInstance();
        builder.RegisterModule(new StorageFoundationModule<SearchTestDbContext>(typeof(Module).Assembly));
        builder.RegisterModule<Postgres.Module>();
    }
}
