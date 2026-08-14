using Nordstein.Core.Common.Async;
using Nordstein.Core.Common.Random;
using Nordstein.Core.Domain;

namespace Nordstein.Core.AI.Tools.Internal;

internal class ToolArgumentsGenerator : DomainObjectGenerator<ToolArguments>
{
    public ToolArgumentsGenerator(IRandom random) : base(random)
    {
    }

    public override Task<ToolArguments> CreateAsync(CancellationToken cancellationToken = default)
        => ToolArguments.None.ToTaskResult();
}
