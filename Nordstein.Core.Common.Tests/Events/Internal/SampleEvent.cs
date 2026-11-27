namespace Nordstein.Core.Common.Tests.Events.Internal;

/// <summary>
/// The stand-in event of the hub tests: a topic a filter can select on and a number to tell events apart.
/// </summary>
internal sealed record SampleEvent(string Topic, int Number);

/// <summary>
/// A second event type, to show that each type has its own hub.
/// </summary>
internal sealed record OtherEvent(int Number);
