using AwesomeAssertions;
using Nordstein.Core.AI.Serialization;

namespace Nordstein.Core.AI.Tests.Serialization;

/// <summary>
/// Exercises <see cref="OutputFormatException"/>: the members it exposes, the fixed message per
/// violation (path and kind only), and its argument guards.
/// </summary>
[TestClass]
public sealed class OutputFormatExceptionTests
{
    [TestMethod]
    [DataRow(OutputFormatViolation.Empty, "$", "The model output violates its format at $: the reply is empty.")]
    [DataRow(OutputFormatViolation.Truncated, "$", "The model output violates its format at $: the reply ends before its JSON document is complete.")]
    [DataRow(OutputFormatViolation.Malformed, "$", "The model output violates its format at $: the reply is not one well-formed JSON document.")]
    [DataRow(OutputFormatViolation.WrongType, "$.total", "The model output violates its format at $.total: the value is not of a type the schema allows.")]
    [DataRow(OutputFormatViolation.MissingRequired, "$.total", "The model output violates its format at $.total: a required property is missing.")]
    [DataRow(OutputFormatViolation.NotInEnum, "$.status", "The model output violates its format at $.status: the value is not one of the schema's enum values.")]
    [DataRow(OutputFormatViolation.UnexpectedProperty, "$.lines[0]", "The model output violates its format at $.lines[0]: the object has a property the schema does not declare.")]
    public void Ctor_PathAndViolation_ExposesBothInAFixedMessage(OutputFormatViolation violation, string path, string message)
    {
        var exception = new OutputFormatException(path, violation);

        exception.Path.Should().Be(path);
        exception.Violation.Should().Be(violation);
        exception.Message.Should().Be(message);
        exception.InnerException.Should().BeNull();
    }

    [TestMethod]
    public void Ctor_NullPath_Throws()
    {
        Action act = () => _ = new OutputFormatException((new string[1])[0], OutputFormatViolation.Empty);

        act.Should().Throw<ArgumentNullException>().WithParameterName("path");
    }

    [TestMethod]
    [DataRow("")]
    [DataRow("total")]
    [DataRow(".total")]
    public void Ctor_PathNotStartingAtTheRoot_Throws(string path)
    {
        Action act = () => _ = new OutputFormatException(path, OutputFormatViolation.WrongType);

        act.Should().Throw<ArgumentException>().WithParameterName("path");
    }

    [TestMethod]
    public void Ctor_UndefinedViolation_Throws()
    {
        Action act = () => _ = new OutputFormatException("$", (OutputFormatViolation)99);

        act.Should().Throw<ArgumentOutOfRangeException>().WithParameterName("violation");
    }

    [TestMethod]
    public void OutputFormatException_IsAFormatException()
    {
        Exception exception = new OutputFormatException("$", OutputFormatViolation.Malformed);

        exception.Should().BeAssignableTo<FormatException>();
    }
}
