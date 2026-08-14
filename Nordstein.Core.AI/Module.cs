using System.Text.Json.Serialization;
using Autofac;
using Nordstein.Core.AI.Messages.Internal;
using Nordstein.Core.AI.Serialization;
using Nordstein.Core.AI.Serialization.Internal;
using Nordstein.Core.AI.Tools.Internal;

namespace Nordstein.Core.AI;

/// <summary>
/// Registers the AI foundation: the domain-object generators for messages, tools, prompts and
/// completions (via assembly discovery), the JSON converters for message content and tool
/// arguments, the text serializer, and the output formats. Consuming products register this
/// module alongside their own domain module.
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

        builder.RegisterType<JsonTextSerializer>().As<ITextSerializer>();
        builder.RegisterType<JsonOutputFormat>().AsSelf();
        builder.RegisterType<StringOutputFormat>().AsSelf();

        builder.Register<IOutputFormat.Create>(c =>
        {
            var ctx = c.Resolve<IComponentContext>();
            return type =>
            {
                if (type == typeof(string))
                    return ctx.Resolve<StringOutputFormat>();
                return ctx.Resolve<JsonOutputFormat>(new TypedParameter(typeof(Type), type));
            };
        }).AsSelf();
    }
}
