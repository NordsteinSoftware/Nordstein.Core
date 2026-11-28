using Docker.DotNet.Models;

namespace Nordstein.Core.Testing.Postgres.Internal;

/// <summary>
/// Publishes every port of a container on the loopback interface only, so no other host can reach
/// a test server.
/// </summary>
internal static class LoopbackPorts
{
    public static void Bind(CreateContainerParameters parameters)
    {
        if (parameters.HostConfig is not { PortBindings: { } portBindings })
        {
            return;
        }

        foreach (PortBinding binding in portBindings.Values.Where(bindings => bindings is not null).SelectMany(bindings => bindings))
        {
            binding.HostIP = "127.0.0.1";
        }
    }
}
