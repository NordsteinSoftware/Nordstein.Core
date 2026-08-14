using AwesomeAssertions;
using Nordstein.Core.AI.Clients;

namespace Nordstein.Core.AI.Tests;

[TestClass]
public sealed class ModelSamplingParametersTests
{
    [TestMethod]
    public void IsEmpty_WithDefaults_IsTrue()
        => new ModelSamplingParameters().IsEmpty.Should().BeTrue();

    [TestMethod]
    public void IsEmpty_WithEmptyStopSequences_IsTrue()
        => new ModelSamplingParameters(StopSequences: []).IsEmpty.Should().BeTrue();

    [TestMethod]
    public void IsEmpty_WithWhitespaceReasoningEffort_IsTrue()
        => new ModelSamplingParameters(ReasoningEffort: "  ").IsEmpty.Should().BeTrue();

    [TestMethod]
    public void IsEmpty_WithAnySingleValue_IsFalse()
    {
        new ModelSamplingParameters(Temperature: 0).IsEmpty.Should().BeFalse();
        new ModelSamplingParameters(TopP: 0).IsEmpty.Should().BeFalse();
        new ModelSamplingParameters(FrequencyPenalty: 0).IsEmpty.Should().BeFalse();
        new ModelSamplingParameters(PresencePenalty: 0).IsEmpty.Should().BeFalse();
        new ModelSamplingParameters(MaxOutputTokens: 0).IsEmpty.Should().BeFalse();
        new ModelSamplingParameters(Seed: 0).IsEmpty.Should().BeFalse();
        new ModelSamplingParameters(StopSequences: ["stop"]).IsEmpty.Should().BeFalse();
        new ModelSamplingParameters(ReasoningEffort: "high").IsEmpty.Should().BeFalse();
    }
}
