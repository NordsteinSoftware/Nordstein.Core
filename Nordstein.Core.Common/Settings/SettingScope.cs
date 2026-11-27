namespace Nordstein.Core.Common.Settings;

/// <summary>
/// Who a setting's value applies to. The scope also selects the prefix of the setting's name
/// (<see cref="SettingNaming"/>); where each scope's values are stored is the product's choice.
/// </summary>
public enum SettingScope
{
    /// <summary>
    /// One value for the whole installation, part of the host's configuration.
    /// </summary>
    Global = 1,

    /// <summary>
    /// One value per user.
    /// </summary>
    User = 2,

    /// <summary>
    /// One value per client installation, such as one browser.
    /// </summary>
    Local = 3,
}
