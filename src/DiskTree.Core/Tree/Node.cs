using DiskTree.Core.Classify;

namespace DiskTree.Core.Tree;

/// <summary>
/// One entry in the scanned disk tree.
///
/// Subtree totals and direct figures are both kept:
/// OwnBytes and OwnFiles are what sits directly in this directory,
/// while Bytes and Files are the full subtree totals the treemap draws.
/// </summary>
public sealed class Node
{
    public string Name { get; set; } = string.Empty;
    public NodeKind Kind { get; set; }

    /// <summary>
    /// Subtree total: direct contents plus every descendant.
    /// </summary>
    public ulong Bytes { get; set; }

    /// <summary>
    /// Bytes of leaf entries directly in this directory (or this file's own size).
    /// </summary>
    public ulong OwnBytes { get; set; }

    /// <summary>
    /// Files at or beneath this node (1 for a file).
    /// </summary>
    public ulong Files { get; set; }

    /// <summary>
    /// Files directly in this directory (1 for a file).
    /// </summary>
    public ulong OwnFiles { get; set; }

    /// <summary>
    /// Directories at or beneath this node (1 for a directory).
    /// </summary>
    public ulong Dirs { get; set; }

    /// <summary>
    /// (VolumeSerialNumber, FileIndex) for files, used to de-duplicate hardlinks.
    /// </summary>
    public (ulong VolumeSerial, ulong FileId)? FileIdentifier { get; set; }

    /// <summary>
    /// True if the directory could not be read.
    /// </summary>
    public bool ReadError { get; set; }

    /// <summary>
    /// Newest write time at or beneath this node (Unix seconds; 0 when unknown).
    /// </summary>
    public long Modified { get; set; }

    /// <summary>
    /// Kind of data, used for treemap color palette.
    /// </summary>
    public Category Category { get; set; } = Category.Other;

    /// <summary>
    /// Why this space can be had back, if it can (diagonal hatching).
    /// </summary>
    public Reclaim? Reclaim { get; set; }

    /// <summary>
    /// Children, ordered by Metric value descending after aggregation.
    /// </summary>
    public List<Node> Children { get; set; } = [];

    public bool IsDirectory => Kind == NodeKind.Directory;

    public static Node CreateDirectory(string name) => new()
    {
        Name = name,
        Kind = NodeKind.Directory,
        Dirs = 1,
        Category = Category.Other
    };

    public static Node CreateEntry(string name, NodeKind kind, ulong bytes, long modified = 0) => new()
    {
        Name = name,
        Kind = kind,
        Bytes = bytes,
        OwnBytes = bytes,
        Files = (ulong)(kind == NodeKind.File ? 1 : 0),
        OwnFiles = (ulong)(kind == NodeKind.File ? 1 : 0),
        Dirs = 0,
        Modified = modified,
        Category = Category.Other
    };

    public ulong Value(Metric metric) => metric switch
    {
        Metric.Bytes => Bytes,
        Metric.Files => Files,
        _ => Bytes
    };

    public Node? ChildNamed(string name)
    {
        for (int i = 0; i < Children.Count; i++)
        {
            if (string.Equals(Children[i].Name, name, StringComparison.OrdinalIgnoreCase))
                return Children[i];
        }
        return null;
    }

    public Node? Child(int index) => (index >= 0 && index < Children.Count) ? Children[index] : null;

    /// <summary>
    /// Follow crumbs (child indices) from this node.
    /// </summary>
    public Node? Resolve(ReadOnlySpan<int> crumbs)
    {
        Node current = this;
        foreach (int index in crumbs)
        {
            if (index < 0 || index >= current.Children.Count)
                return null;
            current = current.Children[index];
        }
        return current;
    }

    /// <summary>
    /// The chain of nodes ending at crumbs, including this root node.
    /// </summary>
    public List<Node> ResolveChain(ReadOnlySpan<int> crumbs)
    {
        var chain = new List<Node>(crumbs.Length + 1) { this };
        Node current = this;
        foreach (int index in crumbs)
        {
            if (index < 0 || index >= current.Children.Count)
                break;
            current = current.Children[index];
            chain.Add(current);
        }
        return chain;
    }

    public int? LargestChild() => Children.Count > 0 ? 0 : null;

    public int Depth()
    {
        int max = 0;
        foreach (var child in Children)
        {
            int d = child.Depth();
            if (d + 1 > max)
                max = d + 1;
        }
        return max;
    }

    /// <summary>
    /// Breadth-first search for the first node containing needle (case-insensitive).
    /// </summary>
    public (List<int> Crumbs, Node Found)? Find(string needle)
    {
        if (string.IsNullOrEmpty(needle))
            return null;

        var queue = new Queue<(List<int> Crumbs, Node Node)>();
        queue.Enqueue(([], this));

        while (queue.Count > 0)
        {
            var (crumbs, node) = queue.Dequeue();
            for (int i = 0; i < node.Children.Count; i++)
            {
                var child = node.Children[i];
                if (child.Name.Contains(needle, StringComparison.OrdinalIgnoreCase))
                {
                    var foundCrumbs = new List<int>(crumbs) { i };
                    return (foundCrumbs, child);
                }

                if (child.IsDirectory && child.Children.Count > 0)
                {
                    var nextCrumbs = new List<int>(crumbs) { i };
                    queue.Enqueue((nextCrumbs, child));
                }
            }
        }

        return null;
    }
}
