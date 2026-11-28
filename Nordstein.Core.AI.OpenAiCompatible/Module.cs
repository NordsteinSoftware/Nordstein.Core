using Autofac;
using Nordstein.Core.AI.OpenAiCompatible.Internal;
using Nordstein.Core.Common.Http;

namespace Nordstein.Core.AI.OpenAiCompatible;

/// <summary>
/// Registers <see cref="OpenAiCompatibleEmbeddingClientFactory"/> and the HTTP handler its clients
/// send through.
/// </summary>
/// <remarks>
/// The handler is registered under the name <see cref="HandlerName"/> as a singleton from
/// <see cref="DirectHttpHandlerFactory"/>: no proxy and no redirect, so the configured endpoint is
/// the only host a request reaches. A host that registers its own <see cref="HttpMessageHandler"/>
/// under that name first (a test double, a handler with a proxy) keeps it. The time-out runs on the
/// container's <see cref="TimeProvider"/>, which <c>Nordstein.Core.Common.Module</c> registers.
/// </remarks>
public sealed class Module : Autofac.Module
{
    /// <summary>
    /// The name of the <see cref="HttpMessageHandler"/> registration the clients send through.
    /// </summary>
    public const string HandlerName = "Nordstein.Core.AI.OpenAiCompatible";

    /// <inheritdoc />
    protected override void Load(ContainerBuilder builder)
    {
        base.Load(builder);
        builder.Register<HttpMessageHandler>(_ => DirectHttpHandlerFactory.Create())
            .Named<HttpMessageHandler>(HandlerName)
            .SingleInstance()
            .PreserveExistingDefaults();
        builder.Register<OpenAiCompatibleEmbeddingClientFactory>(context =>
        {
            HttpMessageHandler handler = context.ResolveNamed<HttpMessageHandler>(HandlerName);
            TimeProvider timeProvider = context.Resolve<TimeProvider>();
            return options => new OpenAiCompatibleEmbeddingClient(options, handler, timeProvider);
        }).SingleInstance();
    }
}
