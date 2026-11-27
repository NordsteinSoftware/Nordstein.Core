using AwesomeAssertions;
using Nordstein.Core.Common.Collections;

namespace Nordstein.Core.Common.Tests.Collections;

[TestClass]
public sealed class CollectionEqualityTests
{
    [TestMethod]
    public void SetEqualTo_SameElementsAnyOrderOrRepetition_EqualAndSameHash()
    {
        int[] left = [3, 1, 2];
        int[] right = [2, 3, 1, 1];

        left.SetEqualTo(right).Should().BeTrue();
        right.SetEqualTo(left).Should().BeTrue();
        left.SetHashCode().Should().Be(right.SetHashCode());
    }

    [TestMethod]
    public void SetEqualTo_DifferentElements_NotEqual()
    {
        int[] left = [1, 2];

        left.SetEqualTo([1, 3]).Should().BeFalse();
        left.SetEqualTo([1]).Should().BeFalse();
        left.SetEqualTo([1, 2, 3]).Should().BeFalse();
        Array.Empty<int>().SetEqualTo([]).Should().BeTrue();
    }

    [TestMethod]
    public void SetEqualTo_CustomComparer_DecidesEqualityAndHash()
    {
        string[] left = ["A", "b"];
        string[] right = ["B", "a"];

        left.SetEqualTo(right).Should().BeFalse();
        left.SetEqualTo(right, StringComparer.OrdinalIgnoreCase).Should().BeTrue();
        left.SetHashCode(StringComparer.OrdinalIgnoreCase).Should().Be(right.SetHashCode(StringComparer.OrdinalIgnoreCase));
    }

    [TestMethod]
    public void SetEqualTo_NullElements_CountAsOneElement()
    {
        string?[] left = [null, "a"];
        string?[] right = ["a", null, null];

        left.SetEqualTo(right).Should().BeTrue();
        left.SetHashCode().Should().Be(right.SetHashCode());
    }

    [TestMethod]
    public void DictionaryEqualTo_SameEntriesAnyOrder_EqualAndSameHash()
    {
        var left = new Dictionary<string, int?> { ["a"] = 1, ["b"] = null };
        var right = new Dictionary<string, int?> { ["b"] = null, ["a"] = 1 };

        left.DictionaryEqualTo(right).Should().BeTrue();
        left.DictionaryHashCode().Should().Be(right.DictionaryHashCode());
    }

    [TestMethod]
    public void DictionaryEqualTo_DifferentValueKeyOrCount_NotEqual()
    {
        var left = new Dictionary<string, int?> { ["a"] = 1, ["b"] = null };

        left.DictionaryEqualTo(new Dictionary<string, int?> { ["a"] = 1, ["b"] = 2 }).Should().BeFalse();
        left.DictionaryEqualTo(new Dictionary<string, int?> { ["a"] = 1, ["c"] = null }).Should().BeFalse();
        left.DictionaryEqualTo(new Dictionary<string, int?> { ["a"] = 1 }).Should().BeFalse();
    }

    [TestMethod]
    public void DictionaryEqualTo_CustomValueComparer_DecidesEqualityAndHash()
    {
        var left = new Dictionary<string, string> { ["k"] = "X" };
        var right = new Dictionary<string, string> { ["k"] = "x" };

        left.DictionaryEqualTo(right).Should().BeFalse();
        left.DictionaryEqualTo(right, StringComparer.OrdinalIgnoreCase).Should().BeTrue();
        left.DictionaryHashCode(StringComparer.OrdinalIgnoreCase).Should().Be(right.DictionaryHashCode(StringComparer.OrdinalIgnoreCase));
    }

    [TestMethod]
    public void DictionaryEqualTo_KeysUnderTheRightComparer_FoundThroughIt()
    {
        // The lookup goes through the right dictionary, so its key comparer decides whether a key is present.
        var left = new Dictionary<string, int> { ["Key"] = 1 };
        var right = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase) { ["key"] = 1 };

        left.DictionaryEqualTo(right).Should().BeTrue();
    }

    [TestMethod]
    public void Methods_NullArguments_Throw()
    {
        int[] some = [1];
        var dictionary = new Dictionary<string, int>();

        ((Action)(() => NullPassing.Ints().SetEqualTo(some))).Should().Throw<ArgumentNullException>();
        ((Action)(() => some.SetEqualTo(NullPassing.Ints()))).Should().Throw<ArgumentNullException>();
        ((Action)(() => NullPassing.Ints().SetHashCode())).Should().Throw<ArgumentNullException>();
        ((Action)(() => NullPassing.Dictionary().DictionaryEqualTo(dictionary))).Should().Throw<ArgumentNullException>();
        ((Action)(() => dictionary.DictionaryEqualTo(NullPassing.Dictionary()))).Should().Throw<ArgumentNullException>();
        ((Action)(() => NullPassing.Dictionary().DictionaryHashCode())).Should().Throw<ArgumentNullException>();
    }
}

// WHY: a caller compiled without nullable annotations can pass null; the null-forgiving operator is banned, so
// these helpers hand a null reference on with nullable analysis off.
#nullable disable
internal static class NullPassing
{
    public static int[] Ints() => null;

    public static Dictionary<string, int> Dictionary() => null;
}
#nullable restore
