using System.Text;

namespace Nordstein.Core.Common.Io.Internal;

/// <summary>
/// Real-path, segment-wise, case- and normalization-insensitive containment. See <see cref="IPathContainment"/>.
/// </summary>
internal sealed class PathContainment : IPathContainment
{
    // Linux's own limit (MAXSYMLINKS); a resolution that needs more is a loop or an attack.
    private const int MaxLinkHops = 40;

    private static readonly char[] Separators = [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar];

    public bool MayBeWithin(string path, string root)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        ArgumentException.ThrowIfNullOrEmpty(root);

        // Two readings of each path: the one the operating system walks ("link/.." from the link's target, what a
        // shell or a backup tool sees) and the one .NET opens (Path.GetFullPath collapses ".." as text first). The
        // guard errs to "within" if any pairing is within, so neither the process nor another tool finds a gap.
        ResolvedPath[] paths = [Resolve(path), Resolve(Path.GetFullPath(path))];
        ResolvedPath[] roots = [Resolve(root), Resolve(Path.GetFullPath(root))];

        return paths.Any(candidate => roots.Any(candidate.IsWithin));
    }

    /// <summary>
    /// Resolves a path the way the operating system walks it: links replaced by their targets as they are met,
    /// <c>..</c> applied to what is resolved so far, a missing tail kept as written.
    /// </summary>
    private static ResolvedPath Resolve(string path)
    {
        string absolute = Path.IsPathFullyQualified(path) ? path : Path.Combine(Directory.GetCurrentDirectory(), path);
        string root = RootOf(absolute);
        var pending = new LinkedList<string>(SplitAfterRoot(absolute));
        var resolved = new List<string>();
        int hops = 0;

        // The index in resolved of the first segment that does not exist; -1 while every segment does.
        int firstMissing = -1;

        while (pending.First is { } next)
        {
            pending.RemoveFirst();
            string segment = next.Value;
            if (segment is "" or ".")
            {
                continue;
            }

            if (segment == "..")
            {
                if (resolved.Count > 0)
                {
                    resolved.RemoveAt(resolved.Count - 1);
                }

                // Stepping back above the missing segment returns to segments that exist, links included.
                if (firstMissing >= resolved.Count)
                {
                    firstMissing = -1;
                }

                continue;
            }

            if (firstMissing >= 0)
            {
                // Below a segment that does not exist nothing can be a link: the tail stays as written.
                resolved.Add(segment);
                continue;
            }

            string candidate = Path.Combine([root, .. resolved, segment]);

            // readlink first: it reads a dangling link too, whose target the next create would land in.
            if (new FileInfo(candidate).LinkTarget is { } target)
            {
                if (++hops > MaxLinkHops)
                {
                    throw new IOException($"More than {MaxLinkHops} links were followed resolving '{path}'.");
                }

                if (Path.IsPathRooted(target))
                {
                    // An absolute target restarts from its own root; on Windows a drive-relative "\x" keeps the
                    // current drive. A relative target continues from the link's directory.
                    root = Path.IsPathFullyQualified(target) ? RootOf(target) : root;
                    resolved.Clear();
                }

                string[] targetSegments = SplitAfterRoot(target);
                for (int index = targetSegments.Length - 1; index >= 0; index--)
                {
                    pending.AddFirst(targetSegments[index]);
                }

                continue;
            }

            // LinkTarget answers null for "not a link" and for every error alike (EACCES included); a stat tells
            // them apart, so an unsearchable directory throws instead of hiding a link behind it.
            if (!Exists(candidate))
            {
                firstMissing = resolved.Count;
            }

            resolved.Add(segment);
        }

        return new ResolvedPath(root, resolved);
    }

    private static bool Exists(string candidate)
    {
        try
        {
            _ = File.GetAttributes(candidate);
            return true;
        }
        catch (Exception exception) when (exception is FileNotFoundException or DirectoryNotFoundException)
        {
            return false;
        }
    }

    private static string RootOf(string fullyQualified) => Path.GetPathRoot(fullyQualified.AsSpan()).ToString();

    private static string[] SplitAfterRoot(string path)
        => path[Path.GetPathRoot(path.AsSpan()).Length..].Split(Separators);

    private sealed record ResolvedPath(string Root, IReadOnlyList<string> Segments)
    {
        public bool IsWithin(ResolvedPath root)
            => SameName(Root, root.Root)
                && Segments.Count >= root.Segments.Count
                && root.Segments.Select((segment, index) => SameName(segment, Segments[index])).All(same => same);

        private static bool SameName(string left, string right)
            => string.Equals(Composed(left), Composed(right), StringComparison.OrdinalIgnoreCase);

        private static string Composed(string name)
        {
            try
            {
                return name.Normalize(NormalizationForm.FormC);
            }
            catch (ArgumentException)
            {
                // WHY: a Windows name may hold a lone surrogate, which Normalize refuses; compare it as written.
                return name;
            }
        }
    }
}
