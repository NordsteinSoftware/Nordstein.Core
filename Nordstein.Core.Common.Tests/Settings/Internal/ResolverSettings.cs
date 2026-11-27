using Nordstein.Core.Common.Settings;

namespace Nordstein.Core.Common.Tests.Settings.Internal;

// Neutral sample configuration records for SettingResolverTests: a keyed collection with settings of
// two scopes, a plain section, a collection at the configuration's root, and a second record under
// the same collection registered apart from the first.

internal sealed record StationOptions
{
    [Setting(SettingScope.Global, "The station's address.")]
    public Uri? Url { get; init; }

    [Setting(SettingScope.Global, "How often the station is read.")]
    public TimeSpan Interval { get; init; } = TimeSpan.FromMinutes(5);

    [Setting(SettingScope.User, "Whether this user follows the station.")]
    public bool Followed { get; init; }
}

internal sealed record ThemeOptions
{
    [Setting(SettingScope.Global, "The installation's accent colour.")]
    public string Accent { get; init; } = "blue";

    [Setting(SettingScope.User, "This user's font size in points.")]
    public int FontSize { get; init; } = 12;

    [Setting(SettingScope.Local, "This browser's density.")]
    public string Density { get; init; } = "normal";
}

internal sealed record ZoneOptions
{
    [Setting(SettingScope.Global, "The zone's label.")]
    public string Label { get; init; } = "zone";
}

internal sealed record StationExtraOptions
{
    [Setting(SettingScope.Global, "Who looks after the station.")]
    public string Owner { get; init; } = "nobody";
}
