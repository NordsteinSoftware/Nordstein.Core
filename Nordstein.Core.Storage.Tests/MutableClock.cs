using Nordstein.Core.Common.Time;

namespace Nordstein.Core.Storage.Tests;

/// <summary>
/// The container's clock in these tests: the real clock until a test pins it, so rows built from
/// <see cref="DateTimeOffset.UtcNow"/> and rows the repositories stamp stay in order.
/// </summary>
internal sealed class MutableClock : IClock
{
    private DateTimeOffset? pinned;

    public DateTimeOffset UtcNow
    {
        get => pinned ?? DateTimeOffset.UtcNow;
        set => pinned = value;
    }
}
