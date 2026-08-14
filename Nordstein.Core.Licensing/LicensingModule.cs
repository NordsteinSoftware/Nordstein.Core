using Autofac;
using Microsoft.Extensions.DependencyInjection;
using Nordstein.Core.Common.DependencyInjection;
using Nordstein.Core.Licensing.Internal;

namespace Nordstein.Core.Licensing;

/// <summary>
/// Autofac module wiring the licensing engine. The configuration (including the product's
/// issuer, audience, trusted keys, and the resolved license JWT) and the tier policy are
/// supplied by the consuming product's composition root. Requires the Nordstein.Core.Common
/// module (clock, async lock, app version) to be registered as well.
/// </summary>
public sealed class LicensingModule : Autofac.Module
{
    private readonly LicensingConfiguration configuration;
    private readonly ILicenseTierPolicy policy;

    public LicensingModule(LicensingConfiguration configuration, ILicenseTierPolicy policy)
    {
        this.configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        this.policy = policy ?? throw new ArgumentNullException(nameof(policy));
    }

    protected override void Load(ContainerBuilder builder)
    {
        base.Load(builder);

        builder.RegisterInstance(configuration).SingleInstance();
        builder.RegisterInstance(policy).As<ILicenseTierPolicy>().SingleInstance();

        builder.RegisterType<JwtLicenseValidator>()
            .As<IJwtLicenseValidator>()
            .SingleInstance();

        builder.RegisterType<LicenseCacheStore>()
            .As<ILicenseCacheStore>()
            .SingleInstance();

        builder.RegisterType<ConfiguredLicenseResolver>()
            .AsSelf()
            .SingleInstance();

        builder.RegisterType<LicenseActivator>()
            .As<ILicenseActivator>()
            .SingleInstance();

        // AutoActivate forces the constructor (and thus the synchronous startup resolution) to
        // run at container build time, so the resolved tier is logged and in force before any
        // request is served. An invalid configured JWT does not crash the host — it degrades
        // to fallback-tier entitlements with LicenseStatus.Invalid for the product to surface.
        builder.RegisterType<LicenseService>()
            .As<ILicenseService>()
            .AsSelf()
            .SingleInstance()
            .AutoActivate();

        builder.RegisterType<LicenseCheckService>()
            .As<ILicenseRefreshTrigger>()
            .AsSelf()
            .SingleInstance();

        builder.RegisterServiceCollection(services =>
        {
            // The base address is resolved from the container (not the constructor argument) so
            // that a configuration registered later — e.g. a composition-root or test override —
            // consistently governs the whole engine, the server client included.
            services.AddHttpClient<ILicenseServerClient, LicenseServerClient>("license-server")
                .ConfigureHttpClient((provider, client) =>
                {
                    var effective = provider.GetRequiredService<LicensingConfiguration>();
                    client.BaseAddress = new Uri(effective.ServerUrl.TrimEnd('/') + "/");
                    client.Timeout = TimeSpan.FromSeconds(30);
                });

            services.AddHostedService(sp => sp.GetRequiredService<LicenseCheckService>());
        });
    }
}
