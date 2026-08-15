using System.ComponentModel.DataAnnotations;
using AwesomeAssertions;
using Nordstein.Core.Common.Validation;
using Check = Nordstein.Core.Common.Validation.Validation;

namespace Nordstein.Core.Common.Tests;

/// <summary>
/// Covers the <see cref="Nordstein.Core.Common.Validation.Validation"/> helpers not exercised by
/// <see cref="ValidationTests"/>: <c>AsEnumerable</c>, <c>NotNull</c>/<c>Null</c>, the
/// collection/length checks, <c>Json</c>, the integer/time-span numeric checks, and <c>True</c>.
/// </summary>
[TestClass]
public sealed class ValidationCoverageTests
{
    [TestMethod]
    public void AsEnumerable_WithNull_YieldsNothing()
    {
        ValidationResult? success = null;

        success.AsEnumerable().Should().BeEmpty();
    }

    [TestMethod]
    public void AsEnumerable_WithResult_YieldsThatResult()
    {
        var result = new ValidationResult("boom");

        result.AsEnumerable().Should().ContainSingle().Which.Should().BeSameAs(result);
    }

    [TestMethod]
    public void NotNull_WithValue_ReturnsSuccess()
        => Check.NotNull(new object()).Should().Be(ValidationResult.Success);

    [TestMethod]
    public void NotNull_WithNull_ReturnsError()
    {
        ValidationResult result = Check.NotNull(null);

        result.Should().NotBe(ValidationResult.Success);
        result.ErrorMessage.Should().Contain("cannot be null");
        result.MemberNames.Should().NotBeEmpty();
    }

    [TestMethod]
    public void Null_WithNull_ReturnsSuccess()
        => Check.Null(null).Should().Be(ValidationResult.Success);

    [TestMethod]
    public void Null_WithValue_ReturnsError()
    {
        ValidationResult result = Check.Null(new object());

        result.Should().NotBe(ValidationResult.Success);
        result.ErrorMessage.Should().Contain("must be null");
    }

    [TestMethod]
    public void NotNegative_WithNegative_ReturnsError()
    {
        ValidationResult result = Check.NotNegative(-1m);

        result.Should().NotBe(ValidationResult.Success);
        result.ErrorMessage.Should().Contain("cannot be negative");
    }

    [TestMethod]
    public void NotNegative_WithZero_ReturnsSuccess()
        => Check.NotNegative(0m).Should().Be(ValidationResult.Success);

    [TestMethod]
    public void NotNegative_WithPositive_ReturnsSuccess()
        => Check.NotNegative(5m).Should().Be(ValidationResult.Success);

    [TestMethod]
    public void HasCount_WithMatchingCount_ReturnsSuccess()
        => Check.HasCount(new[] { 1, 2, 3 }, 3).Should().Be(ValidationResult.Success);

    [TestMethod]
    public void HasCount_WithWrongCount_ReturnsError()
    {
        ValidationResult result = Check.HasCount(new[] { 1, 2 }, 3);

        result.Should().NotBe(ValidationResult.Success);
        result.ErrorMessage.Should().Contain("must have 3 items");
    }

    [TestMethod]
    public void MaxLength_WithinLimit_ReturnsSuccess()
        => Check.MaxLength("abc", 5).Should().Be(ValidationResult.Success);

    [TestMethod]
    public void MaxLength_WithNull_ReturnsSuccess()
        => Check.MaxLength(null, 5).Should().Be(ValidationResult.Success);

    [TestMethod]
    public void MaxLength_OverLimit_ReturnsError()
    {
        ValidationResult result = Check.MaxLength("abcdef", 5);

        result.Should().NotBe(ValidationResult.Success);
        result.ErrorMessage.Should().Contain("cannot be longer than 5 characters");
    }

    [TestMethod]
    public void MinLength_AtLimit_ReturnsSuccess()
        => Check.MinLength("abc", 3).Should().Be(ValidationResult.Success);

    [TestMethod]
    public void MinLength_UnderLimit_ReturnsError()
    {
        ValidationResult result = Check.MinLength("ab", 3);

        result.Should().NotBe(ValidationResult.Success);
        result.ErrorMessage.Should().Contain("cannot be shorter than 3 characters");
    }

    [TestMethod]
    public void MinLength_WithNull_ReturnsError()
        => Check.MinLength(null, 1).Should().NotBe(ValidationResult.Success);

