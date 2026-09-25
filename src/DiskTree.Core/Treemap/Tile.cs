namespace DiskTree.Core.Treemap;

public abstract record TileKind
{
    public sealed record Node(int[] Crumbs) : TileKind;
    public sealed record Others(int[] Crumbs, int Count) : TileKind;
}

/// <summary>
/// One rectangle of the treemap mosaic.
/// </summary>
public sealed class Tile
{
    public required TileKind Kind { get; init; }
    public required TreemapRect Rect { get; set; }
    public required int Depth { get; init; }
    public TreemapRect? Header { get; set; }

    public int[] Crumbs => Kind switch
    {
        TileKind.Node node => node.Crumbs,
        TileKind.Others others => others.Crumbs,
        _ => []
    };
}
