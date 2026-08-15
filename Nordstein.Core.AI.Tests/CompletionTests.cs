using System.ComponentModel.DataAnnotations;
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Nordstein.Core.AI.Completions;
using Nordstein.Core.AI.Completions.Internal;
using Nordstein.Core.AI.Messages;
using Nordstein.Core.Common.Validation;
using Nordstein.Core.Testing;

namespace Nordstein.Core.AI.Tests;

/// <summary>
/// Exercises the internal <see cref="Completion"/> record: its getters, the validation contract
/// (response + optional usage + non-negative latency), the null-usage branch, value equality, and
/// construction through the registered <see cref="ICompletion.Create"/> factory.
/// </summary>
[TestClass]
public sealed class CompletionTests : BaseTest<Nordstein.Core.AI.Module>
{
    private static AssistantMessage ValidResponse()
        => new([Content.FromText("hello")], []);

    [TestMethod]
    public void Constructor_ExposesResponseUsageAndLatency()
    {
        AssistantMessage response = ValidResponse();
        var usage = new TokenUsage(10, 5);
        TimeSpan latency = TimeSpan.FromMilliseconds(250);

        var completion = new Completion(response, usage, latency);

        completion.Response.Should().BeSameAs(response);
        completion.Usage.Should().Be(usage);
        completion.Latency.Should().Be(latency);
    }

    [TestMethod]
    public void Constructor_WithNullUsage_LeavesUsageNull()
    {
        var completion = new Completion(ValidResponse(), usage: null, TimeSpan.Zero);

        completion.Usage.Should().BeNull();
    }

    [TestMethod]
    public void Validate_ValidCompletion_DoesNotThrow()
    {
        var completion = new Completion(ValidResponse(), new TokenUsage(3, 4), TimeSpan.FromSeconds(1));

        FluentActions.Invoking(() => completion.Validate()).Should().NotThrow();
    }

    [TestMethod]
    public void Validate_WithNullUsage_DoesNotAddUsageErrors()
    {
        // The null-usage branch: `Usage?.Validate(...) ?? []` must contribute nothing rather than
        // dereference a null usage.
        var completion = new Completion(ValidResponse(), usage: null, TimeSpan.Zero);

        FluentActions.Invoking(() => completion.Validate()).Should().NotThrow();
    }

    [TestMethod]
    public void Validate_NegativeLatency_YieldsLatencyError()
    {
        var completion = new Completion(ValidResponse(), new TokenUsage(1, 1), TimeSpan.FromTicks(-1));

        ValidationResult[] results = completion.Validate(new ValidationContext(completion)).ToArray();

        results.Should().Contain(r => r != null && r.ErrorMessage == "Latency must not be negative.");
    }

    [TestMethod]
    public void Validate_NegativeLatency_ThrowsOnValidate()
    {
        var completion = new Completion(ValidResponse(), new TokenUsage(1, 1), TimeSpan.FromSeconds(-1));

        FluentActions.Invoking(() => completion.Validate()).Should().Throw<ValidationException>();
    }

    [TestMethod]
    public void Equals_SameComponents_AreEqual()
    {
        AssistantMessage response = ValidResponse();
        var usage = new TokenUsage(7, 8);
        TimeSpan latency = TimeSpan.FromMilliseconds(500);

        var a = new Completion(response, usage, latency);
        var b = new Completion(response, usage, latency);

        a.Should().Be(b);
        a.GetHashCode().Should().Be(b.GetHashCode());
    }

    [TestMethod]
    public void Equals_DifferentLatency_AreNotEqual()
    {
        AssistantMessage response = ValidResponse();
        var usage = new TokenUsage(7, 8);

        var a = new Completion(response, usage, TimeSpan.FromMilliseconds(500));
        var b = new Completion(response, usage, TimeSpan.FromMilliseconds(750));

        a.Should().NotBe(b);
    }

    [TestMethod]
    public void Factory_ProducesValidatedCompletion()
    {
        IServiceProvider services = GetServices();
        var factory = services.GetRequiredService<ICompletion.Create>();

        ICompletion completion = factory(ValidResponse(), new TokenUsage(2, 3), TimeSpan.FromSeconds(1));

        completion.Response.Should().NotBeNull();
        completion.Usage.Should().Be(new TokenUsage(2, 3));
        completion.Latency.Should().Be(TimeSpan.FromSeconds(1));
    }
}
