namespace Nordstein.Core.Common.Settings;

/// <summary>
/// Marks a setting as write-only: its value is a secret (a password, an API key) that may be set
/// but never shown. The registry reports it as <see cref="SettingDescriptor.IsProtected"/>; keeping
/// the value out of every read, list, log and message is the product's duty.
/// </summary>
/// <remarks>
/// Valid only together with <see cref="SettingAttribute"/> on the same property; the registry
/// refuses it alone. Inherited like <see cref="SettingAttribute"/>.
/// </remarks>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = true)]
public sealed class ProtectedAttribute : Attribute
{
}
