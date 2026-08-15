using AwesomeAssertions;
using Nordstein.Core.AI.Completions.Internal;
using Nordstein.Core.Common.Validation;

namespace Nordstein.Core.AI.Tests;

/// <summary>
/// Exercises the internal <see cref="ModelParameters"/> value type directly: property getters, the
/// hand-written value equality / hash contract over every parameter (including the
/// <c>Stop</c> sequence), <see cref="object.ToString"/>, and the boundary values that must
/// <b>pass</b> validation (the throwing cases live in <c>ModelParametersValidationTests</c>).
/// </summary>
[TestClass]
public sealed class ModelParametersTests
{
    private static ModelParameters Build(
        double? temperature = 0.7,
        double? topP = 0.9,
        string? reasoningEffort = "high",
        double? frequencyPenalty = 0.5,
        double? presencePenalty = -0.5,
        int? maxTokens = 256,
        long? seed = 42L,
        IReadOnlyList<string>? stop = null,
        int? n = 2)
        => new(temperature, topP, reasoningEffort, frequencyPenalty, presencePenalty, maxTokens, seed, stop, n);

    [TestMethod]
    public void Constructor_WithAllValues_ExposesEachViaGetter()
    {
        var parameters = new ModelParameters(
            temperature: 0.3,
            topP: 0.6,
            reasoningEffort: "medium",
            frequencyPenalty: 1.0,
            presencePenalty: -1.0,
            maxTokens: 128,
            seed: 99L,
            stop: ["s1", "s2"],
            n: 4);

        parameters.Temperature.Should().Be(0.3);
        parameters.TopP.Should().Be(0.6);
        parameters.ReasoningEffort.Should().Be("medium");
        parameters.FrequencyPenalty.Should().Be(1.0);
        parameters.PresencePenalty.Should().Be(-1.0);
        parameters.MaxTokens.Should().Be(128);
        parameters.Seed.Should().Be(99L);
        parameters.Stop.Should().Equal("s1", "s2");
        parameters.N.Should().Be(4);
    }

    [TestMethod]
    public void Constructor_WithNoArguments_LeavesEverythingUnset()
    {
        var parameters = new ModelParameters();

        parameters.Temperature.Should().BeNull();
        parameters.TopP.Should().BeNull();
        parameters.ReasoningEffort.Should().BeNull();
        parameters.FrequencyPenalty.Should().BeNull();
        parameters.PresencePenalty.Should().BeNull();
        parameters.MaxTokens.Should().BeNull();
        parameters.Seed.Should().BeNull();
        parameters.Stop.Should().BeNull();
        parameters.N.Should().BeNull();
    }

    [TestMethod]
    public void Equals_IdenticalFullParameters_AreEqualWithSameHashCode()
    {
        ModelParameters a = Build(stop: ["a", "b"]);
        ModelParameters b = Build(stop: ["a", "b"]);

        a.Should().Be(b);
        a.GetHashCode().Should().Be(b.GetHashCode());
    }

    [TestMethod]
    public void Equals_Self_ReturnsTrue()
    {
        ModelParameters parameters = Build();

        parameters.Equals(parameters).Should().BeTrue();
    }

    [TestMethod]
    public void Equals_Null_ReturnsFalse()
    {
        ModelParameters parameters = Build();

        parameters.Equals((ModelParameters?)null).Should().BeFalse();
    }

    [TestMethod]
    public void Equals_DifferentRuntimeType_ReturnsFalse()
    {
        ModelParameters parameters = Build();

        parameters.Should().NotBe("not a parameter set");
    }

    [TestMethod]
    public void Equals_DifferingInASingleParameter_AreNotEqual()
    {
        // One variant per parameter so every term of the equality chain is exercised as the
        // first (and therefore decisive) mismatch.
        Build().Should().NotBe(Build(temperature: 0.8));
        Build().Should().NotBe(Build(topP: 0.5));
        Build().Should().NotBe(Build(reasoningEffort: "low"));
        Build().Should().NotBe(Build(frequencyPenalty: 0.1));
        Build().Should().NotBe(Build(presencePenalty: 0.1));
        Build().Should().NotBe(Build(maxTokens: 100));
        Build().Should().NotBe(Build(seed: 7L));
        Build().Should().NotBe(Build(n: 3));
        Build(stop: ["a"]).Should().NotBe(Build(stop: ["b"]));
    }

    [TestMethod]
    public void Equals_NullVersusEmptyStop_AreEqual()
    {
        // Both null and [] are normalised to an empty sequence by the equality comparison, so a
        // parameter set with no stop sequence equals one with an empty stop sequence.
        ModelParameters withNull = Build(stop: null);
        ModelParameters withEmpty = Build(stop: []);

        withNull.Should().Be(withEmpty);
        withNull.GetHashCode().Should().Be(withEmpty.GetHashCode());
    }

    [TestMethod]
    public void GetHashCode_EqualStopContentsInDifferentListInstances_AreEqual()
    {
        // The stop list is folded element-by-element into the hash, so equal contents in distinct
        // list instances must hash the same — otherwise Equals/GetHashCode would disagree.
        ModelParameters a = Build(stop: ["x", "y", "z"]);
        ModelParameters b = Build(stop: ["x", "y", "z"]);

        a.GetHashCode().Should().Be(b.GetHashCode());
    }

    [TestMethod]
    public void ToString_IncludesTypeNameAndParameters()
    {
        string text = Build().ToString();

        text.Should().Contain("ModelParameters");
        text.Should().Contain("Temperature");
    }

    [TestMethod]
    public void Validate_BoundaryValuesWithinRange_DoNotThrow()
    {
        FluentActions.Invoking(() => new ModelParameters(temperature: 0).Validate()).Should().NotThrow();
        FluentActions.Invoking(() => new ModelParameters(temperature: 2.0).Validate()).Should().NotThrow();
        FluentActions.Invoking(() => new ModelParameters(topP: 0).Validate()).Should().NotThrow();
        FluentActions.Invoking(() => new ModelParameters(topP: 1).Validate()).Should().NotThrow();
        FluentActions.Invoking(() => new ModelParameters(frequencyPenalty: -2).Validate()).Should().NotThrow();
        FluentActions.Invoking(() => new ModelParameters(frequencyPenalty: 2).Validate()).Should().NotThrow();
        FluentActions.Invoking(() => new ModelParameters(presencePenalty: -2).Validate()).Should().NotThrow();
        FluentActions.Invoking(() => new ModelParameters(presencePenalty: 2).Validate()).Should().NotThrow();
        FluentActions.Invoking(() => new ModelParameters(maxTokens: 1).Validate()).Should().NotThrow();
        FluentActions.Invoking(() => new ModelParameters(n: 1).Validate()).Should().NotThrow();
    }

    [TestMethod]
    public void Validate_EveryParameterSetToAValidValue_DoesNotThrow()
    {
        ModelParameters parameters = Build(stop: ["END"]);

        FluentActions.Invoking(() => parameters.Validate()).Should().NotThrow();
    }
}
