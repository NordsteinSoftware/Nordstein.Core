using AwesomeAssertions;
using Nordstein.Core.AI.Completions;

namespace Nordstein.Core.AI.Tests;

[TestClass]
public sealed class ModelParametersEmptyTests
{
    [TestMethod]
    public void Empty_HasNoValueSet()
    {
        IModelParameters empty = IModelParameters.Empty;

        empty.Temperature.Should().BeNull();
        empty.TopP.Should().BeNull();
        empty.ReasoningEffort.Should().BeNull();
        empty.FrequencyPenalty.Should().BeNull();
        empty.PresencePenalty.Should().BeNull();
        empty.MaxTokens.Should().BeNull();
        empty.Seed.Should().BeNull();
        empty.Stop.Should().BeNull();
        empty.N.Should().BeNull();
    }

    [TestMethod]
    public void Empty_ReturnsSameInstance()
        => IModelParameters.Empty.Should().BeSameAs(IModelParameters.Empty);
}
