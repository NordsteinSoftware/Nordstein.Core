namespace Nordstein.Core.Common.Settings;

/// <summary>
/// Turns setting names and templates into the concrete settings the configuration actually holds:
/// a template's instance exists only for a key <see cref="ISettingCollections"/> reports.
/// Registered by <c>Nordstein.Core.Common.Module</c> per dependency; it resolves only in a container
/// that also registers an <see cref="ISettingCollections"/> (and the <see cref="SettingNaming"/> the
/// registry needs).
/// </summary>
/// <remarks>
/// <para>
/// <see cref="ISettingRegistry.Find"/> asks only the code; this asks the configuration too. A product
/// that checks a permission per scope finds the setting first, checks, then resolves, so that a
/// caller without the permission learns nothing about which elements are configured.
/// </para>
/// <para>
/// A configured key that breaks the registry's segment rule (see <see cref="ISettingRegistry"/>) has
/// no name and is left out; keys are compared ignoring case, the first spelling wins. Stateless;
/// thread-safe as far as the <see cref="ISettingCollections"/> is.
/// </para>
/// </remarks>
public interface ISettingResolver
{
    /// <summary>
    /// Gets the concrete setting <paramref name="name"/> denotes, ignoring case: a concrete setting as
    /// <see cref="ISettingRegistry.Find"/> returns it, without asking the configuration; a template's
    /// instance only when the configuration holds that element, its segment then spelled as the
    /// configuration spells it.
    /// </summary>
    /// <param name="name">A setting name such as <c>$APP__Feeds__News__Url</c>.</param>
    /// <returns>The setting, or <see langword="null"/> when no setting has the name or its element is not configured.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="name"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException"><see cref="ISettingCollections.KeysOf"/> returned <see langword="null"/> or a <see langword="null"/> key.</exception>
    SettingDescriptor? Resolve(string name);

    /// <summary>
    /// Gets every concrete setting of <paramref name="scopes"/> in <see cref="ISettingRegistry.All"/>
    /// order, each template instantiated once per configured key: the templates of one collection
    /// that stand together in that order are listed per key, all of one element's settings together.
    /// The configuration is asked only about the collections of the requested scopes, once per such
    /// run of templates.
    /// </summary>
    /// <param name="scopes">The scopes to list; empty lists nothing.</param>
    /// <returns>The settings; a new list on every call.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="scopes"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException">A scope is not a defined <see cref="SettingScope"/>.</exception>
    /// <exception cref="InvalidOperationException"><see cref="ISettingCollections.KeysOf"/> returned <see langword="null"/> or a <see langword="null"/> key.</exception>
    IReadOnlyList<SettingDescriptor> Expand(IEnumerable<SettingScope> scopes);
}
