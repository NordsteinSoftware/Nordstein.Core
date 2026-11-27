using Autofac.Core;
using AwesomeAssertions;
using Nordstein.Core.Common.DependencyInjection;

namespace Nordstein.Core.Common.Tests.DependencyInjection;

[TestClass]
public sealed class ResolutionExceptionExtensionsTests
{
    [TestMethod]
    public void UnwrapResolutionFailures_NestedWrappers_ReturnsInnermost()
    {
        var cause = new InvalidOperationException("the real failure");
        var wrapped = new DependencyResolutionException(
            "outer",
            new DependencyResolutionException("middle", new DependencyResolutionException("inner", cause)));

        Exception result = wrapped.UnwrapResolutionFailures();

        result.Should().BeSameAs(cause);
    }

    [TestMethod]
    public void UnwrapResolutionFailures_NotAWrapper_ReturnsItself()
    {
        // Only Autofac's own wrapper is peeled: an ordinary exception with an inner one is the answer.
        var exception = new InvalidOperationException("outer", new ArgumentException("inner"));

        Exception result = exception.UnwrapResolutionFailures();

        result.Should().BeSameAs(exception);
    }

    [TestMethod]
    public void UnwrapResolutionFailures_WrapperWithoutInner_ReturnsWrapper()
    {
        var wrapper = new DependencyResolutionException("nothing inside");

        Exception result = wrapper.UnwrapResolutionFailures();

        result.Should().BeSameAs(wrapper);
    }

    [TestMethod]
    public void UnwrapResolutionFailures_WrapperAroundOrdinaryException_StopsAtTheOrdinaryException()
    {
        var ordinary = new InvalidOperationException("ordinary", new ArgumentException("deeper"));
        var wrapped = new DependencyResolutionException("wrapper", ordinary);

        Exception result = wrapped.UnwrapResolutionFailures();

        result.Should().BeSameAs(ordinary);
    }

    [TestMethod]
    public void UnwrapResolutionFailures_Null_Throws()
    {
        Exception missing = (new Exception[1])[0];

        Func<Exception> act = () => ResolutionExceptionExtensions.UnwrapResolutionFailures(missing);

        act.Should().Throw<ArgumentNullException>().WithParameterName("exception");
    }
}
