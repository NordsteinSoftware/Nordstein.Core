using Autofac;

namespace Nordstein.Core.Testing.Tests;

/// <summary>
/// Wires the harness's own tests over <c>Nordstein.Core.Common.Module</c>, which supplies the
/// temporary directories and the clock the harness module builds on. Each test adds what it needs
/// through <c>GetServices(builder => …)</c>.
/// </summary>
public sealed class Module : Autofac.Module
{
    protected override void Load(ContainerBuilder builder)
    {
        base.Load(builder);
        builder.RegisterModule<Common.Module>();
    }
}
