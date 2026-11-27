using System.Buffers;
using System.Globalization;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using ReflectionModule = System.Reflection.Module;

namespace Nordstein.Core.Common.Settings.Internal;

/// <summary>
/// The <see cref="ISettingRegistry"/>: walks every root once, in the constructor, and answers from
/// collections it never changes afterwards, so concurrent readers need no lock.
/// </summary>
internal sealed class SettingRegistry : ISettingRegistry
{
    private const string PathSeparator = ":";
    private const string NameSeparator = "__";
    private const string Wildcard = "*";

    private const BindingFlags DeclaredProperties =
        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;

    private readonly SettingNaming naming;
    private readonly IReadOnlyList<SettingDescriptor> all;
    private readonly Dictionary<string, SettingDescriptor> concreteByName;
    private readonly IReadOnlyList<Template> templates;
    private readonly Dictionary<SettingDescriptor, Template> templateByDescriptor;

    public SettingRegistry(IEnumerable<SettingRoot> roots, SettingNaming naming)
    {
        ArgumentNullException.ThrowIfNull(roots);
        ArgumentNullException.ThrowIfNull(naming);

        CheckNaming(naming);
        this.naming = naming;

        List<Leaf> leaves = [];
        Dictionary<Type, bool> sections = [];
        foreach (SettingRoot? root in roots.ToArray())
        {
            leaves.AddRange(LeavesOf(root ?? throw new InvalidOperationException("A registered setting root is null."), sections));
        }

        CheckCollisions(leaves);

        SettingDescriptor[] descriptors = [.. leaves.Select(leaf => leaf.Descriptor)];
        this.all = Array.AsReadOnly(descriptors);
        this.concreteByName = descriptors
            .Where(descriptor => !descriptor.IsTemplate)
            .ToDictionary(descriptor => descriptor.Name, StringComparer.OrdinalIgnoreCase);
        Template[] templateList =
        [
            .. leaves
                .Where(leaf => leaf.Descriptor.IsTemplate)
                .Select(leaf => new Template(leaf.Descriptor, leaf.Segments, Array.IndexOf(leaf.Segments, Wildcard))),
        ];
        this.templates = Array.AsReadOnly(templateList);
        this.templateByDescriptor = templateList.ToDictionary(template => template.Descriptor);
    }

    public IReadOnlyList<SettingDescriptor> All => all;

    public SettingDescriptor? Find(string name)
    {
        ArgumentNullException.ThrowIfNull(name);

        if (concreteByName.TryGetValue(name, out SettingDescriptor? concrete))
        {
            return concrete;
        }

        if (!TrySplit(name, out SettingScope scope, out string[] segments))
        {
            return null;
        }

        Template? match = templates.FirstOrDefault(template => Matches(template, scope, segments));
        return match is null ? null : Instantiate(match, segments[match.WildcardIndex]);
    }

    public SettingDescriptor Instantiate(SettingDescriptor template, string segment)
    {
        ArgumentNullException.ThrowIfNull(template);
        ArgumentNullException.ThrowIfNull(segment);

        if (!templateByDescriptor.TryGetValue(template, out Template? known))
        {
            throw new ArgumentException("The descriptor is not a template of this registry.", nameof(template));
        }

        if (!IsConcreteSegment(segment))
        {
            throw new ArgumentException(
                "A concrete segment is non-empty, is not '*', holds no ':' and no '__', and neither starts nor ends with '_'.",
                nameof(segment));
        }

        return Instantiate(known, segment);
    }

    public string NameOf(SettingScope scope, string path)
    {
        ArgumentNullException.ThrowIfNull(path);

        return PrefixOf(scope) + path.Replace(PathSeparator, NameSeparator, StringComparison.Ordinal);
    }

    private string PrefixOf(SettingScope scope) => scope switch
    {
        SettingScope.Global => naming.GlobalPrefix,
        SettingScope.User => naming.UserPrefix,
        SettingScope.Local => naming.LocalPrefix,
        _ => throw new ArgumentOutOfRangeException(nameof(scope), scope, "The scope is not a defined SettingScope."),
    };

    private SettingDescriptor Instantiate(Template template, string segment)
    {
        string[] segments = [.. template.Segments];
        segments[template.WildcardIndex] = segment;
        string path = string.Join(PathSeparator, segments);

        return template.Descriptor with { Path = path, Name = NameOf(template.Descriptor.Scope, path) };
    }

