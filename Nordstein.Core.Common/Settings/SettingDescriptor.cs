namespace Nordstein.Core.Common.Settings;

/// <summary>
/// What an <see cref="ISettingRegistry"/> knows about one setting: its name, path, scope, value
/// type, description, flags and compiled-in default. It holds no configured value.
/// </summary>
/// <remarks>
/// Immutable. Equality compares every member (<paramref name="Type"/> by
/// <see cref="Type.Equals(Type)"/>), so a descriptor changed with a <c>with</c> expression is no
/// longer the registry's own.
/// </remarks>
/// <param name="Name">
/// The canonical name: the scope's prefix, then <paramref name="Path"/> with <c>:</c> written as
/// <c>__</c> (<c>$APP__Mailer__Retry__Attempts</c>).
/// </param>
/// <param name="Path">
/// The configuration path, segments joined by <c>:</c> (<c>Mailer:Retry:Attempts</c>). A
/// template's path holds the wildcard segment <c>*</c>; a descriptor made from a template holds the
/// concrete segment in its place.
/// </param>
/// <param name="Scope">Who the setting's value applies to.</param>
/// <param name="Type">
/// The configured value's type, <see cref="Nullable{T}"/> unwrapped: <see cref="string"/>,
/// <see cref="bool"/>, <see cref="int"/>, <see cref="long"/>, <see cref="double"/>,
/// <see cref="TimeSpan"/>, <see cref="Uri"/>, an enum, or an <see cref="IReadOnlyList{T}"/> of
/// <see cref="string"/>.
/// </param>
/// <param name="Description">The <see cref="SettingAttribute.Description"/>.</param>
/// <param name="IsProtected">Whether the property carries <see cref="ProtectedAttribute"/>: the value is write-only.</param>
/// <param name="IsSink">Whether the property carries <see cref="SinkAttribute"/>: the value names where data goes.</param>
/// <param name="IsWritable">Whether the setting may be changed in place: <see langword="false"/> when its root lists it under <see cref="SettingRoot.ReadOnly"/>.</param>
/// <param name="DefaultValue">
/// The value a freshly constructed record holds, as invariant text (see
/// <see cref="ISettingRegistry"/>); <see langword="null"/> when there is none.
/// </param>
public sealed record SettingDescriptor(
    string Name,
    string Path,
    SettingScope Scope,
    Type Type,
    string Description,
    bool IsProtected,
    bool IsSink,
    bool IsWritable,
    string? DefaultValue)
{
    /// <summary>
    /// Whether <see cref="Path"/> holds the wildcard segment <c>*</c>: the descriptor stands for the
    /// same setting of every element of a keyed collection, and
    /// <see cref="ISettingRegistry.Instantiate"/> makes the descriptor of one element. A segment
    /// that merely contains <c>*</c> (<c>A*</c>) is no wildcard.
    /// </summary>
    public bool IsTemplate =>
        Path == "*"
        || Path.StartsWith("*:", StringComparison.Ordinal)
        || Path.EndsWith(":*", StringComparison.Ordinal)
        || Path.Contains(":*:", StringComparison.Ordinal);
}
