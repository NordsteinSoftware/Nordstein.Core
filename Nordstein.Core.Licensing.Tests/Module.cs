using Autofac;
using NSubstitute;
using Nordstein.Core.Licensing.Internal;

namespace Nordstein.Core.Licensing.Tests;

/// <summary>
/// DI module for licensing engine tests. Registers Common + the licensing engine with a
/// test-generated keypair, the test tier policy, and no real license JWT (fallback tier).
/// Individual tests override registrations via GetServices(action) to supply stubs or specific
/// configurations.
/// </summary>
public sealed class Module : Autofac.Module
{
    internal static readonly TestLicenseFactory Factory = new();

    internal static readonly TestTierPolicy Policy = new();

    protected override void Load(ContainerBuilder builder)
    {
        base.Load(builder);

        builder.RegisterModule<Nordstein.Core.Common.Module>();
        builder.RegisterModule(new LicensingModule(Factory.Configuration(), Policy));

        // Replace the real LicenseCacheStore with a stub so tests don't touch the filesystem.
        builder.RegisterInstance(Substitute.For<ILicenseCacheStore>())
            .As<ILicenseCacheStore>()
            .SingleInstance();

        // Replace the real LicenseServerClient with a stub so tests don't hit the network.
        builder.RegisterInstance(Substitute.For<ILicenseServerClient>())
            .As<ILicenseServerClient>()
            .SingleInstance();
    }
}
