namespace DiskTree.Core.Tree;

public static class TreeAggregation
{
    /// <summary>
    /// Recomputes Bytes, Files, Dirs and direct Own totals bottom-up,
    /// then orders children by Metric descending.
    /// </summary>
    public static void Aggregate(Node node, Metric metric)
    {
        if (!node.IsDirectory)
        {
            node.Bytes = node.OwnBytes;
            node.Files = node.OwnFiles;
            node.Dirs = 0;
            return;
        }

        ulong bytes = 0;
        ulong files = 0;
        ulong ownBytes = 0;
        ulong ownFiles = 0;
        ulong dirs = 1;
        long modified = node.Modified;

        foreach (var child in node.Children)
        {
            Aggregate(child, metric);
            if (child.Modified > modified)
                modified = child.Modified;

            bytes += child.Bytes;
            files += child.Files;
            dirs += child.Dirs;

            if (!child.IsDirectory)
            {
                ownBytes += child.Bytes;
                ownFiles += child.Files;
            }
        }

        node.Bytes = bytes;
        node.Files = files;
        node.OwnBytes = ownBytes;
        node.OwnFiles = ownFiles;
        node.Dirs = dirs;
        node.Modified = modified;

        node.Children.Sort((left, right) =>
        {
            int cmp = right.Value(metric).CompareTo(left.Value(metric));
            if (cmp != 0) return cmp;
            return string.Compare(left.Name, right.Name, StringComparison.Ordinal);
        });
    }
}
