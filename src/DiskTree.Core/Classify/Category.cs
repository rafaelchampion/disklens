namespace DiskTree.Core.Classify;

/// <summary>
/// A kind of data, used for coloring the treemap mosaic.
/// </summary>
public enum Category : byte
{
    /// <summary>
    /// Source code and checkouts.
    /// </summary>
    Code,

    /// <summary>
    /// Space AI agents and developer tooling write into: worktrees, sandboxes, experiments.
    /// </summary>
    AgentScratch,

    /// <summary>
    /// Compilers, SDKs, package managers and their installs.
    /// </summary>
    Toolchain,

    /// <summary>
    /// Folders a sync client owns (OneDrive, Dropbox, Google Drive, Nextcloud).
    /// </summary>
    Synced,

    /// <summary>
    /// Version-control object stores (.git).
    /// </summary>
    Git,

    /// <summary>
    /// Pictures, music, video, games and models.
    /// </summary>
    Media,

    /// <summary>
    /// Documents, downloads, desktop.
    /// </summary>
    Documents,

    /// <summary>
    /// Caches and other regenerable state.
    /// </summary>
    Cache,

    /// <summary>
    /// Nothing recognizable.
    /// </summary>
    Other
}

public static class CategoryExtensions
{
    public static readonly Category[] Legend =
    [
        Category.Code,
        Category.AgentScratch,
        Category.Toolchain,
        Category.Synced,
        Category.Git,
        Category.Media,
        Category.Documents,
        Category.Cache
    ];

    public static string Label(this Category category) => category switch
    {
        Category.Code => "Code",
        Category.AgentScratch => "Agent scratch",
        Category.Toolchain => "Toolchains",
        Category.Synced => "Synced",
        Category.Git => "Git",
        Category.Media => "Media",
        Category.Documents => "Documents",
        Category.Cache => "Cache",
        Category.Other => "Other",
        _ => "Other"
    };
}
