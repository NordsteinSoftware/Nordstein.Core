using Nordstein.Core.Common.Async;
using Nordstein.Core.Common.Random;
using Nordstein.Core.Domain;

namespace Nordstein.Core.AI.Messages.Internal;

internal class SystemMessageGenerator : DomainObjectGenerator<SystemMessage>
{
    public SystemMessageGenerator(IRandom random) : base(random)
    {
    }

    public override Task<SystemMessage> CreateAsync(CancellationToken cancellationToken = default)
        => new SystemMessage(random.String()).ToTaskResult();
}
