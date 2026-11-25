using System.ComponentModel.DataAnnotations;
using System.Reflection;
using AwesomeAssertions;
using Nordstein.Core.Common.Validation;
using Check = Nordstein.Core.Common.Validation.Validation;

namespace Nordstein.Core.Common.Tests;

/// <summary>
/// Pins the member-name contract of the <see cref="Nordstein.Core.Common.Validation.Validation"/>
/// helpers: every failure names exactly the member it was asked about, so a consumer can map it to
/// a form field via <see cref="ValidationResult.MemberNames"/>. An entity-wide rule names none.
/// </summary>
[TestClass]
public sealed class ValidationMemberNameTests
{
    private const string Member = "Field";

    private enum Colour
    {
        Red,
    }

    /// <summary>One failing call per public helper overload, each with <see cref="Member"/>.</summary>
    public static IEnumerable<object[]> FailingChecks =>
    [
        [nameof(Check.NotNull), (Func<ValidationResult>)(() => Check.NotNull(null, Member))],
        [nameof(Check.Null), (Func<ValidationResult>)(() => Check.Null(new object(), Member))],
        [nameof(Check.NotNullOrWhiteSpace), (Func<ValidationResult>)(() => Check.NotNullOrWhiteSpace(" ", Member))],
        [nameof(Check.NotNullOrEmpty), (Func<ValidationResult>)(() => Check.NotNullOrEmpty("", Member))],
        [nameof(Check.NotDefault), (Func<ValidationResult>)(() => Check.NotDefault(Guid.Empty, Member))],
        [nameof(Check.InPast), (Func<ValidationResult>)(() => Check.InPast(DateTimeOffset.MaxValue, Member))],
        [nameof(Check.InFuture), (Func<ValidationResult>)(() => Check.InFuture(DateTimeOffset.MinValue, Member))],
        [nameof(Check.NotBefore), (Func<ValidationResult>)(() => Check.NotBefore(DateTimeOffset.MinValue, DateTimeOffset.MaxValue, Member))],
        [nameof(Check.NotAfter), (Func<ValidationResult>)(() => Check.NotAfter(DateTimeOffset.MaxValue, DateTimeOffset.MinValue, Member))],
        [nameof(Check.NotNegative), (Func<ValidationResult>)(() => Check.NotNegative(-1m, Member))],
        ["Positive(decimal)", (Func<ValidationResult>)(() => Check.Positive(0m, Member))],
        [nameof(Check.LessThan), (Func<ValidationResult>)(() => Check.LessThan(1m, 1m, Member))],
        [nameof(Check.LessThanOrEqual), (Func<ValidationResult>)(() => Check.LessThanOrEqual(2m, 1m, Member))],
        ["GreaterThan(decimal)", (Func<ValidationResult>)(() => Check.GreaterThan(1m, 1m, Member))],
        [nameof(Check.GreaterThanOrEqual), (Func<ValidationResult>)(() => Check.GreaterThanOrEqual(0m, 1m, Member))],
        [nameof(Check.HasCount), (Func<ValidationResult>)(() => Check.HasCount(Array.Empty<int>(), 1, Member))],
        [nameof(Check.MaxLength), (Func<ValidationResult>)(() => Check.MaxLength("ab", 1, Member))],
        [nameof(Check.MinLength), (Func<ValidationResult>)(() => Check.MinLength("a", 2, Member))],
        [nameof(Check.ExactLength), (Func<ValidationResult>)(() => Check.ExactLength("a", 2, Member))],
        ["NotEmpty(string)", (Func<ValidationResult>)(() => Check.NotEmpty("", Member))],
        ["NotEmpty(collection)", (Func<ValidationResult>)(() => Check.NotEmpty(Array.Empty<int>(), Member))],
        [nameof(Check.Matches), (Func<ValidationResult>)(() => Check.Matches("a", "^b$", Member))],
        [nameof(Check.ValidUri), (Func<ValidationResult>)(() => Check.ValidUri("not a uri", Member))],
        [nameof(Check.Defined), (Func<ValidationResult>)(() => Check.Defined((Colour)42, Member))],
        [nameof(Check.Json), (Func<ValidationResult>)(() => Check.Json("{", Member))],
        [nameof(Check.InRange), (Func<ValidationResult>)(() => Check.InRange(0, 1, 2, Member))],
        ["GreaterThan(int)", (Func<ValidationResult>)(() => Check.GreaterThan(1, 1, Member))],
        ["Positive(TimeSpan)", (Func<ValidationResult>)(() => Check.Positive(TimeSpan.Zero, Member))],
        [nameof(Check.True), (Func<ValidationResult>)(() => Check.True(false, Member))],
    ];

    [TestMethod]
    [DynamicData(nameof(FailingChecks))]
    public void Failure_NamesExactlyTheCheckedMember(string check, Func<ValidationResult> failing)
    {
        ValidationResult result = failing();

        result.Should().NotBe(ValidationResult.Success, check);
        result.MemberNames.Should().Equal([Member], check);
    }

    [TestMethod]
    public void FailingChecks_CoverEveryHelperOverload()
    {
        int helpers = typeof(Check).GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Count(m => m.ReturnType == typeof(ValidationResult));

        FailingChecks.Should().HaveCount(helpers, "a new helper must be added to FailingChecks");
    }

    [TestMethod]
    public void Failure_WithoutExplicitName_NamesTheArgumentExpression()
    {
        int count = 0;

        Check.InRange(count, 1, 2).MemberNames.Should().Equal(nameof(count));
    }

    [TestMethod]
    public void Validate_MemberRule_ExceptionNamesTheMember()
    {
        var entity = new Order { Quantity = 0 };

        var exception = entity.Invoking(e => e.Validate()).Should().Throw<ValidationException>().Which;

        exception.ValidationResult.MemberNames.Should().Equal(nameof(Order.Quantity));
    }

    [TestMethod]
    public void Validate_EntityWideRule_ExceptionNamesNoMember()
    {
        var entity = new Order { Quantity = 1, Cancelled = true, Shipped = true };

        var exception = entity.Invoking(e => e.Validate()).Should().Throw<ValidationException>().Which;

        exception.ValidationResult.MemberNames.Should().BeEmpty();
    }

    private sealed class Order : IValidatableObject
    {
        public int Quantity { get; init; }

        public bool Cancelled { get; init; }

        public bool Shipped { get; init; }

        public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
        {
            yield return Check.GreaterThan(Quantity, 0, nameof(Quantity));

            if (Cancelled && Shipped)
                yield return new ValidationResult("A cancelled order cannot be shipped");
        }
    }
}
