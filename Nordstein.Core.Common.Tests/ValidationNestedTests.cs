using System.ComponentModel.DataAnnotations;
using AwesomeAssertions;
using Check = Nordstein.Core.Common.Validation.Validation;

namespace Nordstein.Core.Common.Tests;

/// <summary>
/// <see cref="Nordstein.Core.Common.Validation.Validation.Nested"/>: a child object's failures are
/// reported under the parent member, however deep the nesting goes.
/// </summary>
[TestClass]
public sealed class ValidationNestedTests
{
    [TestMethod]
    public void Nested_ChildFailure_PrefixesTheMemberNamesAndTheMessage()
    {
        var parent = new Parent { Child = new Child { Size = 0 } };

        List<ValidationResult> results = [.. Check.Nested(parent.Child)];

        ValidationResult failure = results.Should().ContainSingle().Subject;
        failure.MemberNames.Should().Equal("parent.Child:Size");
        failure.ErrorMessage.Should().Be("parent.Child:Size must be greater than 0");
    }

    [TestMethod]
    public void Nested_MemberNameArgument_OverridesTheCallerExpression()
    {
        List<ValidationResult> results = [.. Check.Nested(new Child { Size = 0 }, "Limits")];

        results.Should().ContainSingle().Which.MemberNames.Should().Equal("Limits:Size");
    }

    [TestMethod]
    public void Nested_ValidChild_YieldsNothing()
        => Check.Nested(new Child { Size = 1 }, "Limits").Should().BeEmpty();

    [TestMethod]
    public void Nested_NullChild_YieldsNothing()
        => Check.Nested(null, "Limits").Should().BeEmpty();

    [TestMethod]
    public void Nested_ResultWithoutMemberNames_IsReportedUnderTheParentMember()
    {
        List<ValidationResult> results = [.. Check.Nested(new Unnamed(), "Limits")];

        ValidationResult failure = results.Should().ContainSingle().Subject;
        failure.MemberNames.Should().Equal("Limits");
        failure.ErrorMessage.Should().Be("Limits:Cross-field rule broken");
    }

    [TestMethod]
    public void Nested_ResultWithSeveralMemberNames_PrefixesEachOne()
    {
        List<ValidationResult> results = [.. Check.Nested(new Pair(), "Limits")];

        results.Should().ContainSingle().Which.MemberNames.Should().Equal("Limits:Low", "Limits:High");
    }

    [TestMethod]
    public void Nested_TwoLevelsDeep_ChainsThePrefixes()
    {
        var root = new Root { Parent = new Parent { Child = new Child { Size = 0 } } };

        List<ValidationResult> results = [.. Check.Nested(root.Parent, "Outer")];

        ValidationResult failure = results.Should().ContainSingle().Subject;
        failure.MemberNames.Should().Equal("Outer:Child:Size");
        failure.ErrorMessage.Should().Be("Outer:Child:Size must be greater than 0");
    }

    [TestMethod]
    public void Nested_ChildOfAnyShape_ValidatesItsAnnotations()
    {
        List<ValidationResult> results = [.. Check.Nested(new Annotated { Name = null }, "Limits")];

        results.Should().ContainSingle().Which.MemberNames.Should().Equal("Limits:Name");
    }

    private sealed class Child : IValidatableObject
    {
        public int Size { get; init; }

        public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
        {
            yield return Check.GreaterThan(Size, 0, nameof(Size));
        }
    }

    private sealed class Parent : IValidatableObject
    {
        public Child? Child { get; init; }

        public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
        {
            foreach (ValidationResult failure in Check.Nested(Child, nameof(Child)))
            {
                yield return failure;
            }
        }
    }

    private sealed class Root : IValidatableObject
    {
        public Parent? Parent { get; init; }

        public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
        {
            // The member name is spelled out: the helper prefixes whatever Parent reports.
            foreach (ValidationResult failure in Check.Nested(Parent, "Outer"))
            {
                yield return failure;
            }
        }
    }

    private sealed class Unnamed : IValidatableObject
    {
        public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
        {
            yield return new ValidationResult("Cross-field rule broken");
        }
    }

    private sealed class Pair : IValidatableObject
    {
        public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
        {
            yield return new ValidationResult("Low must not exceed High", ["Low", "High"]);
        }
    }

    private sealed class Annotated
    {
        [Required]
        public string? Name { get; init; }
    }
}
