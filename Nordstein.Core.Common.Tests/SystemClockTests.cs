using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Nordstein.Core.Common.Time;
using Nordstein.Core.Testing;

namespace Nordstein.Core.Common.Tests;

[TestClass]
public sealed class SystemClockTests : BaseTest<Module>
{
    [TestMethod]
    public void UtcNow_ReturnsCurrentSystemTime()
    {
        IClock clock = GetServices().GetRequiredService<IClock>();

        DateTimeOffset before = DateTimeOffset.UtcNow;
        DateTimeOffset now = clock.UtcNow;
        DateTimeOffset after = DateTimeOffset.UtcNow;

        now.Should().BeOnOrAfter(before);
        now.Should().BeOnOrBefore(after);
    }

    [TestMethod]
    public void UtcNow_HasZeroOffset()
    {
        IClock clock = GetServices().GetRequiredService<IClock>();

        clock.UtcNow.Offset.Should().Be(TimeSpan.Zero);
    }

    [TestMethod]
    public void UtcNow_IsMonotonicAcrossReads()
    {
        IClock clock = GetServices().GetRequiredService<IClock>();

        DateTimeOffset first = clock.UtcNow;
        DateTimeOffset second = clock.UtcNow;

        second.Should().BeOnOrAfter(first);
    }
}
