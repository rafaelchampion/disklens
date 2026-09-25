using DiskTree.Core.Tree;

namespace DiskTree.Core.Filter;

public abstract record Keep
{
    public sealed record WholeNode : Keep;
    public sealed record PartialNode(ulong Bytes, ulong Files) : Keep;
}

public sealed class FilterMatches
{
    public required string Needle { get; init; }
    public required int[] Base { get; init; }
    public Dictionary<string, Keep> KeepMap { get; } = new(StringComparer.Ordinal);
    public int Count { get; set; }
    public ulong Bytes { get; set; }
    public ulong Files { get; set; }

    public static string CrumbsKey(ReadOnlySpan<int> crumbs)
    {
        if (crumbs.IsEmpty) return string.Empty;
        return string.Join('/', crumbs.ToArray());
    }

    public Keep? GetKeep(ReadOnlySpan<int> crumbs)
    {
        if (!crumbs.StartsWith(Base))
            return new Keep.WholeNode();

        for (int length = Base.Length; length <= crumbs.Length; length++)
        {
            string key = CrumbsKey(crumbs[..length]);
            if (KeepMap.TryGetValue(key, out var keep))
            {
                if (keep is Keep.WholeNode)
                    return keep;
                if (length == crumbs.Length)
                    return keep;
            }
            else if (length > Base.Length)
            {
                return null;
            }
        }

        return new Keep.PartialNode(Bytes, Files);
    }

    public static ulong Value(Keep keep, Node node, Metric metric) => keep switch
    {
        Keep.WholeNode => node.Value(metric),
        Keep.PartialNode p when metric == Metric.Bytes => p.Bytes,
        Keep.PartialNode p when metric == Metric.Files => p.Files,
        _ => node.Value(metric)
    };

    public static FilterMatches? Filter(Node node, int[] @base, string needle)
    {
        string trimmed = needle.Trim();
        if (string.IsNullOrEmpty(trimmed))
            return null;

        var matches = new FilterMatches
        {
            Needle = trimmed,
            Base = @base
        };

        var crumbs = new List<int>(@base);
        var (bytes, files) = Visit(node, crumbs, matches);
        matches.Bytes = bytes;
        matches.Files = files;
        return matches;
    }

    private static (ulong Bytes, ulong Files) Visit(Node node, List<int> crumbs, FilterMatches matches)
    {
        ulong totalBytes = 0;
        ulong totalFiles = 0;

        for (int i = 0; i < node.Children.Count; i++)
        {
            var child = node.Children[i];
            crumbs.Add(i);

            if (child.Name.Contains(matches.Needle, StringComparison.OrdinalIgnoreCase))
            {
                matches.KeepMap[CrumbsKey(crumbs.ToArray())] = new Keep.WholeNode();
                matches.Count++;
                totalBytes += child.Bytes;
                totalFiles += child.Files;
            }
            else if (child.Children.Count > 0)
            {
                var (b, f) = Visit(child, crumbs, matches);
                if (b > 0 || f > 0)
                {
                    matches.KeepMap[CrumbsKey(crumbs.ToArray())] = new Keep.PartialNode(b, f);
                    totalBytes += b;
                    totalFiles += f;
                }
            }

            crumbs.RemoveAt(crumbs.Count - 1);
        }

        return (totalBytes, totalFiles);
    }
}
