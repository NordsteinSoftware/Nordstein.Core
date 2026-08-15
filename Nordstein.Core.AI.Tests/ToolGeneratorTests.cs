using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Nordstein.Core.AI.Tools;
using Nordstein.Core.Common.Validation;
using Nordstein.Core.Domain;
using Nordstein.Core.Testing;

namespace Nordstein.Core.AI.Tests;

[TestClass]
public sealed class ToolGeneratorTests : BaseTest<Nordstein.Core.AI.Module>
{
    [TestMethod]
    public async Task ToolArgumentGenerator_CreateAsync_ReturnsValidArgument()
    {
        IServiceProvider services = GetServices();
        var generator = services.GetRequiredService<IDomainObjectGenerator<IToolArgument>>();

        IToolArgument argument = await generator.CreateAsync(CancellationToken);

        argument.Should().NotBeNull();
        argument.Name.Should().NotBeNullOrWhiteSpace();
        argument.Type.Should().Be(typeof(object));
        argument.DefaultValue.Should().BeNull();
        argument.Invoking(candidate => candidate.Validate()).Should().NotThrow();
    }

    [TestMethod]
    public async Task ToolArgumentsGenerator_CreateAsync_ReturnsNone()
    {
        IServiceProvider services = GetServices();
        var generator = services.GetRequiredService<IDomainObjectGenerator<ToolArguments>>();

        ToolArguments arguments = await generator.CreateAsync(CancellationToken);

        arguments.Should().BeSameAs(ToolArguments.None);
    }

    [TestMethod]
    public async Task ToolSpecificationGenerator_CreateAsync_ReturnsValidSpecification()
    {
        IServiceProvider services = GetServices();
        var generator = services.GetRequiredService<IDomainObjectGenerator<ToolSpecification>>();

        ToolSpecification specification = await generator.CreateAsync(CancellationToken);

        specification.Should().NotBeNull();
        specification.Name.Should().NotBeNullOrWhiteSpace();
        specification.Description.Should().NotBeNullOrWhiteSpace();
        specification.Arguments.Should().NotBeNull();
        specification.Invoking(candidate => candidate.Validate()).Should().NotThrow();
    }
}
