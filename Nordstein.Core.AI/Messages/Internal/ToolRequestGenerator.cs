using Nordstein.Core.Common.Async;
using Nordstein.Core.Common.Random;
using Nordstein.Core.Domain;

namespace Nordstein.Core.AI.Messages.Internal;

internal class ToolRequestGenerator : DomainObjectGenerator<ToolRequest>
{
    public ToolRequestGenerator(IRandom random) : base(random)
    {
    }

    public override Task<ToolRequest> CreateAsync(CancellationToken cancellationToken = default)
        => new ToolRequest(
                id: random.Guid().ToString(),
                name: random.String(),
                arguments: "{}")
            .ToTaskResult();
}
