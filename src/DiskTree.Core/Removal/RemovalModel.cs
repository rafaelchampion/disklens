namespace DiskTree.Core.Removal;

public sealed record Target(
    string Path,
    ulong Bytes,
    bool IsDirectory,
    bool Hidden
);

public enum RemovalMode : byte
{
    RecycleBin,
    Permanent
}

public sealed record Blocked(
    string Path,
    string Reason
);

public sealed class RemovalPlan
{
    public List<Target> Targets { get; set; } = [];
    public List<Target> Covered { get; set; } = [];
    public List<Blocked> Blocked { get; set; } = [];

    public ulong TotalBytes => Targets.Aggregate(0UL, (acc, t) => acc + t.Bytes);
    public bool IsEmpty => Targets.Count == 0;
}
