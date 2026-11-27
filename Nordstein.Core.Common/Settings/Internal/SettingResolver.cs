namespace Nordstein.Core.Common.Settings.Internal;

/// <summary>
/// The <see cref="ISettingResolver"/>: the registry for names and templates, the product's
/// <see cref="ISettingCollections"/> for the keys a template's wildcard stands for.
/// </summary>
internal sealed class SettingResolver : ISettingResolver
{
    private const char PathSeparator = ':';
    private const string Wildcard = "*";

    private readonly ISettingRegistry registry;
    private readonly ISettingCollections collections;

    public SettingResolver(ISettingRegistry registry, ISettingCollections collections)
    {
        this.registry = registry;
        this.collections = collections;
    }

    public SettingDescriptor? Resolve(string name)
    {
        ArgumentNullException.ThrowIfNull(name);

        SettingDescriptor? found = registry.Find(name);
        if (found is null || TemplateOf(found) is not { } template)
        {
            return found;
        }

        string segment = found.Path.Split(PathSeparator)[template.WildcardIndex];
        string? key = KeysOf(template.Collection).FirstOrDefault(candidate => string.Equals(candidate, segment, StringComparison.OrdinalIgnoreCase));
        return key is null ? null : registry.Instantiate(template.Descriptor, key);
    }

    public IReadOnlyList<SettingDescriptor> Expand(IEnumerable<SettingScope> scopes)
    {
        ArgumentNullException.ThrowIfNull(scopes);

        HashSet<SettingScope> wanted = [.. scopes];
        if (wanted.Any(scope => !Enum.IsDefined(scope)))
        {
            throw new ArgumentOutOfRangeException(nameof(scopes), "A scope is not a defined SettingScope.");
        }

        SettingDescriptor[] visible = [.. registry.All.Where(descriptor => wanted.Contains(descriptor.Scope))];
        var expanded = new List<SettingDescriptor>(visible.Length);
        int index = 0;
        while (index < visible.Length)
        {
            if (!visible[index].IsTemplate)
            {
                expanded.Add(visible[index]);
                index++;
                continue;
            }

            // The templates of one collection stand together in registry order (one wildcard root's
            // walk); each key gets all of them in turn.
            string collection = Template.Of(visible[index]).Collection;
            int end = index + 1;
            while (end < visible.Length && visible[end].IsTemplate
                   && string.Equals(Template.Of(visible[end]).Collection, collection, StringComparison.OrdinalIgnoreCase))
            {
                end++;
            }

            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string key in KeysOf(collection).Where(key => SettingRegistry.IsConcreteSegment(key) && seen.Add(key)))
            {
                for (int member = index; member < end; member++)
                {
                    expanded.Add(registry.Instantiate(visible[member], key));
                }
            }

            index = end;
        }

        return expanded;
    }

    // The template a descriptor Find made from a template stems from; null for a concrete setting. The
    // registry refuses paths equal but for a wildcard, so at most one template matches.
    private Template? TemplateOf(SettingDescriptor found)
    {
        string[] segments = found.Path.Split(PathSeparator);
        foreach (SettingDescriptor descriptor in registry.All.Where(descriptor => descriptor.IsTemplate && descriptor.Scope == found.Scope))
        {
            var template = Template.Of(descriptor);
            string[] pattern = descriptor.Path.Split(PathSeparator);
            if (pattern.Length == segments.Length
                && pattern.Select((part, at) => at == template.WildcardIndex || string.Equals(part, segments[at], StringComparison.OrdinalIgnoreCase)).All(matches => matches))
            {
                return template;
            }
        }

        return null;
    }

    private IReadOnlyList<string> KeysOf(string collection)
    {
        IReadOnlyList<string>? keys = collections.KeysOf(collection);
        if (keys is null || keys.Any(key => key is null))
        {
            throw new InvalidOperationException(
                $"{nameof(ISettingCollections)}.{nameof(ISettingCollections.KeysOf)} returned null, or a null key, for the collection '{collection}'.");
        }

        return keys;
    }

    /// <summary>
    /// A template with the index of its wildcard segment and the collection path in front of it
    /// (empty for a wildcard first segment: the configuration's root).
    /// </summary>
    private sealed record Template(SettingDescriptor Descriptor, int WildcardIndex, string Collection)
    {
        public static Template Of(SettingDescriptor descriptor)
        {
            string[] segments = descriptor.Path.Split(PathSeparator);
            int wildcard = Array.IndexOf(segments, Wildcard);
            return new Template(descriptor, wildcard, string.Join(PathSeparator, segments[..wildcard]));
        }
    }
}
