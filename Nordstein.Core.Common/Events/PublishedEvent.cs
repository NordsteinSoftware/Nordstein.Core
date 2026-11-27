namespace Nordstein.Core.Common.Events;

/// <summary>
/// An event with the id its hub gave it: the subscriber's replay cursor.
/// </summary>
/// <typeparam name="TEvent">The event type.</typeparam>
/// <param name="Id">The event's id; ids rise by one per event within a hub.</param>
/// <param name="Event">The published event, shared by reference with every other subscriber.</param>
public sealed record PublishedEvent<TEvent>(long Id, TEvent Event)
    where TEvent : notnull;
