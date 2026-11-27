using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Text.Json;

namespace Nordstein.Core.Common.Settings;

/// <summary>
/// What a setting's value looks like as text: its kind, and whether a text typed for it is one its
/// type can hold. Pure and thread-safe.
/// </summary>
public static class SettingValueExtensions
{
    /// <summary>
    /// Gets the kind of value <paramref name="descriptor"/> holds, from its <see cref="SettingDescriptor.Type"/>.
    /// </summary>
    /// <param name="descriptor">The setting.</param>
    /// <returns>The kind.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="descriptor"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">
    /// The type is not one the registry supports (a descriptor built or changed outside it); the
    /// message names the path and the type.
    /// </exception>
    public static SettingValueKind ValueKind(this SettingDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        Type type = descriptor.Type;
        return type == typeof(string) ? SettingValueKind.Text
            : type.IsEnum ? SettingValueKind.Choice
            : type == typeof(bool) ? SettingValueKind.Boolean
            : type == typeof(int) || type == typeof(long) ? SettingValueKind.Integer
            : type == typeof(double) ? SettingValueKind.Number
            : type == typeof(TimeSpan) ? SettingValueKind.Duration
            : type == typeof(Uri) ? SettingValueKind.Url
            : type == typeof(IReadOnlyList<string>) ? SettingValueKind.List
            : throw Unsupported(descriptor);
    }

    /// <summary>
    /// Checks that <paramref name="value"/>, typed for <paramref name="descriptor"/>, is text its type
    /// can hold, read culture-invariantly as configuration binds it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Accepted, by <see cref="SettingDescriptor.Type"/>: <see cref="string"/>, any text;
    /// <see cref="bool"/>, what <see cref="bool.TryParse(string, out bool)"/> takes (<c>true</c> or
    /// <c>false</c>, any case, surrounding whitespace allowed); <see cref="int"/> and
    /// <see cref="long"/>, <see cref="System.Globalization.NumberStyles.Integer"/> within the type's
    /// range; <see cref="double"/>, <see cref="System.Globalization.NumberStyles.Float"/>
    /// (<c>NaN</c> and <c>Infinity</c> included); <see cref="TimeSpan"/>, what
    /// <see cref="TimeSpan.TryParse(string, IFormatProvider, out TimeSpan)"/> takes
    /// (<c>00:01:30</c>); <see cref="Uri"/>, an absolute URI that starts with its scheme (so neither a
    /// bare file path nor surrounding whitespace); an enum, a member's name, ignoring case (never a
    /// number or a combination of flags); a list, a JSON array whose elements are strings or
    /// <c>null</c> (the form <see cref="SettingDescriptor.DefaultValue"/> uses). Every
    /// <see cref="SettingDescriptor.DefaultValue"/> the registry produces passes.
    /// </para>
    /// <para>
    /// Ranges, lengths and rules across settings are the product's. The failure names
    /// <see cref="SettingDescriptor.Path"/> and the expected form, never the value, which may be a
    /// secret typed into the wrong field.
    /// </para>
    /// </remarks>
    /// <param name="descriptor">The setting the value is for.</param>
    /// <param name="value">The text typed for it.</param>
    /// <returns>
    /// <see langword="null"/> (<see cref="ValidationResult.Success"/>) when the type can hold the
    /// value; otherwise a result whose message is <c>{Path} must be {form}.</c> and whose
    /// <see cref="ValidationResult.MemberNames"/> is the path.
    /// </returns>
    /// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">
    /// The type is not one the registry supports; the message names the path and the type.
    /// </exception>
    public static ValidationResult? CheckValue(this SettingDescriptor descriptor, string value)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        ArgumentNullException.ThrowIfNull(value);
        Type type = descriptor.Type;
        string? expected = descriptor.ValueKind() switch
        {
            SettingValueKind.Text => null,
            SettingValueKind.Choice => IsMemberName(type, value) ? null : $"one of {string.Join(", ", Enum.GetNames(type))}",
            SettingValueKind.Boolean => bool.TryParse(value, out _) ? null : "true or false",
            SettingValueKind.Integer => IsInteger(type, value) ? null : "an integer",
            SettingValueKind.Number => double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out _) ? null : "a number",
            SettingValueKind.Duration => TimeSpan.TryParse(value, CultureInfo.InvariantCulture, out _) ? null : "a duration such as 00:01:30",
            SettingValueKind.Url => IsAbsoluteUrl(value) ? null : "an absolute URL",
            _ => IsStringArray(value) ? null : "a JSON array of strings",
        };

        return expected is null ? null : new ValidationResult($"{descriptor.Path} must be {expected}.", [descriptor.Path]);
    }

    private static bool IsInteger(Type type, string value)
        => type == typeof(int)
            ? int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out _)
            : long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out _);

    // Enum.TryParse also takes numbers, flag combinations and surrounding whitespace: only a name passes.
    private static bool IsMemberName(Type type, string value)
        => Enum.GetNames(type).Any(name => string.Equals(name, value, StringComparison.OrdinalIgnoreCase));

    // On Unix Uri takes "/var/data" as an absolute file URI: the text must name its scheme itself.
    private static bool IsAbsoluteUrl(string value)
        => Uri.TryCreate(value, UriKind.Absolute, out Uri? uri)
            && value.StartsWith(uri.Scheme + ":", StringComparison.OrdinalIgnoreCase);

    private static bool IsStringArray(string value)
    {
        try
        {
            using var document = JsonDocument.Parse(value);
            return document.RootElement.ValueKind == JsonValueKind.Array
                && document.RootElement.EnumerateArray().All(element => element.ValueKind is JsonValueKind.String or JsonValueKind.Null);
        }
        catch (JsonException)
        {
            // Not JSON at all: refused like any other text that is not an array of strings.
            return false;
        }
    }

    private static InvalidOperationException Unsupported(SettingDescriptor descriptor)
        => new($"The setting {descriptor.Path} has a value type the setting registry does not support: {descriptor.Type.FullName}.");
}
