using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using Nordstein.Core.Testing.Fences.Internal;

namespace Nordstein.Core.Testing.Fences;

/// <summary>
/// The scans an architecture fence is built from: a test that fails the build when a structural
/// rule is broken (a type takes a dependency it must not, a static helper appears, a type sits in
/// the wrong namespace, an assembly calls a member it must not).
/// </summary>
/// <remarks>
/// <para>
/// Each scan returns sorted, distinct, human-readable lines, so a fence asserts
/// <c>Should().Equal(allowlist)</c>: a new offender fails the test, and so does a stale allowlist
/// entry. Every scan includes nested and compiler-generated types (closures, async state machines),
/// where a naive scan misses what the code really does.
/// </para>
/// <para>
/// A fence is only as good as its proof that it can fail. Plant a deliberately violating fixture
/// type in the fence's own test project, one per branch of the scan, and assert the scan of that
/// test assembly reports each: a scan that silently stopped finding anything fails there first.
/// </para>
/// <para>All members are pure reads of reflection or metadata and safe to call concurrently.</para>
/// </remarks>
public static partial class FenceScanExtensions
{
    private const BindingFlags DeclaredStatics =
        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.DeclaredOnly;

    /// <summary>
    /// Every constructor and method parameter of the assembly's types that are not interfaces:
    /// static, abstract, nested and compiler-generated types included, instance and static
    /// constructors and methods, every visibility.
    /// </summary>
    /// <param name="assembly">The assembly to scan.</param>
    /// <returns>The parameters, in no particular order.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="assembly"/> is null.</exception>
    public static IEnumerable<InjectedParameter> InjectedParameters(this Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        return assembly.GetTypes()
            .Where(type => !type.IsInterface)
            .SelectMany(type => type
                .GetConstructors(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                .Cast<MethodBase>()
                .Concat(type.GetMethods(
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance
                    | BindingFlags.Static | BindingFlags.DeclaredOnly))
                .SelectMany(member => member.GetParameters()
                    .Select(parameter => new InjectedParameter(type, member, parameter))));
    }

    /// <summary>
    /// The parameters of <paramref name="assembly"/> whose type reaches a fenced type
    /// (<see cref="Mentions"/>), as sorted, distinct <c>Owner.Full.Name: ParameterType</c> lines
    /// (<see cref="InjectedParameter.ToString"/>).
    /// </summary>
    /// <param name="assembly">The assembly to scan.</param>
    /// <param name="isFenced">Whether a type is one the fence forbids.</param>
    /// <returns>The offending parameters, sorted ordinally.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="assembly"/> or <paramref name="isFenced"/> is null.</exception>
    public static string[] ParametersMentioning(this Assembly assembly, Func<Type, bool> isFenced)
    {
        ArgumentNullException.ThrowIfNull(isFenced);
        return assembly.InjectedParameters()
            .Where(parameter => parameter.ParameterType.Mentions(isFenced))
            .Select(parameter => parameter.ToString())
            .Distinct()
            .Order(StringComparer.Ordinal)
            .ToArray();
    }

    /// <summary>
    /// Whether <paramref name="type"/> is a fenced type or reaches one: through an implemented
    /// interface or a base type, an element type (array, by-reference, pointer), a generic argument
    /// (<c>Lazy&lt;T&gt;</c>, <c>Func&lt;T&gt;</c>, <c>IEnumerable&lt;T&gt;</c>, …), a
    /// generic-parameter constraint, or a delegate's <c>Invoke</c> return or parameter type.
    /// </summary>
    /// <param name="type">The type to inspect.</param>
    /// <param name="isFenced">Whether a type is one the fence forbids; called once per type reached.</param>
    /// <returns>True when a fenced type is reached; cycles in the type graph are walked once.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="type"/> or <paramref name="isFenced"/> is null.</exception>
    public static bool Mentions(this Type type, Func<Type, bool> isFenced)
    {
        ArgumentNullException.ThrowIfNull(type);
        ArgumentNullException.ThrowIfNull(isFenced);
        return Walk(type, isFenced, []);
    }

    /// <summary>
    /// The static members of the assembly's types that the "no statics" rule forbids, as sorted
    /// <c>Namespace.Type.Member</c> lines: a non-private method or property that is not an
    /// extension method, an operator or an accessor; any field that is neither <c>const</c> nor
    /// <c>readonly</c> (a field-like event's backing field included, reported under the event's
    /// name); and a private static auto-property with a setter, reported under the property's name.
    /// </summary>
    /// <remarks>
    /// Never reported: compiler-generated types (a <c>&lt;</c> in the name: closures, lambda caches,
    /// state machines, file-local types), the <c>Coverlet.</c> hit tracker a coverage run weaves into
    /// each assembly, and the types <paramref name="isExempt"/> exempts (a product's entry point or
    /// generated migrations). A <c>readonly</c> field holding a mutable object is static state the
    /// scan cannot see.
    /// </remarks>
    /// <param name="assembly">The assembly to scan.</param>
    /// <param name="isExempt">Exempts a type; null exempts none.</param>
    /// <returns>The offending members, sorted ordinally.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="assembly"/> is null.</exception>
    public static string[] StaticMembers(this Assembly assembly, Func<Type, bool>? isExempt = null)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        return assembly.GetTypes()
            .Where(type => !IsGenerated(type) && isExempt?.Invoke(type) != true)
            .SelectMany(type => StaticMembersOf(type).Select(member => $"{type.FullName}.{member}"))
            .Distinct()
            .Order(StringComparer.Ordinal)
            .ToArray();
    }

