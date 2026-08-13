using Autofac;

namespace Nordstein.Core.Domain.Tests;

public sealed class Module : Autofac.Module
{
    protected override void Load(ContainerBuilder builder)
    {
        base.Load(builder);
        builder.RegisterType<TestEntityRepository>().As<IRepository<ITestEntity>>().SingleInstance();
        builder.RegisterModule(new Domain.Module(typeof(Module).Assembly));
    }
}
