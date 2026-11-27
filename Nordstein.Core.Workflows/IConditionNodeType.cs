namespace Nordstein.Core.Workflows;

/// <summary>
/// The kernel's <c>condition</c> node type (<c>condition@1</c>): compares values with rules and takes the branch
/// <see cref="True"/> or <see cref="False"/>. A product adds it to its catalogue by resolving it from
/// <see cref="Module"/>'s container; the kernel never adds it on its own.
/// </summary>
/// <remarks>
/// <para>
/// Configuration: exactly one of <c>all</c> (every rule holds) and <c>any</c> (at least one rule holds), a non-empty array
/// of rules <c>{ "left": …, "op": …, "right": … }</c> with no other keys. <c>all</c>, <c>any</c>, each rule and each
/// <c>op</c> are written literally; <c>left</c> and <c>right</c> may be or hold data references. The rules are evaluated in
/// order, and evaluation stops at the first rule that decides (a false one under <c>all</c>, a true one under
/// <c>any</c>). The output is <c>{ "result": true | false }</c>, and the node completes on the branch of that name.
/// </para>
/// <para>
/// A value counts as a number when it is a JSON number that fits a <see cref="decimal"/>, or a string that parses as one
/// in invariant notation (sign, decimal point and exponent allowed, a thousands separator never: <c>"1,50"</c> stays
/// text). A value's text is a string's own text, nothing for <c>null</c>, <c>true</c> or <c>false</c> for a boolean, and
/// the JSON text otherwise. The operators:
/// </para>
/// <list type="bullet">
/// <item><description><c>eq</c>, <c>neq</c>: two numbers are compared numerically (<c>5</c> equals <c>"5.0"</c>);
/// otherwise, when either side is an object or an array, the two are compared by JSON content; otherwise by text,
/// ignoring case, culture-independently (ordinal). A missing value resolves to <c>null</c>, whose text is empty, so it
/// equals <c>""</c>.</description></item>
/// <item><description><c>contains</c>: on an array <c>left</c>, whether an item equals <c>right</c> as <c>eq</c> compares;
/// otherwise whether <c>left</c>'s text contains <c>right</c>'s text, ignoring case.</description></item>
/// <item><description><c>gt</c>, <c>lt</c>: whether two numbers compare so; false unless both are numbers.</description></item>
/// <item><description><c>in</c>: whether <c>right</c> is an array holding an item equal to <c>left</c>; false when
/// <c>right</c> is not an array.</description></item>
/// <item><description><c>matches</c>: whether <c>right</c>, a .NET regular expression (culture-invariant, at most one
/// second per match), matches <c>left</c>'s text; false when <c>right</c> is not a string.</description></item>
/// </list>
/// <para>
/// <see cref="INodeType.ValidateConfig"/> refuses what the input schema cannot express: neither or both of <c>all</c> and
/// <c>any</c>, no rule, a reference in place of a literal part, a literal <c>matches</c> pattern that does not compile, a
/// literal <c>in</c> right side that is not an array. A pattern that comes from data and does not compile, and a match
/// that takes longer than one second, fail the step with a reason naming the rule
/// (<c>config.all[0].right is not a regular expression.</c>), as does an input of another shape when the node is executed
/// directly. The type is <see cref="INodeType.ReadOnly"/>, has no side effects and is stateless: one instance serves
/// every run concurrently.
/// </para>
/// </remarks>
public interface IConditionNodeType : INodeType
{
    /// <summary>
    /// The type's name, <c>condition</c>.
    /// </summary>
    const string TypeName = "condition";

    /// <summary>
    /// The branch taken when the rules hold, <c>true</c>.
    /// </summary>
    const string True = "true";

    /// <summary>
    /// The branch taken when the rules do not hold, <c>false</c>.
    /// </summary>
    const string False = "false";
}
