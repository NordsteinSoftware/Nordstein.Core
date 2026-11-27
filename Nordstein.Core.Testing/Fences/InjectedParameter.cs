using System.Reflection;

namespace Nordstein.Core.Testing.Fences;

/// <summary>
/// One constructor or method parameter found by <see cref="FenceScanExtensions.InjectedParameters"/>:
/// the type that declares the member, the member, and the parameter.
/// </summary>
/// <remarks>Immutable and safe to read from any thread.</remarks>
public sealed class InjectedParameter
{
    internal InjectedParameter(Type owner, MethodBase member, ParameterInfo parameter)
    {
        Owner = owner;
        Member = member;
        Parameter = parameter;
    }

    /// <summary>The type declaring <see cref="Member"/>; for a compiler-generated type, that type itself.</summary>
    public Type Owner { get; }

    /// <summary>The constructor or method taking the parameter.</summary>
    public MethodBase Member { get; }

    /// <summary>The parameter.</summary>
    public ParameterInfo Parameter { get; }

    /// <summary>The parameter's type; by-reference types keep their <c>&amp;</c>.</summary>
    public Type ParameterType => Parameter.ParameterType;

    /// <summary>
    /// <c>Owner.Full.Name: ParameterType</c>, the parameter type spelled by
    /// <see cref="FenceScanExtensions.Display"/>: the line a fence lists and compares.
    /// </summary>
    public override string ToString() => $"{Owner.FullName ?? Owner.Name}: {ParameterType.Display()}";
}
