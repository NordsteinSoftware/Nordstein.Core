using AwesomeAssertions;
using Nordstein.Core.Domain.Exceptions;

namespace Nordstein.Core.Domain.Tests;

[TestClass]
public sealed class OptimisticConcurrencyExceptionTests
{
    [TestMethod]
    public void Constructor_WithIdAndType_SetsDescriptiveMessageAndNoInner()
    {
        Guid id = Guid.NewGuid();

        var exception = new OptimisticConcurrencyException(id, typeof(TimeSpan));

        exception.Message.Should().Contain(id.ToString()).And.Contain(nameof(TimeSpan));
        exception.InnerException.Should().BeNull();
    }

    [TestMethod]
    public void Constructor_WithInnerException_SetsMessageAndPreservesInner()
    {
        Guid id = Guid.NewGuid();
        var inner = new InvalidOperationException("boom");

        var exception = new OptimisticConcurrencyException(id, typeof(TimeSpan), inner);

        exception.Message.Should().Contain(id.ToString()).And.Contain(nameof(TimeSpan));
        exception.InnerException.Should().BeSameAs(inner);
    }
}
