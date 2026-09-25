using DiskTree.Core.Filter;
using DiskTree.Core.Tree;

namespace DiskTree.Core.Treemap;

public static class TreemapLayout
{
    private sealed record Placement(
        Metric Metric,
        LayoutOptions Options,
        FilterMatches? Filter
    );

    public static List<Tile> Layout(
        Node root,
        ReadOnlySpan<int> rootCrumbs,
        TreemapRect area,
        Metric metric,
        LayoutOptions options,
        FilterMatches? filter = null)
    {
        var tiles = new List<Tile>();
        var crumbs = new List<int>(rootCrumbs.ToArray());
        var placement = new Placement(metric, options, filter);
        PlaceChildren(root, area, placement, 0, crumbs, tiles);
        return tiles;
    }

    private static void PlaceChildren(
        Node node,
        TreemapRect area,
        Placement place,
        int depth,
        List<int> crumbs,
        List<Tile> outTiles)
    {
        var (metric, options, filter) = (place.Metric, place.Options, place.Filter);
        if (node.Children.Count == 0 || area.W <= 0f || area.H <= 0f)
            return;

        var ranked = new List<(int Index, double Value)>();
        for (int i = 0; i < node.Children.Count; i++)
        {
            var child = node.Children[i];
            double val;
            if (filter == null)
            {
                val = child.Value(metric);
            }
            else
            {
                crumbs.Add(i);
                var keep = filter.GetKeep(crumbs.ToArray());
                crumbs.RemoveAt(crumbs.Count - 1);
                val = keep != null ? FilterMatches.Value(keep, child, metric) : 0;
            }

            if (val > 0)
                ranked.Add((i, val));
        }

        if (ranked.Count == 0)
            return;

        ranked.Sort((a, b) => b.Value.CompareTo(a.Value));

        int kept = Math.Min(ranked.Count, options.MaxChildren);
        var values = new List<double>(kept + 1);
        var sources = new List<int?>(kept + 1);

        for (int i = 0; i < kept; i++)
        {
            values.Add(ranked[i].Value);
            sources.Add(ranked[i].Index);
        }

        int tailCount = ranked.Count - kept;
        if (tailCount > 0)
        {
            double tailSum = 0;
            for (int i = kept; i < ranked.Count; i++)
                tailSum += ranked[i].Value;
            values.Add(tailSum);
            sources.Add(null);
        }

        var rects = Squarify(values.ToArray(), area);
        for (int slot = 0; slot < rects.Count; slot++)
        {
            float pad = depth == 0 ? options.PaddingOuter : options.Padding;
            var rect = rects[slot].Inset(pad);

            if (rect.W < options.MinTile || rect.H < options.MinTile)
                continue;

            int? index = sources[slot];
            if (!index.HasValue)
            {
                outTiles.Add(new Tile
                {
                    Kind = new TileKind.Others(crumbs.ToArray(), tailCount),
                    Rect = rect,
                    Depth = depth,
                    Header = null
                });
                continue;
            }

            var child = node.Children[index.Value];
            bool hasChildren = child.IsDirectory && child.Children.Count > 0;
            bool subdividable = hasChildren && (depth + 1 < options.MaxDepth);

            crumbs.Add(index.Value);

            if (subdividable)
            {
                // Show a header band only if there is sufficient room for both header and content
                float headerH = depth == 0 ? options.Header : options.HeaderInner;
                bool canFitHeader = rect.W >= 36f && rect.H >= (headerH + options.MinTile * 3f);

                TreemapRect? header = canFitHeader
                    ? new TreemapRect(rect.X, rect.Y, rect.W, headerH)
                    : null;

                outTiles.Add(new Tile
                {
                    Kind = new TileKind.Node(crumbs.ToArray()),
                    Rect = rect,
                    Depth = depth,
                    Header = header
                });

                TreemapRect body = header.HasValue
                    ? new TreemapRect(
                        rect.X,
                        header.Value.Bottom,
                        rect.W,
                        rect.Bottom - header.Value.Bottom)
                    : rect;

                if (body.W >= options.MinTile && body.H >= options.MinTile)
                {
                    var innerFilter = filter;
                    if (filter != null && filter.GetKeep(crumbs.ToArray()) is Keep.WholeNode)
                        innerFilter = null;

                    var innerPlacement = new Placement(metric, options, innerFilter);
                    PlaceChildren(child, body, innerPlacement, depth + 1, crumbs, outTiles);
                }
            }
            else
            {
                // Leaf file or empty directory
                outTiles.Add(new Tile
                {
                    Kind = new TileKind.Node(crumbs.ToArray()),
                    Rect = rect,
                    Depth = depth,
                    Header = null
                });
            }

            crumbs.RemoveAt(crumbs.Count - 1);
        }
    }


