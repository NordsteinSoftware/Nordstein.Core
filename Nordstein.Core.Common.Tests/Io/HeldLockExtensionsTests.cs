using AwesomeAssertions;
using Nordstein.Core.Common.Io.Internal;

namespace Nordstein.Core.Common.Tests.Io;

[TestClass]
public sealed class HeldLockExtensionsTests
{
    private const int Ewouldblock = 11;
    private const int Eio = 5;

    [TestMethod]
    [OSCondition(OperatingSystems.Linux)]
    public void IsHeldLock_WithEwouldblock_IsTrue()
    {
        new IOException("held", Ewouldblock).IsHeldLock().Should().BeTrue();
    }

    [TestMethod]
    [OSCondition(OperatingSystems.Linux)]
    public void IsHeldLock_WithAnyOtherErrno_IsFalse()
    {
        new IOException("io error", Eio).IsHeldLock().Should().BeFalse();
    }

    [TestMethod]
    [OSCondition(OperatingSystems.Linux)]
    public void IsHeldLock_OnADerivedException_IsFalseWhateverItsCode()
    {
        new FileNotFoundException("missing") { HResult = Ewouldblock }.IsHeldLock().Should().BeFalse();
    }
}
