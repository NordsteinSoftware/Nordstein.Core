namespace Nordstein.Core.Common.Settings;

/// <summary>
/// The kind of value a setting holds, as a form or a wire format presents it; see
/// <see cref="SettingValueExtensions.ValueKind"/>. The numeric values are stable; never renumber.
/// </summary>
public enum SettingValueKind
{
    /// <summary>
    /// Free text: <see cref="string"/>.
    /// </summary>
    Text = 1,

    /// <summary>
    /// One member of an enum, written by name.
    /// </summary>
    Choice = 2,

    /// <summary>
    /// <c>true</c> or <c>false</c>: <see cref="bool"/>.
    /// </summary>
    Boolean = 3,

    /// <summary>
    /// A whole number: <see cref="int"/> or <see cref="long"/>.
    /// </summary>
    Integer = 4,

    /// <summary>
    /// A floating-point number: <see cref="double"/>.
    /// </summary>
    Number = 5,

    /// <summary>
    /// A length of time: <see cref="TimeSpan"/>.
    /// </summary>
    Duration = 6,

    /// <summary>
    /// An absolute URI: <see cref="Uri"/>.
    /// </summary>
    Url = 7,

    /// <summary>
    /// A list of strings: <see cref="IReadOnlyList{T}"/> of <see cref="string"/>.
    /// </summary>
    List = 8,
}
