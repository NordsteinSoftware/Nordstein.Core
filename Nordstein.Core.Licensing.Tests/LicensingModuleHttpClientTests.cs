using Autofac;
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Nordstein.Core.Testing;

namespace Nordstein.Core.Licensing.Tests;

/// <summary>
/// Covers the typed-<c>HttpClient</c> configuration the <c>LicensingModule</c> registers for the
/// license-server client: the base address is resolved from the container-registered
/// <see cref="LicensingConfiguration"/> (so a late override governs it) and normalized to a single
/// trailing slash, and the request timeout is pinned.
/// </summary>
[TestClass]
public sealed class LicensingModuleHttpClientTests : BaseTest<Module>
{
    [TestMethod]
    public void ConfiguredHttpClient_UsesContainerConfigurationBaseAddressAndTimeout()
    {
        // The override deliberately carries a ServerUrl distinct from Factory.Configuration()'s
        // default ("https://license.example.com"): the assertion can only pass when the
        // container-registered configuration — not the constructor argument the module captured —
        // governs the typed client's base address.
        var config = Module.Factory.Configuration() with { ServerUrl = "https://override.example.com" };
        var services = GetServices(builder => builder.RegisterInstance(config).SingleInstance());

        var factory = services.GetRequiredService<IHttpClientFactory>();
        using var client = factory.CreateClient("license-server");

        client.BaseAddress.Should().Be(new Uri("https://override.example.com/"));
        client.Timeout.Should().Be(TimeSpan.FromSeconds(30));
    }

    [TestMethod]
    public void ConfiguredHttpClient_TrailingSlashInServerUrl_IsNotDoubled()
    {
        // The engine trims a trailing slash before re-appending one, so a configured URL that
        // already ends in '/' must not produce "…//".
        var config = Module.Factory.Configuration() with { ServerUrl = "https://license.example.com/" };
        var services = GetServices(builder => builder.RegisterInstance(config).SingleInstance());

        var factory = services.GetRequiredService<IHttpClientFactory>();
        using var client = factory.CreateClient("license-server");

        client.BaseAddress.Should().Be(new Uri("https://license.example.com/"));
    }
}
