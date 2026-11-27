namespace Nordstein.Core.Common.Settings;

/// <summary>
/// Marks a property of a configuration record as a setting: one leaf an
/// <see cref="ISettingRegistry"/> lists, finds by name and describes.
/// </summary>
/// <remarks>
/// <para>
/// The property must be a public, readable, non-indexed instance property; its name becomes the
/// last segment of the setting's path and must consist of ASCII letters and digits. Its type, or
/// <see cref="ValueType"/> when set, must be a supported value type (see
/// <see cref="ISettingRegistry"/>). Inherited: a property that overrides a marked one stays a
/// setting.
/// </para>
/// <para>
/// The attribute holds no state beyond its arguments; the registry checks them when it is built
/// and refuses a blank description or an undefined scope there, not here.
/// </para>
/// </remarks>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = true)]
public sealed class SettingAttribute : Attribute
{
    /// <summary>
    /// Marks the property as a setting in <paramref name="scope"/>.
    /// </summary>
    /// <param name="scope">Who the setting's value applies to.</param>
    /// <param name="description">
    /// One sentence for a person or an agent choosing a value: what the setting controls, its unit,
    /// default and range. Never blank.
    /// </param>
    public SettingAttribute(SettingScope scope, string description)
    {
        Scope = scope;
        Description = description;
    }

    /// <summary>
    /// Who the setting's value applies to.
    /// </summary>
    public SettingScope Scope { get; }

    /// <summary>
    /// What the setting controls, as given to the constructor.
    /// </summary>
    public string Description { get; }

    /// <summary>
    /// The configured value's type when it differs from the property's: a property that holds a
    /// record resolved by name is configured as a <see cref="string"/>, a list of parsed values as
    /// an <see cref="IReadOnlyList{T}"/> of <see cref="string"/>. <see langword="null"/> (the
    /// default) means the property's own type.
    /// </summary>
    public Type? ValueType { get; init; }
}
