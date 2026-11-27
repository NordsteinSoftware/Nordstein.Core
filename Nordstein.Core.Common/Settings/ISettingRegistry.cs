namespace Nordstein.Core.Common.Settings;

/// <summary>
/// The catalogue of a product's settings, built once from every registered <see cref="SettingRoot"/>
/// and the one <see cref="SettingNaming"/>: it lists each setting, finds one by name and spells
/// names. It holds no values; reading and storing them is the product's.
/// </summary>
/// <remarks>
/// <para>
/// <b>Walk.</b> Each root's record is read by reflection: a public instance property with
/// <see cref="SettingAttribute"/> is a setting; a public instance property without it whose type
/// (a class or struct, <see cref="Nullable{T}"/> unwrapped, never an interface) carries a setting
/// attribute on a property, itself or through the types its own properties hold, is a nested
/// section named by the property; anything else is ignored. So a grouping record without settings
/// of its own is a section, and an unmarked property that returns its own record type is refused
/// as a record graph that contains itself. Inherited properties come first, base type first;
/// within a type, declaration order; an override keeps its base property's place and is judged by
/// the base declaration's type. A property hidden with <c>new</c> is a second property.
/// </para>
/// <para>
/// <b>Value types.</b> <see cref="SettingAttribute.ValueType"/>, else the property's type, with
/// <see cref="Nullable{T}"/> unwrapped: <see cref="string"/>, <see cref="bool"/>, <see cref="int"/>,
/// <see cref="long"/>, <see cref="double"/>, <see cref="TimeSpan"/>, <see cref="Uri"/>, any enum,
/// <see cref="IReadOnlyList{T}"/> of <see cref="string"/>.
/// </para>
/// <para>
/// <b>Defaults.</b> The registry constructs one instance of each root record through its public
/// parameterless constructor (a struct: its default value) and reads each setting from it, a nested
/// section from the parent's property value. No such constructor, a <see langword="null"/> section,
/// a <see langword="null"/> value, a <c>required</c> member, a <see cref="ProtectedAttribute"/>
/// setting, or a value not of <see cref="SettingDescriptor.Type"/> gives <see langword="null"/>.
/// The default is the compiled-in text as it stands, not redacted (a <see cref="Uri"/> default
/// keeps its user information). Text is culture-invariant:
/// <c>true</c>/<c>false</c>; numbers as <see cref="IFormattable.ToString(string, IFormatProvider)"/>
/// with <see cref="System.Globalization.CultureInfo.InvariantCulture"/>; <see cref="TimeSpan"/> in
/// the constant format <c>c</c> (<c>00:01:30</c>); <see cref="Uri.OriginalString"/>; the enum
/// member's name; a list as compact JSON (<c>["*"]</c>) escaped by <c>System.Text.Json</c>'s
/// default encoder (non-ASCII and HTML-sensitive characters as <c>\uXXXX</c>), a
/// <see langword="null"/> element as <c>null</c>.
/// </para>
/// <para>
/// <b>Names.</b> A name is a scope's prefix followed by the path with <c>:</c> written as
/// <c>__</c>. Names, prefixes and paths compare ignoring case (ordinal). A concrete segment, the
/// one that stands in a template's wildcard, is non-empty, is not <c>*</c>, holds no <c>:</c> and
/// no <c>__</c>, and neither starts nor ends with <c>_</c>, so a name splits into its segments one
/// way only.
/// </para>
/// <para>
/// <b>Construction</b> throws <see cref="InvalidOperationException"/>, naming the type and
/// property, the root or the prefix at fault, for: a blank prefix or two prefixes of which one
/// starts with the other; a root path that is not ASCII-alphanumeric segments with at most one
/// <c>*</c>; a root without a record type, or whose type is an interface or an open generic; a root
/// without settings; two settings whose paths can name the same key (equal ignoring case, or equal
/// but for a wildcard, whatever their scopes) or make one key both a setting and a section; a
/// property name that is not ASCII letters and digits; an unsupported value
/// type; an undefined scope; a blank description; <see cref="ProtectedAttribute"/> or
/// <see cref="SinkAttribute"/> without <see cref="SettingAttribute"/>; any of the three on a static,
/// non-public, write-only or indexed property; a <see cref="SettingRoot.ReadOnly"/> entry that names
/// no setting of its root; a record graph that contains itself; a constructor or property getter
/// that throws while defaults are read.
/// </para>
/// <para>
/// <b>Thread-safety.</b> Immutable after construction; every member is safe for any number of
/// concurrent callers.
/// </para>
/// </remarks>
public interface ISettingRegistry
{
    /// <summary>
    /// Every setting: roots in registration order, then each root's settings in walk order (see
    /// the type's remarks). A wildcard root contributes its templates. Read-only; the same list on
    /// every call.
    /// </summary>
    IReadOnlyList<SettingDescriptor> All { get; }

    /// <summary>
    /// Finds the setting a name denotes, ignoring case.
    /// </summary>
    /// <remarks>
    /// A concrete setting is returned as listed in <see cref="All"/>, with its canonical
    /// <see cref="SettingDescriptor.Name"/> whatever the casing asked for. A name that matches a
    /// template returns <see cref="Instantiate"/> of that template with the concrete segment as
    /// given (its casing kept). A name of the wrong scope's prefix, with a <c>:</c>, an empty
    /// segment, a concrete segment that breaks the segment rule, or naming a section, an unmarked
    /// property or nothing at all, returns <see langword="null"/>.
    /// </remarks>
    /// <param name="name">A setting name such as <c>$APP__Mailer__Port</c>.</param>
    /// <returns>The setting, or <see langword="null"/> when the name denotes none.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="name"/> is <see langword="null"/>.</exception>
    SettingDescriptor? Find(string name);

    /// <summary>
    /// Makes the descriptor of one element of a template: <paramref name="segment"/> replaces the
    /// wildcard in <see cref="SettingDescriptor.Path"/> and <see cref="SettingDescriptor.Name"/>;
    /// every other member is the template's.
    /// </summary>
    /// <param name="template">A template of this registry, as listed in <see cref="All"/>.</param>
    /// <param name="segment">The concrete segment, kept as given; must satisfy the segment rule (see the type's remarks).</param>
    /// <returns>A new descriptor whose <see cref="SettingDescriptor.IsTemplate"/> is <see langword="false"/>.</returns>
    /// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="template"/> is not one of this registry's templates (a concrete setting, a
    /// descriptor made from a template, or one changed or built elsewhere), or
    /// <paramref name="segment"/> breaks the segment rule.
    /// </exception>
    SettingDescriptor Instantiate(SettingDescriptor template, string segment);

    /// <summary>
    /// Spells the name of <paramref name="path"/> in <paramref name="scope"/>: the scope's prefix,
    /// then the path with every <c>:</c> written as <c>__</c>. Pure text; it does not check that a
    /// setting exists under the path (<see cref="Find"/> does).
    /// </summary>
    /// <param name="scope">The scope whose prefix leads the name.</param>
    /// <param name="path">A configuration path such as <c>Mailer:Retry:Attempts</c>.</param>
    /// <returns>The name, such as <c>$APP__Mailer__Retry__Attempts</c>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="path"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="scope"/> is not a defined <see cref="SettingScope"/>.</exception>
    string NameOf(SettingScope scope, string path);
}