    /// <summary>
    /// The full names of the assembly's internal types that do not live in an <c>Internal</c>
    /// namespace (one whose last segment is <c>Internal</c>, as in <c>Product.Concept.Internal</c>),
    /// sorted: the rule that every internal type sits in an <c>Internal/</c> folder with the
    /// matching namespace, checked on the compiled assembly.
    /// </summary>
    /// <remarks>
    /// Only top-level types are checked: a nested type lives in the file of the type that declares
    /// it, whatever its accessibility. Never reported: compiler-generated types (a
    /// <c>[CompilerGenerated]</c> attribute or a <c>&lt;</c> in the name: file-local types, embedded
    /// attributes, anonymous types), the <c>Coverlet.</c> hit tracker, and the types
    /// <paramref name="isExempt"/> exempts. The check reads namespaces, not folders; pair it with a
    /// namespace-matches-folder check (IDE0130, or a source scan) to cover the folder too.
    /// </remarks>
    /// <param name="assembly">The assembly to scan.</param>
    /// <param name="isExempt">Exempts a type; null exempts none.</param>
    /// <returns>The misplaced types' full names, sorted ordinally.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="assembly"/> is null.</exception>
    public static string[] InternalTypesOutsideInternalNamespaces(this Assembly assembly, Func<Type, bool>? isExempt = null)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        return assembly.GetTypes()
            .Where(type => !type.IsNested
                && !type.IsPublic
                && !IsGenerated(type)
                && !type.IsDefined(typeof(CompilerGeneratedAttribute), inherit: false)
                && type.Namespace?.EndsWith(".Internal", StringComparison.Ordinal) != true
                && isExempt?.Invoke(type) != true)
            .Select(type => type.FullName ?? type.Name)
            .Order(StringComparer.Ordinal)
            .ToArray();
    }

    /// <summary>
    /// Every member the assembly's metadata references, as sorted, distinct
    /// <c>Declaring.Type::Member</c> lines: members of other assemblies (a call, a field access,
    /// a constructor), members of generic instantiations under their generic type's name
    /// (<c>System.Collections.Generic.List`1::Add</c>), and vararg call sites. Read from the
    /// metadata tables, so a reference made only inside a compiler-generated type is found, and no
    /// resolution step can skip one silently. A call to a non-generic member of the assembly itself
    /// is a definition, not a reference, and is not listed.
    /// </summary>
    /// <param name="assembly">The assembly to scan; it must have been loaded from a file.</param>
    /// <returns>The referenced members, sorted ordinally.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="assembly"/> is null.</exception>
    /// <exception cref="InvalidOperationException"><paramref name="assembly"/> has no file to read (a dynamic or in-memory assembly).</exception>
    public static string[] ReferencedMemberNames(this Assembly assembly)
        => ReadMetadata(assembly, metadata => metadata.MemberReferences
            .Select(handle => metadata.GetMemberReference(handle))
            .Select(member => metadata.ParentName(member.Parent) + "::" + metadata.GetString(member.Name)));

    /// <summary>
    /// Every type the assembly's metadata references, as sorted, distinct full names
    /// (<c>Namespace.Type</c>, nested types as <c>Namespace.Outer+Inner</c>): types of other
    /// assemblies used anywhere, including only in a <c>typeof</c>, a signature or a
    /// compiler-generated type.
    /// </summary>
    /// <param name="assembly">The assembly to scan; it must have been loaded from a file.</param>
    /// <returns>The referenced types, sorted ordinally.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="assembly"/> is null.</exception>
    /// <exception cref="InvalidOperationException"><paramref name="assembly"/> has no file to read (a dynamic or in-memory assembly).</exception>
    public static string[] ReferencedTypeNames(this Assembly assembly)
        => ReadMetadata(assembly, metadata => metadata.TypeReferences.Select(metadata.FullName));

    /// <summary>
    /// The C# spelling of a type's name without namespaces: generic arguments in angle brackets
    /// (<c>Dictionary&lt;String, List&lt;Int32&gt;&gt;</c>), arrays with their rank
    /// (<c>Int32[,]</c>), by-reference and pointer types with <c>&amp;</c> and <c>*</c>.
    /// </summary>
    /// <param name="type">The type to spell.</param>
    /// <returns>The spelling.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="type"/> is null.</exception>
    public static string Display(this Type type)
    {
        ArgumentNullException.ThrowIfNull(type);
        if (type.GetElementType() is { } element)
        {
            string suffix = type.IsArray ? "[" + new string(',', type.GetArrayRank() - 1) + "]"
                : type.IsByRef ? "&"
                : "*";
            return element.Display() + suffix;
        }

        if (!type.IsGenericType)
        {
            return type.Name;
        }

        // A type nested in a generic type is generic without a backtick in its own name.
        string name = Arity().Replace(type.Name, string.Empty);
        return $"{name}<{string.Join(", ", type.GetGenericArguments().Select(Display))}>";
    }

    private static bool IsGenerated(Type type)
        => type.Name.Contains('<', StringComparison.Ordinal)
            || type.Namespace?.StartsWith("Coverlet.", StringComparison.Ordinal) == true;

    private static IEnumerable<string> StaticMembersOf(Type type)
    {
        IEnumerable<string> methods = type.GetMethods(DeclaredStatics)
            .Where(method => !method.IsPrivate
                && !method.IsSpecialName
                && !method.IsDefined(typeof(ExtensionAttribute), inherit: false))
            .Select(method => method.Name);
        IEnumerable<string> properties = type.GetProperties(DeclaredStatics)
            .Where(property => (property.GetMethod ?? property.SetMethod) is { IsPrivate: false })
            .Select(property => property.Name);
        IEnumerable<string> fields = type.GetFields(DeclaredStatics)
            .Where(field => !field.IsLiteral && !field.IsInitOnly)
            .Select(field => BackingField().Match(field.Name) is { Success: true } backing
                ? backing.Groups["property"].Value
                : field.Name);
        return methods.Concat(properties).Concat(fields);
    }

    private static bool Walk(Type type, Func<Type, bool> isFenced, HashSet<Type> seen)
    {
        if (!seen.Add(type))
        {
            return false;
        }

        if (isFenced(type))
        {
            return true;
        }

        IEnumerable<Type> reachable = type.GetInterfaces();
        if (type.BaseType is { } baseType)
        {
            reachable = reachable.Append(baseType);
        }

        if (type.GetElementType() is { } element)
        {
            reachable = reachable.Append(element);
        }

        if (type.IsGenericType)
        {
            reachable = reachable.Concat(type.GetGenericArguments());
        }

        if (type.IsGenericParameter)
        {
            reachable = reachable.Concat(type.GetGenericParameterConstraints());
        }

        if (type.IsSubclassOf(typeof(Delegate)) && type.GetMethod("Invoke") is { } invoke)
        {
            reachable = reachable
                .Append(invoke.ReturnType)
                .Concat(invoke.GetParameters().Select(parameter => parameter.ParameterType));
        }

        return reachable.Any(next => Walk(next, isFenced, seen));
    }

    private static string[] ReadMetadata(Assembly assembly, Func<MetadataReader, IEnumerable<string>> names)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        if (assembly.IsDynamic || string.IsNullOrEmpty(assembly.Location))
        {
            throw new InvalidOperationException(
                $"The assembly {assembly.GetName().Name} has no file to read its metadata from; scan an assembly loaded from disk.");
        }

        using FileStream stream = File.OpenRead(assembly.Location);
        using var pe = new PEReader(stream);
        return names(pe.GetMetadataReader())
            .Distinct()
            .Order(StringComparer.Ordinal)
            .ToArray();
    }

    [GeneratedRegex("`[0-9]+")]
    private static partial Regex Arity();

    [GeneratedRegex("^<(?<property>[^>]+)>k__BackingField$")]
    private static partial Regex BackingField();
}
