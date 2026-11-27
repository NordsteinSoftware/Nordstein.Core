using Autofac;
using Autofac.Extensions.DependencyInjection;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NSubstitute;
using Nordstein.Core.Common.DependencyInjection;
using Nordstein.Core.Common.Lifecycle;

namespace Nordstein.Core.Testing.Internal;

/// <summary>
/// The harness's own registrations, stacked under every test container before the module under
/// test: the <see cref="IServiceProvider"/> façade, a substitute <see cref="IHostEnvironment"/> rooted
/// in a temporary directory, logging, and an ephemeral Data Protection key ring.
/// </summary>
internal sealed class Module : Autofac.Module
{
    protected override void Load(ContainerBuilder builder)
    {
        base.Load(builder);
        builder
            .Register(sp => new AutofacServiceProvider(sp.Resolve<ILifetimeScope>()))
            .As<IServiceProvider>();

        builder.Register(c =>
        {
            var dir = c.Resolve<ITempDirectory.Create>()();
            var env = Substitute.For<IHostEnvironment>();
            env.ContentRootPath.Returns(dir.Path);
            return env;
        }).SingleInstance();

        builder.RegisterServiceCollection(sc => sc.AddLogging());

        // A product's ISecretProtector (Nordstein.Core.Common.Security) is typically built on
        // IDataProtectionProvider, and code that protects secrets at rest resolves it eagerly. Register
        // an ephemeral (non-persisted) Data Protection key ring so every test container can construct it.
        builder.RegisterServiceCollection(sc => sc.AddDataProtection());
    }
}
