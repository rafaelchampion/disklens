namespace DiskTree.Core.Classify;

/// <summary>
/// Why a directory's space can be had back (drawn with diagonal hatching).
/// </summary>
public enum Reclaim : byte
{
    /// <summary>
    /// A cache: whatever wrote it will regenerate it on demand.
    /// </summary>
    Regenerable,

    /// <summary>
    /// A sync client's old versions of files.
    /// </summary>
    SyncHistory,

    /// <summary>
    /// A package manager's content store (e.g. NuGet fallback/cache, npm cache, cargo cache).
    /// </summary>
    PackageStore,

    /// <summary>
    /// Compiler, bundler or IDE output beside its sources (bin, obj, target, dist, build, .vs).
    /// </summary>
    BuildOutput,

    /// <summary>
    /// Installed dependencies beside their manifest (node_modules, packages).
    /// </summary>
    Reinstallable,

    /// <summary>
    /// Container or sandbox image layers.
    /// </summary>
    SandboxLayers,

    /// <summary>
    /// Sandbox or VM snapshots.
    /// </summary>
    Snapshots,

    /// <summary>
    /// Already deleted, still on disk (Recycle Bin / trash).
    /// </summary>
    Trash,

    /// <summary>
    /// Scratch space meant to be thrown away (temp / tmp).
    /// </summary>
    Temporary
}

public static class ReclaimExtensions
{
    public static string Label(this Reclaim reclaim) => reclaim switch
    {
        Reclaim.Regenerable => "regenerable",
        Reclaim.SyncHistory => "sync history",
        Reclaim.PackageStore => "package store",
        Reclaim.BuildOutput => "build output",
        Reclaim.Reinstallable => "reinstallable",
        Reclaim.SandboxLayers => "sandbox layers",
        Reclaim.Snapshots => "snapshots",
        Reclaim.Trash => "trash",
        Reclaim.Temporary => "temporary",
        _ => "reclaimable"
    };
}