    [TestMethod]
    public void NotEmpty_String_WithContent_ReturnsSuccess()
        => Check.NotEmpty("a").Should().Be(ValidationResult.Success);

    [TestMethod]
    public void NotEmpty_String_WithEmpty_ReturnsError()
        => Check.NotEmpty(string.Empty).Should().NotBe(ValidationResult.Success);

    [TestMethod]
    public void NotEmpty_String_WithNull_ReturnsError()
        => Check.NotEmpty((string?)null).Should().NotBe(ValidationResult.Success);

    [TestMethod]
    public void NotEmpty_Collection_WithItems_ReturnsSuccess()
        => Check.NotEmpty(new[] { 1 }).Should().Be(ValidationResult.Success);

    [TestMethod]
    public void NotEmpty_Collection_WhenEmpty_ReturnsError()
    {
        ValidationResult result = Check.NotEmpty(Array.Empty<int>());

        result.Should().NotBe(ValidationResult.Success);
        result.ErrorMessage.Should().Contain("cannot be empty");
    }

    [TestMethod]
    public void Json_WithValidJson_ReturnsSuccess()
        => Check.Json("{ \"a\": 1 }").Should().Be(ValidationResult.Success);

    [TestMethod]
    public void Json_WithInvalidJson_ReturnsError()
    {
        ValidationResult result = Check.Json("{ not json");

        result.Should().NotBe(ValidationResult.Success);
        result.ErrorMessage.Should().Contain("is not valid JSON");
    }

    [TestMethod]
    public void Json_WithEmptyString_ReturnsError()
        => Check.Json(string.Empty).Should().NotBe(ValidationResult.Success);

    [TestMethod]
    public void InRange_WithinBounds_ReturnsSuccess()
        => Check.InRange(5, 1, 10).Should().Be(ValidationResult.Success);

    [TestMethod]
    public void InRange_AtBounds_ReturnsSuccess()
    {
        Check.InRange(1, 1, 10).Should().Be(ValidationResult.Success);
        Check.InRange(10, 1, 10).Should().Be(ValidationResult.Success);
    }

    [TestMethod]
    public void InRange_BelowLowerBound_ReturnsError()
    {
        ValidationResult result = Check.InRange(0, 1, 10);

        result.Should().NotBe(ValidationResult.Success);
        result.ErrorMessage.Should().Contain("must be between 1 and 10");
    }

    [TestMethod]
    public void InRange_AboveUpperBound_ReturnsError()
        => Check.InRange(11, 1, 10).Should().NotBe(ValidationResult.Success);

    [TestMethod]
    public void GreaterThan_Int_WithLargerValue_ReturnsSuccess()
        => Check.GreaterThan(5, 3).Should().Be(ValidationResult.Success);

    [TestMethod]
    public void GreaterThan_Int_WithEqualValue_ReturnsError()
    {
        ValidationResult result = Check.GreaterThan(3, 3);

        result.Should().NotBe(ValidationResult.Success);
        result.ErrorMessage.Should().Contain("must be greater than 3");
    }

    [TestMethod]
    public void GreaterThan_Int_WithSmallerValue_ReturnsError()
        => Check.GreaterThan(1, 3).Should().NotBe(ValidationResult.Success);

    [TestMethod]
    public void Positive_TimeSpan_WithPositive_ReturnsSuccess()
        => Check.Positive(TimeSpan.FromSeconds(1)).Should().Be(ValidationResult.Success);

    [TestMethod]
    public void Positive_TimeSpan_WithZero_ReturnsError()
    {
        ValidationResult result = Check.Positive(TimeSpan.Zero);

        result.Should().NotBe(ValidationResult.Success);
        result.ErrorMessage.Should().Contain("must be positive");
    }

    [TestMethod]
    public void Positive_TimeSpan_WithNegative_ReturnsError()
        => Check.Positive(TimeSpan.FromSeconds(-1)).Should().NotBe(ValidationResult.Success);

    [TestMethod]
    public void True_WithTrue_ReturnsSuccess()
        => Check.True(true).Should().Be(ValidationResult.Success);

    [TestMethod]
    public void True_WithFalse_ReturnsError()
    {
        ValidationResult result = Check.True(false);

        result.Should().NotBe(ValidationResult.Success);
        result.ErrorMessage.Should().Contain("must be true");
    }
}
