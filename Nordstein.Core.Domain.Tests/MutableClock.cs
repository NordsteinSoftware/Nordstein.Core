using Nordstein.Core.Common.Time;

namespace Nordstein.Core.Domain.Tests;

/// <summary>
/// A test clock that reads what a test set, so entity timestamps and their validation can be
/// pinned to an instant.
/// </summary>
internal sealed class MutableClock : IClock
{
    public MutableClock(DateTimeOffset start) => UtcNow = start;

    public DateTimeOffset UtcNow { get; set; }
}
