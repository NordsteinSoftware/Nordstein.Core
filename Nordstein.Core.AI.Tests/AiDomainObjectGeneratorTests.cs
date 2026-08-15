using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Nordstein.Core.AI.Completions;
using Nordstein.Core.AI.Prompts;
using Nordstein.Core.Common.Validation;
using Nordstein.Core.Domain;
using Nordstein.Core.Testing;

namespace Nordstein.Core.AI.Tests;

/// <summary>
/// Resolves the assembly-discovered <see cref="IDomainObjectGenerator{T}"/> implementations for the
/// AI completion and prompt types and asserts each produces a valid, non-null domain object. These
/// generators back test-data and demo seeding, so a broken one silently poisons every consumer's
/// fixtures.
/// </summary>
[TestClass]
public sealed class AiDomainObjectGeneratorTests : BaseTest<Nordstein.Core.AI.Module>
{
    [TestMethod]
    public async Task CompletionGenerator_CreateAsync_ProducesValidCompletion()
    {
        IServiceProvider services = GetServices();
        var generator = services.GetRequiredService<IDomainObjectGenerator<ICompletion>>();

        ICompletion completion = await generator.CreateAsync(CancellationToken);

        completion.Should().NotBeNull();
        completion.Response.Should().NotBeNull();
        completion.Usage.Should().NotBeNull();
        (completion.Latency > TimeSpan.Zero).Should().BeTrue();
        FluentActions.Invoking(() => completion.Validate()).Should().NotThrow();
    }

    [TestMethod]
    public async Task ModelParametersGenerator_CreateAsync_ProducesValidParameters()
    {
        IServiceProvider services = GetServices();
        var generator = services.GetRequiredService<IDomainObjectGenerator<IModelParameters>>();

        IModelParameters parameters = await generator.CreateAsync(CancellationToken);

        parameters.Should().NotBeNull();
        parameters.Temperature.Should().BeInRange(0.7, 0.9);
        parameters.TopP.Should().BeInRange(0.7, 0.9);
        parameters.ReasoningEffort.Should().BeOneOf("none", "low", "medium", "high");
        parameters.MaxTokens.Should().BeInRange(50, 200);
        FluentActions.Invoking(() => parameters.Validate()).Should().NotThrow();
    }

    [TestMethod]
    public async Task TokenUsageGenerator_CreateAsync_ProducesValidUsage()
    {
        IServiceProvider services = GetServices();
        var generator = services.GetRequiredService<IDomainObjectGenerator<TokenUsage>>();

        TokenUsage usage = await generator.CreateAsync(CancellationToken);

        usage.Should().NotBeNull();
        usage.InputTokenCount.Should().BeInRange(0UL, 1000UL);
        usage.OutputTokenCount.Should().BeInRange(0UL, 1000UL);
        FluentActions.Invoking(() => usage.Validate()).Should().NotThrow();
    }

    [TestMethod]
    public async Task PromptGenerator_CreateAsync_ProducesValidPrompt()
    {
        IServiceProvider services = GetServices();
        var generator = services.GetRequiredService<IDomainObjectGenerator<IPrompt>>();

        IPrompt prompt = await generator.CreateAsync(CancellationToken);

        prompt.Should().NotBeNull();
        prompt.Name.Should().NotBeNullOrWhiteSpace();
        prompt.ToPromptString().Should().NotBeNullOrWhiteSpace();
    }

    [TestMethod]
    public async Task PromptTemplateGenerator_CreateAsync_ProducesValidTemplate()
    {
        IServiceProvider services = GetServices();
        var generator = services.GetRequiredService<IDomainObjectGenerator<IPromptTemplate>>();

        IPromptTemplate template = await generator.CreateAsync(CancellationToken);

        template.Should().NotBeNull();
        template.Name.Should().NotBeNullOrWhiteSpace();
        template.Template.Should().NotBeNullOrWhiteSpace();
    }
}
