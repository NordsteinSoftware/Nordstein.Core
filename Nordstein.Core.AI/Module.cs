using System.Text.Json.Serialization;
using Autofac;
using Nordstein.Core.AI.Messages.Internal;
using Nordstein.Core.AI.Tools.Internal;

namespace Nordstein.Core.AI;

/// <summary>
/// Registers the AI foundation: the domain-object generators for messages, tools, prompts and
/// completions (via assembly discovery), and the JSON converters for message content and tool
/// arguments. Consuming products register this module alongside their own domain module.
/// </summary>
public sealed class Module : Autofac.Module
{
    protected override void Load(ContainerBuilder builder)
    {
        base.Load(builder);

        builder.RegisterModule(new Nordstein.Core.Domain.Module(typeof(Module).Assembly));

        builder.RegisterType<ContentJsonConverter>()
            .As<JsonConverter>()
            .SingleInstance();

        builder.RegisterType<ToolArgumentsJsonConverter>()
            .As<JsonConverter>()
            .SingleInstance();
    }
}
