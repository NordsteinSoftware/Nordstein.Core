namespace Nordstein.Core.Workflows;

/// <summary>
/// The kernel's <c>for-each</c> node type (<c>for-each@1</c>): an <see cref="IIteratingNodeType"/> that runs the nodes
/// behind its <see cref="LoopBranches.Body"/> edges once per item of an array, then completes on
/// <see cref="LoopBranches.Done"/>. A product adds it to its catalogue by resolving it from <see cref="Module"/>'s
/// container; the kernel never adds it on its own.
/// </summary>
/// <remarks>
/// <para>
/// Configuration: <c>items</c> (required), the array to iterate, as a rule a whole-string data reference such as
/// <c>{{steps.read.output.lines}}</c> (a literal array is allowed); <c>maxItems</c> (optional), a literal whole number from
/// 1 to <see cref="MaxItemsLimit"/>, default <see cref="DefaultMaxItems"/>. An array with more items than that fails the
/// step; so does an <c>items</c> that is not an array once resolved, a missing value included. Inside the body
/// <c>{{item.&lt;path&gt;}}</c> and <c>{{index}}</c> read the current item and its position.
/// </para>
/// <para>
/// Output: <c>{ "count": n, "results": [ … ] }</c>, <c>results</c> holding what the body's last executed node produced per
/// item, in order (<c>null</c> for an iteration whose body reached no node); downstream nodes read
/// <c>{{steps.&lt;id&gt;.output.results}}</c>. The kernel runs the loop: <see cref="INodeType.ExecuteAsync"/> only reports
/// a failure. <see cref="IIteratingNodeType.MaxItems"/> throws an <see cref="ArgumentException"/> for a configuration
/// that is not an object or whose <c>maxItems</c> <see cref="INodeType.ValidateConfig"/> refuses; the interpreter calls it
/// only for a validated graph. The type has no side effects, is not <see cref="INodeType.ReadOnly"/>, and is stateless: one
/// instance serves every run concurrently.
/// </para>
/// </remarks>
public interface IForEachNodeType : IIteratingNodeType
{
    /// <summary>
    /// The type's name, <c>for-each</c>.
    /// </summary>
    const string TypeName = "for-each";

    /// <summary>
    /// The most items a node iterates when its configuration sets no <c>maxItems</c>: 20.
    /// </summary>
    const int DefaultMaxItems = 20;

    /// <summary>
    /// The highest <c>maxItems</c> a configuration may set: 100.
    /// </summary>
    const int MaxItemsLimit = 100;
}
