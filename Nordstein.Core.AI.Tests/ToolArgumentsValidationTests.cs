using System.ComponentModel.DataAnnotations;
using AwesomeAssertions;
using Nordstein.Core.AI.Tools;
using Nordstein.Core.Common.Validation;
using NSubstitute;

namespace Nordstein.Core.AI.Tests;

[TestClass]
public sealed class ToolArgumentsValidationTests
{
    [TestMethod]
    public void Validate_WithNoArguments_DoesNotThrow()
    {
        ToolArguments.None.Invoking(arguments => arguments.Validate()).Should().NotThrow();
    }

    [TestMethod]
    public void Validate_WithValidArguments_DoesNotThrow()
    {
        // FromJsonSchema already validates on construction; re-validating must stay clean.
        ToolArguments toolArgs = ToolArguments.FromJsonSchema(
            """{"type":"object","properties":{"city":{"type":"string"}},"required":["city"]}""");

        toolArgs.Invoking(arguments => arguments.Validate()).Should().NotThrow();
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

        var toolArgs = new ToolArguments([badArgument]);

        toolArgs.Invoking(arguments => arguments.Validate()).Should().Throw<ValidationException>();
    }
}
