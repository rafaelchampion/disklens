namespace DiskTree.Core.Tree;

/// <summary>
/// What a node represents on disk.
/// </summary>
public enum NodeKind : byte
{
    Directory,
    File,
    Symlink,
    /// <summary>
    /// Sockets, FIFOs, and devices: addressable, but not disk space.
    /// </summary>
    Other
}

public static class NodeKindExtensions
{
    public static bool IsDirectory(this NodeKind kind) => kind == NodeKind.Directory;
}
