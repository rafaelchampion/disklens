namespace DiskTree.Core.Treemap;

/// <summary>
/// How much of the tree to draw, and how finely.
/// Configured for full-fidelity leaf file resolution (WizTree style).
/// </summary>
public sealed record LayoutOptions
{
    /// <summary>
    /// Nesting levels drawn at once; 32 allows full traversal to leaf files.
    /// </summary>
    public int MaxDepth { get; init; } = 32;

    /// <summary>
    /// Gap between siblings inside a directory, and inset into it.
    /// </summary>
    public float Padding { get; init; } = 0.5f;

    /// <summary>
    /// Gap between top-level directories: wider than Padding, so top-level structure reads first.
    /// </summary>
    public float PaddingOuter { get; init; } = 2.0f;

    /// <summary>
    /// Tiles below this many pixels in width or height are dropped.
    /// </summary>
    public float MinTile { get; init; } = 2.0f;

    /// <summary>
    /// Children kept per directory. 4096 ensures all files in normal directories are placed.
    /// </summary>
    public int MaxChildren { get; init; } = 4096;

    /// <summary>
    /// Height of the band a top-level directory keeps for its name.
    /// </summary>
    public float Header { get; init; } = 18.0f;

    /// <summary>
    /// Height of the slimmer band a deeper directory keeps when drawn open.
    /// </summary>
    public float HeaderInner { get; init; } = 14.0f;
}
