using System.ComponentModel.DataAnnotations;
using AwesomeAssertions;
using V = Nordstein.Core.Common.Validation.Validation;

namespace Nordstein.Core.Common.Tests;

[TestClass]
public sealed class ValidationCollectionTests
{
    [TestMethod]
    public void NotNullOrDefault_Value_Succeeds()
    {
        Guid? id = Guid.NewGuid();

        V.NotNullOrDefault(id).Should().Be(ValidationResult.Success);
    }

    [TestMethod]
    public void NotNullOrDefault_NullOrDefault_FailsNamingTheMember()
    {
        Guid? none = null;
        Guid? empty = Guid.Empty;

        ValidationResult? nullResult = V.NotNullOrDefault(none);
        ValidationResult? defaultResult = V.NotNullOrDefault(empty, "UserId");

        nullResult.Should().NotBeNull();
        nullResult.ErrorMessage.Should().Be("none cannot be null or default");
        nullResult.MemberNames.Should().Equal("none");
        defaultResult.Should().NotBeNull();
        defaultResult.ErrorMessage.Should().Be("UserId cannot be null or default");
    }

    [TestMethod]
    public void Empty_EmptyCollection_Succeeds_NonEmptyFails()
    {
        IReadOnlyCollection<int> none = [];
        IReadOnlyCollection<int> some = [1];

        V.Empty(none).Should().Be(ValidationResult.Success);
        ValidationResult? result = V.Empty(some, "GroupIds");
        result.Should().NotBeNull();
        result.ErrorMessage.Should().Be("GroupIds must be empty");
        result.MemberNames.Should().Equal("GroupIds");
    }

    [TestMethod]
    public void Empty_Null_Throws()
    {
        Action act = () => InvokeWithNull(nameof(V.Empty));

        act.Should().Throw<System.Reflection.TargetInvocationException>().WithInnerException<ArgumentNullException>();
    }

    [TestMethod]
    public void NoneDefault_NoDefaultElement_Succeeds_OneDefaultFails()
    {
        Guid[] fine = [Guid.NewGuid(), Guid.NewGuid()];
        Guid[] bad = [Guid.NewGuid(), Guid.Empty];

        V.NoneDefault(fine).Should().Be(ValidationResult.Success);
        V.NoneDefault(Array.Empty<Guid>()).Should().Be(ValidationResult.Success);
        ValidationResult? result = V.NoneDefault(bad, "GroupIds");
        result.Should().NotBeNull();
        result.ErrorMessage.Should().Be("GroupIds cannot contain a default value");
    }

    [TestMethod]
    public void NoneDefault_ReferenceElements_NullIsTheDefault()
    {
        string?[] withNull = ["a", null];

        V.NoneDefault(withNull, "Names").Should().NotBe(ValidationResult.Success);
    }

    [TestMethod]
    public void NoneDefault_Null_Throws()
    {
        Action act = () => InvokeWithNull(nameof(V.NoneDefault));

        act.Should().Throw<System.Reflection.TargetInvocationException>().WithInnerException<ArgumentNullException>();
    }

    // The nullable annotations forbid a null collection; a caller without them can still pass one.
    private static void InvokeWithNull(string method)
        => typeof(V).GetMethods().Single(candidate => candidate.Name == method).MakeGenericMethod(typeof(int)).Invoke(null, [null, "Values"]);
}
