namespace Nordstein.Core.Common.Settings;

/// <summary>
/// How setting names are spelled: one prefix per <see cref="SettingScope"/>, followed by the
/// setting's path with <c>:</c> written as <c>__</c> (the environment-variable form, such as
/// <c>$APP__Mailer__Retry__Attempts</c>). A product registers exactly one instance in its container.
/// </summary>
/// <remarks>
/// Checked when the registry is built: no prefix is blank, and none starts with another, ignoring
/// case, so a name selects exactly one scope.
/// </remarks>
/// <param name="GlobalPrefix">The prefix of every <see cref="SettingScope.Global"/> setting's name.</param>
/// <param name="UserPrefix">The prefix of every <see cref="SettingScope.User"/> setting's name.</param>
/// <param name="LocalPrefix">The prefix of every <see cref="SettingScope.Local"/> setting's name.</param>
public sealed record SettingNaming(string GlobalPrefix, string UserPrefix, string LocalPrefix);
