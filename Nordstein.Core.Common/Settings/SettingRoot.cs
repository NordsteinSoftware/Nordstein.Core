namespace Nordstein.Core.Common.Settings;

/// <summary>
/// One configuration section the <see cref="ISettingRegistry"/> reads settings from: the section's
/// path and the record it binds to. A product registers one instance per section in its container;
/// the registry takes them in registration order.
/// </summary>
/// <remarks>
/// The record's <see cref="SettingAttribute"/> properties become settings under
/// <paramref name="Path"/>; a property without the attribute whose type declares such properties is
/// a nested section named by the property. Checked when the registry is built, never here.
/// </remarks>
/// <param name="Path">
/// The section's configuration path, segments joined by <c>:</c> (<c>Mailer</c>,
/// <c>Feeds:*</c>). Each segment consists of ASCII letters and digits, or is the wildcard
/// <c>*</c>, at most once: a wildcard root describes every element of a keyed collection, and its
/// settings are templates (<see cref="SettingDescriptor.IsTemplate"/>).
/// </param>
/// <param name="RecordType">The record the section binds to.</param>
/// <param name="ReadOnly">
/// The settings of this root that are listed and readable but never changed in place
/// (<see cref="SettingDescriptor.IsWritable"/> <see langword="false"/>), as paths relative to
/// <paramref name="Path"/> (<c>Retry:Attempts</c>), compared ignoring case. Each entry must name a
/// setting of this root. <see langword="null"/> (the default) means none.
/// </param>
public sealed record SettingRoot(string Path, Type RecordType, IReadOnlyList<string>? ReadOnly = null);
