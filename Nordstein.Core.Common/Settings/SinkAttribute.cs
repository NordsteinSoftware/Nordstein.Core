namespace Nordstein.Core.Common.Settings;

/// <summary>
/// Marks a setting whose value names where data goes or who may reach it: an outbound address, a
/// credential for it, a listener, a trusted network. The registry reports it as
/// <see cref="SettingDescriptor.IsSink"/>; a product requires a stronger proof of identity (such as
/// re-entering a password) before it changes such a value.
/// </summary>
/// <remarks>
/// Valid only together with <see cref="SettingAttribute"/> on the same property; the registry
/// refuses it alone. Inherited like <see cref="SettingAttribute"/>.
/// </remarks>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = true)]
public sealed class SinkAttribute : Attribute
{
}
