using System.ComponentModel.DataAnnotations;
using AwesomeAssertions;
using Nordstein.Core.AI.Tools;
using Nordstein.Core.Common.Validation;
using NSubstitute;

namespace Nordstein.Core.AI.Tests;

[TestClass]
public sealed class ToolSpecificationTests
{
    [TestMethod]
    public void Constructor_SetsNameDescriptionAndArguments()
    {
        ToolArguments arguments = ToolArguments.None;

        var specification = new ToolSpecification("get_weather", "Gets the weather", arguments);

        specification.Name.Should().Be("get_weather");
        specification.Description.Should().Be("Gets the weather");
        specification.Arguments.Should().BeSameAs(arguments);
    }

    [TestMethod]
    public void Validate_WithValidSpecification_DoesNotThrow()
    {
        var specification = new ToolSpecification("get_weather", "Gets the weather", ToolArguments.None);

        specification.Invoking(candidate => candidate.Validate()).Should().NotThrow();
    }

    [TestMethod]
    public void Validate_WithWhitespaceName_Throws()
    {
        var specification = new ToolSpecification("   ", "Gets the weather", ToolArguments.None);

        specification.Invoking(candidate => candidate.Validate()).Should().Throw<ValidationException>();
    }

    [TestMethod]
    public void Validate_WithEmptyDescription_Throws()
    {
        var specification = new ToolSpecification("get_weather", string.Empty, ToolArguments.None);

        specification.Invoking(candidate => candidate.Validate()).Should().Throw<ValidationException>();
    }

    [TestMethod]
    public void Validate_PropagatesArgumentValidationFailures()
    {
        var badArgument = Substitute.For<IToolArgument>();
        badArgument.Name.Returns("x");
        badArgument.Type.Returns(typeof(string));
        badArgument.IsRequired.Returns(false);
        badArgument.JsonSchema.Returns("""{"type":"string"}""");
        IEnumerable<ValidationResult> failures = [new ValidationResult("argument is invalid")];
        badArgument.Validate(Arg.Any<ValidationContext>()).Returns(failures);

        var specification = new ToolSpecification("get_weather", "Gets the weather", new ToolArguments([badArgument]));

        specification.Invoking(candidate => candidate.Validate()).Should().Throw<ValidationException>();
    }
}
