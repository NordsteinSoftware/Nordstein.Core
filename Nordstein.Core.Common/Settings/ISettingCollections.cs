namespace Nordstein.Core.Common.Settings;

/// <summary>
/// The seam through which <see cref="ISettingResolver"/> asks the product's configuration which
/// elements a keyed collection holds: the concrete segments a template's wildcard stands for.
/// A product that resolves templates registers exactly one implementation.
/// </summary>
/// <remarks>
/// Implementations must be safe for concurrent callers when registered as a singleton, and are
/// asked only about the collections of the scopes a caller requested.
/// </remarks>
public interface ISettingCollections
{
    /// <summary>
    /// Gets the keys of the elements configured under <paramref name="collectionPath"/>, spelled as
    /// the configuration spells them: <c>Feeds</c> gives the feed names (<c>News</c>,
    /// <c>Sports</c>), a list gives its indices (<c>0</c>, <c>1</c>).
    /// </summary>
    /// <param name="collectionPath">
    /// The configuration path in front of a template's wildcard (<c>Feeds</c> for
    /// <c>Feeds:*:Url</c>); the empty string for the configuration's root (a template such as
    /// <c>*:Url</c>). Compared ignoring case, as configuration paths are.
    /// </param>
    /// <returns>The keys, in the configuration's order; empty when none is configured. Never <see langword="null"/>.</returns>
    IReadOnlyList<string> KeysOf(string collectionPath);
}