    public static List<TreemapRect> Squarify(IReadOnlyList<double> values, TreemapRect area)
    {
        var rects = new List<TreemapRect>(new TreemapRect[values.Count]);
        double total = 0;
        for (int i = 0; i < values.Count; i++)
        {
            if (values[i] > 0)
                total += values[i];
        }

        if (total <= 0 || area.W <= 0f || area.H <= 0f)
            return rects;

        var order = new List<int>();
        for (int i = 0; i < values.Count; i++)
        {
            if (values[i] > 0)
                order.Add(i);
        }
        order.Sort((a, b) => values[b].CompareTo(values[a]));

        double scale = ((double)area.W * (double)area.H) / total;
        var areas = new double[order.Count];
        for (int i = 0; i < order.Count; i++)
            areas[i] = values[order[i]] * scale;

        var free = area;
        int start = 0;

        while (start < areas.Length)
        {
            double side = Math.Min(free.W, free.H);
            int end = start + 1;
            double rowSum = areas[start];
            double rowWorst = WorstRatio(areas.AsSpan(start..end), rowSum, side);

            while (end < areas.Length)
            {
                double candidateSum = rowSum + areas[end];
                double candidateWorst = WorstRatio(areas.AsSpan(start..(end + 1)), candidateSum, side);
                if (candidateWorst > rowWorst)
                    break;

                rowSum = candidateSum;
                rowWorst = candidateWorst;
                end++;
            }

            if (free.W >= free.H)
            {
                // Vertical strip on the left; tiles stack top to bottom
                float stripW = (float)(rowSum / free.H);
                stripW = Math.Min(stripW, free.W);
                float y = free.Y;

                for (int i = start; i < end; i++)
                {
                    float height = stripW > 0f ? (float)(areas[i] / stripW) : 0f;
                    height = Math.Max(0f, Math.Min(height, free.Bottom - y));
                    rects[order[i]] = new TreemapRect(free.X, y, stripW, height);
                    y += height;
                }

                free = new TreemapRect(free.X + stripW, free.Y, free.W - stripW, free.H);
            }
            else
            {
                // Horizontal strip along the top; tiles run left to right
                float stripH = (float)(rowSum / free.W);
                stripH = Math.Min(stripH, free.H);
                float x = free.X;

                for (int i = start; i < end; i++)
                {
                    float width = stripH > 0f ? (float)(areas[i] / stripH) : 0f;
                    width = Math.Max(0f, Math.Min(width, free.Right - x));
                    rects[order[i]] = new TreemapRect(x, free.Y, width, stripH);
                    x += width;
                }

                free = new TreemapRect(free.X, free.Y + stripH, free.W, free.H - stripH);
            }

            start = end;
        }

        return rects;
    }

    private static double WorstRatio(ReadOnlySpan<double> areas, double rowSum, double side)
    {
        if (rowSum <= 0 || side <= 0)
            return double.PositiveInfinity;

        double thickness = rowSum / side;
        double worst = 0;

        foreach (double area in areas)
        {
            if (area <= 0 || thickness <= 0)
                continue;

            double other = area / thickness;
            double ratio = Math.Max(thickness / other, other / thickness);
            if (ratio > worst)
                worst = ratio;
        }

        return worst;
    }

    /// <summary>
    /// Finds the deepest tile containing point (px, py).
    /// </summary>
    public static Tile? Hit(IReadOnlyList<Tile> tiles, float px, float py)
    {
        for (int i = tiles.Count - 1; i >= 0; i--)
        {
            if (tiles[i].Rect.Contains(px, py))
                return tiles[i];
        }
        return null;
    }
}
