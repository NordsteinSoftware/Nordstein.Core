using Autofac;
using Nordstein.Core.Workflows.Internal;

namespace Nordstein.Core.Workflows;

/// <summary>
/// Registers the workflow kernel: <see cref="IWorkflowValidator"/>, <see cref="IWorkflowInterpreter"/>,
/// <see cref="INodeTrial"/> and the kernel's own node types
/// <see cref="IConditionNodeType"/> and <see cref="IForEachNodeType"/>, all stateless singletons. The product registers its
/// node catalogue and step journal itself, and puts a kernel node type in its catalogue only by resolving it: the module
/// registers none of them as <see cref="INodeType"/>.
/// </summary>
public sealed class Module : Autofac.Module
{
    protected override void Load(ContainerBuilder builder)
    {
        base.Load(builder);
        builder.RegisterType<ReferenceParser>().AsSelf().SingleInstance();
        builder.RegisterType<SchemaReader>().AsSelf().SingleInstance();
        builder.RegisterType<ConfigChecker>().AsSelf().SingleInstance();
        builder.RegisterType<ReferenceChecker>().AsSelf().SingleInstance();
        builder.RegisterType<OutputChecker>().AsSelf().SingleInstance();
        builder.RegisterType<ReferenceResolver>().AsSelf().SingleInstance();
        builder.RegisterType<WorkflowValidator>().As<IWorkflowValidator>().SingleInstance();
        builder.RegisterType<WorkflowInterpreter>().As<IWorkflowInterpreter>().SingleInstance();
        builder.RegisterType<NodeTrial>().As<INodeTrial>().SingleInstance();
        builder.RegisterType<ConditionNodeType>().As<IConditionNodeType>().SingleInstance();
        builder.RegisterType<ForEachNodeType>().As<IForEachNodeType>().SingleInstance();
    }
}
