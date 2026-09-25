using DiskTree.Core.Tree;

namespace DiskTree.Core.Scan;

/// <summary>
/// How a scan measures and filters the tree.
/// </summary>
public sealed record ScanOptions
{
    /// <summary>
    /// Measure apparent length instead of allocated cluster blocks.
    /// </summary>
    public bool ApparentSize { get; init; } = false;

    /// <summary>
    /// Follow directory junctions and symlinks. Off by default to avoid double-counting.
    /// </summary>
    public bool FollowLinks { get; init; } = false;

    /// <summary>
    /// Include hidden and system files/directories. On by default.
    /// </summary>
    public bool IncludeHidden { get; init; } = true;

    /// <summary>
    /// Stay on the root volume: skip junction points or links to other drive letters.
    /// </summary>
    public bool OneFileSystem { get; init; } = true;

    /// <summary>
    /// Stop descending past this depth.
    /// </summary>
    public int? MaxDepth { get; init; }

    /// <summary>
    /// Count hardlinked files once instead of once per link.
    /// </summary>
    public bool DedupHardlinks { get; init; } = true;

    /// <summary>
    /// Whether children are ranked by bytes or by file count.
    /// </summary>
    public Metric Metric { get; init; } = Metric.Bytes;

    /// <summary>
    /// NTFS cluster size in bytes. Set from the volume data
    /// before scanning so every file rounds to the real cluster
    /// boundary without a per-file GetCompressedFileSize call.
    /// </summary>
    public ulong ClusterSize { get; init; } = 4096;
}