    private bool TrySplit(string name, out SettingScope scope, out string[] segments)
    {
        scope = default;
        segments = [];

        ReadOnlySpan<SettingScope> scopes = [SettingScope.Global, SettingScope.User, SettingScope.Local];
        foreach (SettingScope candidate in scopes)
        {
            string prefix = PrefixOf(candidate);
            if (!name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            // The prefixes overlap nowhere, so at most one matches.
            string rest = name[prefix.Length..];
            string[] parts = rest.Split(NameSeparator);
            if (rest.Contains(PathSeparator, StringComparison.Ordinal) || parts.Any(part => part.Length == 0))
            {
                return false;
            }

            scope = candidate;
            segments = parts;
            return true;
        }

        return false;
    }

    private static bool Matches(Template template, SettingScope scope, string[] segments)
    {
        if (template.Descriptor.Scope != scope || template.Segments.Length != segments.Length)
        {
            return false;
        }

        for (int index = 0; index < segments.Length; index++)
        {
            bool matches = index == template.WildcardIndex
                ? IsConcreteSegment(segments[index])
                : string.Equals(template.Segments[index], segments[index], StringComparison.OrdinalIgnoreCase);
            if (!matches)
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsConcreteSegment(string segment) =>
        segment.Length > 0
        && segment != Wildcard
        && !segment.Contains(PathSeparator, StringComparison.Ordinal)
        && !segment.Contains(NameSeparator, StringComparison.Ordinal)
        && segment[0] != '_'
        && segment[^1] != '_';

    private static bool IsPathSegment(string segment) => segment.Length > 0 && segment.All(char.IsAsciiLetterOrDigit);

    private static void CheckNaming(SettingNaming naming)
    {
        (string Member, string? Prefix)[] prefixes =
        [
            (nameof(SettingNaming.GlobalPrefix), naming.GlobalPrefix),
            (nameof(SettingNaming.UserPrefix), naming.UserPrefix),
            (nameof(SettingNaming.LocalPrefix), naming.LocalPrefix),
        ];

        foreach ((string member, string? prefix) in prefixes)
        {
            if (string.IsNullOrWhiteSpace(prefix))
            {
                throw new InvalidOperationException($"The setting name prefix {nameof(SettingNaming)}.{member} is blank.");
            }
        }

        for (int first = 0; first < prefixes.Length; first++)
        {
            for (int second = first + 1; second < prefixes.Length; second++)
            {
                string one = prefixes[first].Prefix ?? "";
                string other = prefixes[second].Prefix ?? "";
                if (one.StartsWith(other, StringComparison.OrdinalIgnoreCase)
                    || other.StartsWith(one, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException(
                        $"The setting name prefixes {nameof(SettingNaming)}.{prefixes[first].Member} '{one}' and "
                        + $"{nameof(SettingNaming)}.{prefixes[second].Member} '{other}' overlap: no prefix may start with another, ignoring case.");
                }
            }
        }
    }

    private List<Leaf> LeavesOf(SettingRoot root, Dictionary<Type, bool> sections)
    {
        string path = root.Path ?? "";
        Type? recordType = root.RecordType;
        string typeName = recordType is null ? "(no record type)" : NameOfType(recordType);

        string[] rootSegments = path.Split(PathSeparator);
        if (rootSegments.Count(segment => segment == Wildcard) > 1
            || !rootSegments.All(segment => segment == Wildcard || IsPathSegment(segment)))
        {
            throw new InvalidOperationException(
                $"The setting root '{path}' of {typeName} is invalid: each ':'-separated segment is ASCII letters and digits, "
                + "or the wildcard '*', which stands at most once.");
        }

        if (recordType is null || recordType.IsInterface || recordType.ContainsGenericParameters)
        {
            throw new InvalidOperationException(
                $"The setting root '{path}' has no usable record type ({typeName}): it needs a class or struct type that is not an open generic.");
        }

        List<Leaf> leaves = [];
        Walk(recordType, DefaultInstance(recordType, typeName), rootSegments, [], [recordType], sections, leaves);
        if (leaves.Count == 0)
        {
            throw new InvalidOperationException(
                $"The setting root '{path}' ({typeName}) declares no settings: mark at least one property with [Setting].");
        }

        string[] readOnly = [.. root.ReadOnly ?? []];
        foreach (string? entry in readOnly)
        {
            if (entry is null || !leaves.Any(leaf => string.Equals(leaf.RelativePath, entry, StringComparison.OrdinalIgnoreCase)))
            {
                throw new InvalidOperationException(
                    $"The read-only entry '{entry}' of the setting root '{path}' ({typeName}) names no setting of that root; "
                    + "entries are setting paths relative to the root, such as 'Retry:Attempts'.");
            }
        }

        return
        [
            .. leaves.Select(leaf => leaf with
            {
                Descriptor = leaf.Descriptor with
                {
                    IsWritable = !readOnly.Contains(leaf.RelativePath, StringComparer.OrdinalIgnoreCase),
                },
            }),
        ];
    }

    private void Walk(
        Type type,
        object? instance,
        string[] rootSegments,
        string[] relative,
        HashSet<Type> visiting,
        Dictionary<Type, bool> sections,
        List<Leaf> leaves)
    {
        foreach (Slot slot in SlotsOf(type))
        {
            PropertyInfo property = slot.Declaration;
            string member = $"{NameOfType(slot.Override.DeclaringType ?? type)}.{property.Name}";
            var setting = (SettingAttribute?)Attribute.GetCustomAttribute(slot.Override, typeof(SettingAttribute), inherit: true);
            bool isProtected = Attribute.IsDefined(slot.Override, typeof(ProtectedAttribute), inherit: true);
            bool isSink = Attribute.IsDefined(slot.Override, typeof(SinkAttribute), inherit: true);
            bool isMarked = IsMarked(slot.Override);

            if (!IsReadableInstanceProperty(property))
            {
                if (isMarked)
                {
                    throw new InvalidOperationException(
                        $"{member} carries a setting attribute but is not a public, readable, non-indexed instance property.");
                }

                continue;
            }

            if (setting is null && isMarked)
            {
                throw new InvalidOperationException(
                    $"{member} carries [Protected] or [Sink] without [Setting]; both only qualify a setting.");
            }

            Type sectionType = Unwrapped(property.PropertyType);
            if (setting is null && !IsSection(sectionType, sections))
            {
                continue;
            }

            if (!IsPathSegment(property.Name))
            {
                throw new InvalidOperationException(
                    $"{member} cannot be a setting path segment: a segment is ASCII letters and digits only.");
            }

            string[] path = [.. relative, property.Name];
            if (setting is null)
            {
                if (!visiting.Add(sectionType))
                {
                    throw new InvalidOperationException(
                        $"{member} makes the record graph contain itself ({NameOfType(sectionType)}); a section cannot nest its own type.");
                }

                Walk(sectionType, ValueOf(property, instance, member), rootSegments, path, visiting, sections, leaves);
                visiting.Remove(sectionType);
                continue;
            }

            leaves.Add(LeafOf(slot, setting, isProtected, isSink, instance, rootSegments, path, member));
        }
    }

    private Leaf LeafOf(
        Slot slot,
        SettingAttribute setting,
        bool isProtected,
        bool isSink,
        object? instance,
        string[] rootSegments,
        string[] relative,
        string member)
    {
        if (!Enum.IsDefined(setting.Scope))
        {
            throw new InvalidOperationException($"{member} has the undefined setting scope {(int)setting.Scope}.");
        }

        if (string.IsNullOrWhiteSpace(setting.Description))
        {
            throw new InvalidOperationException($"{member} has a blank setting description.");
        }

        Type declared = setting.ValueType ?? slot.Declaration.PropertyType;
        Type valueType = Unwrapped(declared);
        if (!IsSupported(valueType))
        {
            throw new InvalidOperationException(
                $"{member} has the unsupported setting type {NameOfType(valueType)}; supported are string, bool, int, long, double, "
                + $"TimeSpan, Uri, an enum and IReadOnlyList<string>. Set {nameof(SettingAttribute)}.{nameof(SettingAttribute.ValueType)} "
                + "to the configured value's type when it differs from the property's.");
        }

        // A protected value is write-only, its compiled-in default included.
        bool isRequired = slot.Declaration.IsDefined(typeof(RequiredMemberAttribute), inherit: false)
            || slot.Override.IsDefined(typeof(RequiredMemberAttribute), inherit: false);
        string? defaultValue = isRequired || isProtected ? null : Format(valueType, ValueOf(slot.Declaration, instance, member));

        string[] segments = [.. rootSegments, .. relative];
        string path = string.Join(PathSeparator, segments);
        var descriptor = new SettingDescriptor(
            NameOf(setting.Scope, path),
            path,
            setting.Scope,
            valueType,
            setting.Description,
            isProtected,
            isSink,
            IsWritable: true,
            defaultValue);

        return new Leaf(descriptor, segments, string.Join(PathSeparator, relative), member);
    }

    // Two settings collide when one path can name the same key as the other (equal ignoring case,
    // or equal but for a wildcard), or the key of a section the other's path runs through.
    private static void CheckCollisions(List<Leaf> leaves)
    {
        for (int first = 0; first < leaves.Count; first++)
        {
            for (int second = first + 1; second < leaves.Count; second++)
            {
                Leaf one = leaves[first];
                Leaf other = leaves[second];
                if (!Collide(one.Segments, other.Segments))
                {
                    continue;
                }

                string problem = one.Segments.Length == other.Segments.Length
                    ? "can name the same setting path"
                    : "make one key both a setting and a section";
                throw new InvalidOperationException(
                    $"{one.Member} ('{one.Descriptor.Path}') and {other.Member} ('{other.Descriptor.Path}') {problem}.");
            }
        }
    }

    // Zip stops at the shorter path, so a path also collides with every longer path it prefixes.
    private static bool Collide(string[] one, string[] other) =>
        one.Zip(other).All(pair => pair.First == Wildcard
            || pair.Second == Wildcard
            || string.Equals(pair.First, pair.Second, StringComparison.OrdinalIgnoreCase));

    private static bool IsSupported(Type type) =>
        type == typeof(string)
        || type == typeof(bool)
        || type == typeof(int)
        || type == typeof(long)
        || type == typeof(double)
        || type == typeof(TimeSpan)
        || type == typeof(Uri)
        || type.IsEnum
        || type == typeof(IReadOnlyList<string>);

    // A section is a class or struct that carries a setting attribute on one of its properties, or
    // holds through its readable properties, at any depth, a type that does. A breadth-first search
    // over property types answers it without recursion; the answer is kept per registry build.
    private static bool IsSection(Type type, Dictionary<Type, bool> sections)
    {
        if (sections.TryGetValue(type, out bool known))
        {
            return known;
        }

        HashSet<Type> seen = [type];
        Queue<Type> pending = new([type]);
        bool found = false;
        while (!found && pending.TryDequeue(out Type? current))
        {
            if (!CanBeSection(current))
            {
                continue;
            }

            foreach (Slot slot in SlotsOf(current))
            {
                if (IsMarked(slot.Override))
                {
                    found = true;
                    break;
                }

                Type next = Unwrapped(slot.Declaration.PropertyType);
                if (IsReadableInstanceProperty(slot.Declaration) && seen.Add(next))
                {
                    pending.Enqueue(next);
                }
            }
        }

        sections[type] = found;
        return found;
    }

    private static bool CanBeSection(Type type) =>
        !type.IsInterface
        && !type.IsPrimitive
        && !type.IsArray
        && !type.IsPointer
        && !type.IsByRef
        && !type.ContainsGenericParameters
        && !IsSupported(type);

    private static bool IsMarked(PropertyInfo property) =>
        Attribute.IsDefined(property, typeof(SettingAttribute), inherit: true)
        || Attribute.IsDefined(property, typeof(ProtectedAttribute), inherit: true)
        || Attribute.IsDefined(property, typeof(SinkAttribute), inherit: true);

    private static Type Unwrapped(Type type) => Nullable.GetUnderlyingType(type) ?? type;

    private static bool IsReadableInstanceProperty(PropertyInfo property) =>
        property.GetMethod is { IsPublic: true, IsStatic: false } && property.GetIndexParameters().Length == 0;

    // Base type first, declaration order within a type (metadata order), and an override in the
    // place of the property it overrides: GetProperties promises no order, and lists an
    // overridden property once per declaring type. Accessors are keyed by their base definition's
    // metadata identity, not by MethodInfo instances, whose equality depends on the reflected type.
    private static List<Slot> SlotsOf(Type type)
    {
        Stack<Type> chain = new();
        for (Type? current = type; current is not null && current != typeof(object) && current != typeof(ValueType); current = current.BaseType)
        {
            chain.Push(current);
        }

        List<Slot> slots = [];
        Dictionary<(ReflectionModule Module, int Token), int> slotByBaseAccessor = [];
        foreach (Type declaring in chain)
        {
            foreach (PropertyInfo property in declaring.GetProperties(DeclaredProperties).OrderBy(property => property.MetadataToken))
            {
                (ReflectionModule, int)[] baseAccessors =
                [
                    .. new[] { property.GetMethod, property.SetMethod }
                        .OfType<MethodInfo>()
                        .Select(accessor => accessor.GetBaseDefinition())
                        .Select(accessor => (accessor.Module, accessor.MetadataToken)),
                ];
                int? overridden = baseAccessors
                    .Select(accessor => slotByBaseAccessor.TryGetValue(accessor, out int slot) ? slot : (int?)null)
                    .FirstOrDefault(slot => slot is not null);

                if (overridden is { } index)
                {
                    slots[index] = slots[index] with { Override = property };
                    continue;
                }

                foreach ((ReflectionModule, int) accessor in baseAccessors)
                {
                    slotByBaseAccessor[accessor] = slots.Count;
                }

                slots.Add(new Slot(property, property));
            }
        }

        return slots;
    }

    private static object? DefaultInstance(Type type, string typeName)
    {
        try
        {
            return type switch
            {
                { IsValueType: true } => Activator.CreateInstance(type),
                { IsAbstract: true } => null,
                _ => type.GetConstructor(Type.EmptyTypes)?.Invoke(null),
            };
        }
        catch (TargetInvocationException exception)
        {
            throw new InvalidOperationException(
                $"The parameterless constructor of {typeName} threw while the setting defaults were read.",
                exception.InnerException ?? exception);
        }
    }

    private static object? ValueOf(PropertyInfo property, object? instance, string member)
    {
        if (instance is null)
        {
            return null;
        }

        try
        {
            return property.GetValue(instance);
        }
        catch (TargetInvocationException exception)
        {
            throw new InvalidOperationException(
                $"The getter of {member} threw while the setting defaults were read.",
                exception.InnerException ?? exception);
        }
    }

    private static string? Format(Type type, object? value) => value switch
    {
        null => null,
        _ when !type.IsInstanceOfType(value) => null,
        bool flag => flag ? "true" : "false",
        TimeSpan span => span.ToString("c", CultureInfo.InvariantCulture),
        Uri uri => uri.OriginalString,
        Enum member => member.ToString(),
        string text => text,
        IReadOnlyList<string?> items => ToJson(items),
        IFormattable number => number.ToString(null, CultureInfo.InvariantCulture),
        _ => null,
    };

    private static string ToJson(IReadOnlyList<string?> items)
    {
        ArrayBufferWriter<byte> buffer = new();
        using (Utf8JsonWriter writer = new(buffer))
        {
            writer.WriteStartArray();
            foreach (string? item in items)
            {
                if (item is null)
                {
                    writer.WriteNullValue();
                }
                else
                {
                    writer.WriteStringValue(item);
                }
            }

            writer.WriteEndArray();
        }

        return Encoding.UTF8.GetString(buffer.WrittenSpan);
    }

    private static string NameOfType(Type type) => type.FullName ?? type.Name;

    /// <summary>
    /// A property's place in the walk: the declaration that fixes its position and accessors, and
    /// the most derived override whose attributes count.
    /// </summary>
    private sealed record Slot(PropertyInfo Declaration, PropertyInfo Override);

    /// <summary>
    /// One setting found by the walk, with what the collision and read-only checks need.
    /// </summary>
    private sealed record Leaf(SettingDescriptor Descriptor, string[] Segments, string RelativePath, string Member);

    /// <summary>
    /// A template's segments and the position of its wildcard, for matching names.
    /// </summary>
    private sealed record Template(SettingDescriptor Descriptor, string[] Segments, int WildcardIndex);
}
