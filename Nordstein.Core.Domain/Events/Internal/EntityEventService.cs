using System.Collections.Concurrent;
using System.Threading.Channels;

namespace Nordstein.Core.Domain.Events.Internal;

internal sealed class EntityEventService : IEntityEventService, IDisposable
{
    private readonly ConcurrentDictionary<Guid, Subscription> subscribers = new();

    public void Notify(EntityChangedEvent evt)
    {
        foreach (var pair in subscribers)
        {
            Subscription subscription = pair.Value;
            if (subscription.EntityType is not null && subscription.EntityType != evt.EntityType)
            {
                continue;
            }

            if (!subscription.Writer.TryWrite(evt))
            {
                subscribers.TryRemove(pair.Key, out _);
            }
        }
    }

    public ChannelReader<EntityChangedEvent> Subscribe(CancellationToken cancellationToken, Type? entityType = null)
    {
        if (!cancellationToken.CanBeCanceled)
        {
            throw new ArgumentException(
                "Subscription requires a cancellable token to avoid leaking subscribers.",
                nameof(cancellationToken));
        }

        Channel<EntityChangedEvent> channel = Channel.CreateUnbounded<EntityChangedEvent>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false,
        });
        Guid id = Guid.NewGuid();
        subscribers[id] = new Subscription(channel.Writer, entityType);
        cancellationToken.Register(() =>
        {
            subscribers.TryRemove(id, out _);
            channel.Writer.TryComplete();
        });
        return channel.Reader;
    }

    public void Dispose()
    {
        Subscription[] snapshot = subscribers.Values.ToArray();
        subscribers.Clear();
        foreach (Subscription subscription in snapshot)
        {
            subscription.Writer.TryComplete();
        }
    }

    private sealed record Subscription(ChannelWriter<EntityChangedEvent> Writer, Type? EntityType);
}
