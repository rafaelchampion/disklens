namespace DiskTree.Core.Treemap;

/// <summary>
/// An axis-aligned rectangle in viewport pixels.
/// </summary>
public readonly record struct TreemapRect(float X, float Y, float W, float H)
{
    public float Right => X + W;
    public float Bottom => Y + H;
    public float Area => Math.Max(0f, W) * Math.Max(0f, H);

    public bool Contains(float px, float py) =>
        px >= X && px < Right && py >= Y && py < Bottom;

    /// <summary>
    /// Shrinks on every side, never past empty.
    /// </summary>
    public TreemapRect Inset(float padding)
    {
        float w = Math.Max(0f, W - (padding * 2f));
        float h = Math.Max(0f, H - (padding * 2f));
        return new TreemapRect(X + padding, Y + padding, w, h);
    }

    public TreemapRect Scaled(float scale) =>
        new(X * scale, Y * scale, W * scale, H * scale);

    public TreemapRect Translated(float dx, float dy) =>
        new(X + dx, Y + dy, W, H);
}
