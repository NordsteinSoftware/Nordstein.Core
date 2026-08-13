using System.Threading.Channels;

namespace Nordstein.Core.Domain.Events;

public interface IEntityEventService
{
    void Notify(EntityChangedEvent evt);

    ChannelReader<EntityChangedEvent> Subscribe(CancellationToken cancellationToken, Type? entityType = null);
}

public enum EntityChangeType
{
    Added,
    Updated,
    Removed,
}

public record EntityChangedEvent(Guid EntityId, Type EntityType, EntityChangeType ChangeType);
