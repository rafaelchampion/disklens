using System.Numerics;
using Windows.Foundation;
using Windows.System;
using Windows.UI;
using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Brushes;
using Microsoft.Graphics.Canvas.Text;
using Microsoft.Graphics.Canvas.UI.Xaml;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using DiskTree.App.Rendering;
using DiskTree.App.ViewModels;
using DiskTree.Core.Classify;
using DiskTree.Core.Tree;
using DiskTree.Core.Treemap;

namespace DiskTree.App.Controls;

public sealed class TreemapCanvasView : UserControl
{
    private readonly CanvasControl _canvas;
    private MainViewModel? _viewModel;

    private List<Tile> _cachedTiles = [];
    private Tile? _hoveredTile;
    private Vector2 _pointerPosition;
    private bool _isPointerOver;
    private LayoutOptions _layoutOptions = new();

    private CanvasTextFormat? _headerFormat;
    private CanvasTextFormat? _headerSizeFormat;
    private CanvasTextFormat? _labelFormat;
    private CanvasTextFormat? _detailFormat;
    private CanvasTextFormat? _compactFormat;
    private CanvasTextFormat? _centerMessageFormat;
    private CanvasTextFormat? _tooltipTitleFormat;
    private CanvasTextFormat? _tooltipSubFormat;
    private CanvasTextFormat? _tooltipHintFormat;

    public TreemapCanvasView()
    {
        _canvas = new CanvasControl();
        _canvas.CreateResources += OnCreateResources;
        _canvas.Draw += OnDraw;
        _canvas.SizeChanged += OnSizeChanged;

        _canvas.PointerMoved += OnPointerMoved;
        _canvas.PointerPressed += OnPointerPressed;
        _canvas.DoubleTapped += OnDoubleTapped;
        _canvas.PointerWheelChanged += OnPointerWheelChanged;
        _canvas.PointerExited += OnPointerExited;

        ActualThemeChanged += (_, _) => _canvas.Invalidate();

        Content = _canvas;
    }

    public void AttachViewModel(MainViewModel viewModel)
    {
        _viewModel = viewModel;
        _viewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(MainViewModel.CurrentViewNode)
                or nameof(MainViewModel.RootNode)
                or nameof(MainViewModel.Metric)
                or nameof(MainViewModel.CurrentFilter))
            {
                RecomputeLayout();
                _canvas.Invalidate();
            }
            else if (e.PropertyName is nameof(MainViewModel.SelectedNode)
                or nameof(MainViewModel.IsSelectedMarked)
                or nameof(MainViewModel.MarkedBytes))
            {
                _canvas.Invalidate();
            }
        };

        RecomputeLayout();
        _canvas.Invalidate();
    }

    private void OnCreateResources(
        CanvasControl sender,
        Microsoft.Graphics.Canvas.UI.CanvasCreateResourcesEventArgs args)
    {
        _headerFormat = new CanvasTextFormat
        {
            FontFamily = "Segoe UI Variable Display, Segoe UI",
            FontSize = 11.5f,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            WordWrapping = CanvasWordWrapping.NoWrap,
            TrimmingGranularity = CanvasTextTrimmingGranularity.Character,
            Options = CanvasDrawTextOptions.Clip
        };

        _headerSizeFormat = new CanvasTextFormat
        {
            FontFamily = "Segoe UI Variable Text, Segoe UI",
            FontSize = 10.0f,
            FontWeight = Microsoft.UI.Text.FontWeights.Normal,
            HorizontalAlignment = CanvasHorizontalAlignment.Right,
            WordWrapping = CanvasWordWrapping.NoWrap,
            TrimmingGranularity = CanvasTextTrimmingGranularity.Character,
            Options = CanvasDrawTextOptions.Clip
        };

        _labelFormat = new CanvasTextFormat
        {
            FontFamily = "Segoe UI Variable Text, Segoe UI",
            FontSize = 11.0f,
            FontWeight = Microsoft.UI.Text.FontWeights.Medium,
            WordWrapping = CanvasWordWrapping.NoWrap,
            TrimmingGranularity = CanvasTextTrimmingGranularity.Character,
            Options = CanvasDrawTextOptions.Clip
        };

        _detailFormat = new CanvasTextFormat
        {
            FontFamily = "Segoe UI Variable Text, Segoe UI",
            FontSize = 9.5f,
            FontWeight = Microsoft.UI.Text.FontWeights.Normal,
            WordWrapping = CanvasWordWrapping.NoWrap,
            TrimmingGranularity = CanvasTextTrimmingGranularity.Character,
            Options = CanvasDrawTextOptions.Clip
        };

        _compactFormat = new CanvasTextFormat
        {
            FontFamily = "Segoe UI Variable Text, Segoe UI",
            FontSize = 9.5f,
            FontWeight = Microsoft.UI.Text.FontWeights.Normal,
            WordWrapping = CanvasWordWrapping.NoWrap,
            TrimmingGranularity = CanvasTextTrimmingGranularity.Character,
            Options = CanvasDrawTextOptions.Clip
        };

        _centerMessageFormat = new CanvasTextFormat
        {
            FontFamily = "Segoe UI Variable Text, Segoe UI",
            FontSize = 14.0f,
            FontWeight = Microsoft.UI.Text.FontWeights.Normal,
            HorizontalAlignment = CanvasHorizontalAlignment.Center,
            VerticalAlignment = CanvasVerticalAlignment.Center,
            WordWrapping = CanvasWordWrapping.NoWrap
        };

        _tooltipTitleFormat = new CanvasTextFormat
        {
            FontFamily = "Segoe UI Variable Text, Segoe UI",
            FontSize = 12.0f,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            WordWrapping = CanvasWordWrapping.NoWrap,
            TrimmingGranularity = CanvasTextTrimmingGranularity.Character,
            Options = CanvasDrawTextOptions.Clip
        };

        _tooltipSubFormat = new CanvasTextFormat
        {
            FontFamily = "Segoe UI Variable Text, Segoe UI",
            FontSize = 9.5f,
            FontWeight = Microsoft.UI.Text.FontWeights.Normal,
            WordWrapping = CanvasWordWrapping.NoWrap,
            TrimmingGranularity = CanvasTextTrimmingGranularity.Character,
            Options = CanvasDrawTextOptions.Clip
        };

        _tooltipHintFormat = new CanvasTextFormat
        {
            FontFamily = "Segoe UI Variable Text, Segoe UI",
            FontSize = 8.5f,
            FontWeight = Microsoft.UI.Text.FontWeights.Normal,
            WordWrapping = CanvasWordWrapping.NoWrap,
            TrimmingGranularity = CanvasTextTrimmingGranularity.Character,
            Options = CanvasDrawTextOptions.Clip
        };
    }

    private void OnSizeChanged(object sender, SizeChangedEventArgs e)
    {
        RecomputeLayout();
        _canvas.Invalidate();
    }

    public void RecomputeLayout()
    {
        if (_viewModel?.RootNode == null)
        {
            _cachedTiles.Clear();
            return;
        }

        var viewNode = _viewModel.CurrentViewNode ?? _viewModel.RootNode;
        var viewCrumbs = _viewModel.CurrentViewCrumbs;

        float width = (float)_canvas.ActualWidth;
        float height = (float)_canvas.ActualHeight;

        if (width <= 10f || height <= 10f)
            return;

        var area = new TreemapRect(0, 0, width, height);
        _cachedTiles = TreemapLayout.Layout(
            viewNode,
            viewCrumbs.ToArray(),
            area,
            _viewModel.Metric,
            _layoutOptions,
            _viewModel.CurrentFilter
        );
    }

    private void OnDraw(CanvasControl sender, CanvasDrawEventArgs args)
    {
        var ds = args.DrawingSession;
        bool isDark = ActualTheme != ElementTheme.Light;

        // Background surface
        Color bg = isDark
            ? Color.FromArgb(255, 20, 20, 24)
            : Color.FromArgb(255, 243, 243, 246);
        ds.Clear(bg);

        if (_viewModel?.RootNode == null || _cachedTiles.Count == 0)
        {
            string msg = _viewModel?.IsScanning == true
                ? "Scanning directory tree..."
                : "Choose a folder or drive to inspect disk usage";
            var centerRect = new Rect(
                0, 0, (float)_canvas.ActualWidth, (float)_canvas.ActualHeight);
            if (_centerMessageFormat != null)
            {
                ds.DrawText(
                    msg,
                    centerRect,
                    isDark ? Colors.Gray : Colors.DimGray,
                    _centerMessageFormat);
            }
            return;
        }

        var root = _viewModel.RootNode;
        int[]? selectedCrumbs = _viewModel.SelectedCrumbs?.ToArray();

        // 1. Draw tile rectangles, colors, hatchings and labels
        foreach (var tile in _cachedTiles)
        {
            var rect = new Rect(
                tile.Rect.X, tile.Rect.Y, tile.Rect.W, tile.Rect.H);

            if (tile.Kind is TileKind.Others others)
            {
                Color othersColor = isDark
                    ? Color.FromArgb(255, 38, 38, 44)
                    : Color.FromArgb(255, 220, 220, 225);
                ds.FillRectangle(rect, othersColor);

                if (tile.Rect.W > 40f && tile.Rect.H > 14f && _detailFormat != null)
                {
                    Color othersTextColor = isDark
                        ? Color.FromArgb(170, 190, 190, 200)
                        : Color.FromArgb(170, 80, 80, 90);
                    ds.DrawText(
                        $"Others ({others.Count:N0})",
                        new Rect(
                            tile.Rect.X + 3f,
                            tile.Rect.Y + 2f,
                            Math.Max(0, tile.Rect.W - 6f),
                            Math.Max(0, tile.Rect.H - 4f)),
                        othersTextColor,
                        _detailFormat);
                }
                continue;
            }

            var node = root.Resolve(tile.Crumbs);
            if (node == null)
                continue;

            Color fillColor = DisktreePalette.CategoryFill(
                node.Category, tile.Depth, isDark);
            ds.FillRectangle(rect, fillColor);

            // Reclaimable diagonal hatch pattern
            if (node.Reclaim.HasValue)
            {
                DrawDiagonalHatch(ds, rect, DisktreePalette.ReclaimHatch);
            }

            // Header band for subdivided directories
            if (tile.Header.HasValue)
            {
                var h = tile.Header.Value;
                var hRect = new Rect(h.X, h.Y, h.W, h.H);
                Color headerBg = DisktreePalette.CategoryAccent(
                    node.Category, isDark);
                ds.FillRectangle(hRect, headerBg);

                Color textColor = Colors.White;
                string sizeStr = DisktreePalette.FormatBytes(node.Bytes);

                if (h.W > 130f && _headerFormat != null && _headerSizeFormat != null)
                {
                    // Show name on left and size on right
                    float sizeWidth = Math.Min(75f, h.W * 0.35f);
                    float nameWidth = Math.Max(0, h.W - sizeWidth - 10f);

                    ds.DrawText(
                        node.Name,
                        new Rect(
                            h.X + 5f, h.Y + 2f,
                            nameWidth, Math.Max(0, h.H - 4f)),
                        textColor,
                        _headerFormat);

                    ds.DrawText(
                        sizeStr,
                        new Rect(
                            h.X + h.W - sizeWidth - 5f, h.Y + 2f,
                            sizeWidth, Math.Max(0, h.H - 4f)),
                        Color.FromArgb(220, 255, 255, 255),
                        _headerSizeFormat);
                }
                else if (_headerFormat != null)
                {
                    ds.DrawText(
                        node.Name,
                        new Rect(
                            h.X + 4f, h.Y + 2f,
                            Math.Max(0, h.W - 8f), Math.Max(0, h.H - 4f)),
                        textColor,
                        _headerFormat);
                }
            }
            else if (tile.Rect.W > 28f && tile.Rect.H > 14f)
            {
                // Leaf or closed directory text
                Color textColor = isDark
                    ? Color.FromArgb(245, 255, 255, 255)
                    : Color.FromArgb(245, 20, 20, 20);
                string sizeStr = DisktreePalette.FormatBytes(node.Bytes);

                if (tile.Rect.H >= 28f && _labelFormat != null && _detailFormat != null)
                {
                    // Two lines: Name on line 1, Size on line 2
                    ds.DrawText(
                        node.Name,
                        new Rect(
                            tile.Rect.X + 4f,
                            tile.Rect.Y + 2f,
                            Math.Max(0, tile.Rect.W - 8f),
                            15f),
                        textColor,
                        _labelFormat);

                    Color subColor = isDark
                        ? Color.FromArgb(180, 200, 200, 215)
                        : Color.FromArgb(180, 60, 60, 75);
                    ds.DrawText(
                        sizeStr,
                        new Rect(
                            tile.Rect.X + 4f,
                            tile.Rect.Y + 16f,
                            Math.Max(0, tile.Rect.W - 8f),
                            Math.Max(0, tile.Rect.H - 18f)),
                        subColor,
                        _detailFormat);
                }
                else if (_compactFormat != null)
                {
                    // Single line
                    string singleLine = tile.Rect.W >= 70f
                        ? $"{node.Name} • {sizeStr}"
                        : node.Name;

                    ds.DrawText(
                        singleLine,
                        new Rect(
                            tile.Rect.X + 4f,
                            tile.Rect.Y + 1f,
                            Math.Max(0, tile.Rect.W - 8f),
                            Math.Max(0, tile.Rect.H - 2f)),
                        textColor,
                        _compactFormat);
                }
            }

            // Marked danger indicator
            string path = PathForCrumbs(tile.Crumbs);
            bool isMarked = !string.IsNullOrEmpty(path)
                && _viewModel.MarkedTargets.Any(
                    t => t.Path.Equals(path, StringComparison.OrdinalIgnoreCase));
            if (isMarked)
            {
                ds.FillRectangle(rect, Color.FromArgb(90, 239, 68, 68));
                ds.DrawRectangle(rect, DisktreePalette.DangerRed, 2.0f);
            }
        }

        // 2. Draw hover and selection borders
        if (_hoveredTile != null
            && _hoveredTile != _cachedTiles.FirstOrDefault(
                t => AreCrumbsEqual(t.Crumbs, selectedCrumbs)))
        {
            var hRect = new Rect(
                _hoveredTile.Rect.X, _hoveredTile.Rect.Y,
                _hoveredTile.Rect.W, _hoveredTile.Rect.H);
            ds.DrawRectangle(
                hRect,
                isDark
                    ? Color.FromArgb(200, 255, 255, 255)
                    : Color.FromArgb(200, 40, 40, 40),
                1.5f);
        }

        if (selectedCrumbs != null)
        {
            var selectedTile = _cachedTiles.FirstOrDefault(
                t => AreCrumbsEqual(t.Crumbs, selectedCrumbs));
            if (selectedTile != null)
            {
                var sRect = new Rect(
                    selectedTile.Rect.X, selectedTile.Rect.Y,
                    selectedTile.Rect.W, selectedTile.Rect.H);
                ds.DrawRectangle(sRect, DisktreePalette.HighlightAmber, 2.5f);
            }
        }

        // 3. Draw interactive floating hover tooltip
        if (_isPointerOver && _hoveredTile != null)
        {
            DrawHoverTooltip(
                ds, _hoveredTile, _pointerPosition, root, isDark,
                (float)_canvas.ActualWidth, (float)_canvas.ActualHeight);
        }
    }

    private void DrawHoverTooltip(
        CanvasDrawingSession ds,
        Tile tile,
        Vector2 pt,
        Node root,
        bool isDark,
        float canvasWidth,
        float canvasHeight)
    {
        if (tile.Kind is TileKind.Others others)
        {
            DrawOthersTooltip(
                ds, others, pt, isDark, canvasWidth, canvasHeight);
            return;
        }

        var node = root.Resolve(tile.Crumbs);
        if (node == null || _tooltipTitleFormat == null || _tooltipSubFormat == null || _tooltipHintFormat == null)
            return;

        string name = string.IsNullOrEmpty(node.Name)
            ? (node.IsDirectory ? "Folder" : "File")
            : node.Name;
        string path = PathForCrumbs(tile.Crumbs);
        string sizeStr = DisktreePalette.FormatBytes(node.Bytes);
        string itemsStr = node.IsDirectory
            ? $"{node.Files:N0} files, {node.Dirs:N0} dirs"
            : (node.Modified > 0
                ? DateTimeOffset.FromUnixTimeSeconds(node.Modified)
                    .LocalDateTime.ToString("yyyy-MM-dd HH:mm")
                : "Single file");
        string categoryStr = node.Category.Label();
        string reclaimStr = node.Reclaim.HasValue
            ? $"Reclaimable: {node.Reclaim.Value.Label()}"
            : string.Empty;

        float cardW = Math.Clamp(canvasWidth * 0.42f, 280f, 380f);
        float cardH = string.IsNullOrEmpty(reclaimStr) ? 82f : 100f;

        float tipX = pt.X + 16f;
        float tipY = pt.Y + 16f;

        if (tipX + cardW > canvasWidth - 10f)
            tipX = pt.X - cardW - 12f;
        if (tipY + cardH > canvasHeight - 10f)
            tipY = pt.Y - cardH - 12f;

        tipX = Math.Clamp(tipX, 8f, Math.Max(8f, canvasWidth - cardW - 8f));
        tipY = Math.Clamp(tipY, 8f, Math.Max(8f, canvasHeight - cardH - 8f));

        var cardRect = new Rect(tipX, tipY, cardW, cardH);

        // Subtle soft drop shadow
        ds.FillRoundedRectangle(
            new Rect(tipX + 2f, tipY + 2f, cardW, cardH),
            6f, 6f, Color.FromArgb(50, 0, 0, 0));

        // Acrylic / Card background
        Color cardBg = isDark
            ? Color.FromArgb(245, 32, 32, 38)
            : Color.FromArgb(248, 255, 255, 255);
        ds.FillRoundedRectangle(cardRect, 6f, 6f, cardBg);

        // Border stroke
        Color borderColor = isDark
            ? Color.FromArgb(120, 80, 80, 95)
            : Color.FromArgb(100, 200, 200, 215);
        ds.DrawRoundedRectangle(cardRect, 6f, 6f, borderColor, 1.0f);

        // Category Accent Pill on left edge
        Color accentColor = DisktreePalette.CategoryAccent(
            node.Category, isDark);
        ds.FillRoundedRectangle(
            new Rect(tipX + 4f, tipY + 8f, 3.5f, cardH - 16f),
            2f, 2f, accentColor);

        // Line 1: Title / Name
        Color titleColor = isDark
            ? Colors.White
            : Color.FromArgb(255, 20, 20, 20);
        ds.DrawText(
            name,
            new Rect(tipX + 14f, tipY + 6f, cardW - 22f, 18f),
            titleColor,
            _tooltipTitleFormat);

        // Line 2: Path (dimmed, ellipsis)
        Color pathColor = isDark
            ? Color.FromArgb(160, 190, 190, 200)
            : Color.FromArgb(160, 100, 100, 115);
        ds.DrawText(
            path,
            new Rect(tipX + 14f, tipY + 26f, cardW - 22f, 15f),
            pathColor,
            _tooltipSubFormat);

        // Line 3: Stats (Size • Files/Modified • Category)
        Color statsColor = isDark
            ? Color.FromArgb(220, 220, 225, 235)
            : Color.FromArgb(220, 45, 45, 55);
        string statsText = $"{sizeStr}  •  {itemsStr}  •  {categoryStr}";
        ds.DrawText(
            statsText,
            new Rect(tipX + 14f, tipY + 44f, cardW - 22f, 16f),
            statsColor,
            _tooltipSubFormat);

        // Line 4: Reclaim (if applicable)
        if (!string.IsNullOrEmpty(reclaimStr))
        {
            Color reclaimColor = isDark
                ? Color.FromArgb(240, 245, 158, 11)
                : Color.FromArgb(240, 180, 83, 9);
            ds.DrawText(
                reclaimStr,
                new Rect(tipX + 14f, tipY + 62f, cardW - 22f, 15f),
                reclaimColor,
                _tooltipSubFormat);
        }

        // Action hint footer
        float hintY = string.IsNullOrEmpty(reclaimStr)
            ? tipY + 63f
            : tipY + 80f;
        Color hintColor = isDark
            ? Color.FromArgb(120, 160, 160, 175)
            : Color.FromArgb(120, 130, 130, 145);
        string hint = node.IsDirectory
            ? "Click to select  •  Double-click to explore folder"
            : "Click to select  •  Double-click to zoom to folder";

        ds.DrawText(
            hint,
            new Rect(tipX + 14f, hintY, cardW - 22f, 14f),
            hintColor,
            _tooltipHintFormat);
    }

    private void DrawOthersTooltip(
        CanvasDrawingSession ds,
        TileKind.Others others,
        Vector2 pt,
        bool isDark,
        float canvasWidth,
        float canvasHeight)
    {
        if (_tooltipTitleFormat == null || _tooltipSubFormat == null)
            return;

        float cardW = 250f;
        float cardH = 56f;

        float tipX = pt.X + 16f;
        float tipY = pt.Y + 16f;

        if (tipX + cardW > canvasWidth - 10f)
            tipX = pt.X - cardW - 12f;
        if (tipY + cardH > canvasHeight - 10f)
            tipY = pt.Y - cardH - 12f;

        tipX = Math.Clamp(tipX, 8f, Math.Max(8f, canvasWidth - cardW - 8f));
        tipY = Math.Clamp(tipY, 8f, Math.Max(8f, canvasHeight - cardH - 8f));

        var cardRect = new Rect(tipX, tipY, cardW, cardH);
        ds.FillRoundedRectangle(
            new Rect(tipX + 2f, tipY + 2f, cardW, cardH),
            6f, 6f, Color.FromArgb(50, 0, 0, 0));

        Color cardBg = isDark
            ? Color.FromArgb(245, 32, 32, 38)
            : Color.FromArgb(248, 255, 255, 255);
        ds.FillRoundedRectangle(cardRect, 6f, 6f, cardBg);

        Color borderColor = isDark
            ? Color.FromArgb(120, 80, 80, 95)
            : Color.FromArgb(100, 200, 200, 215);
        ds.DrawRoundedRectangle(cardRect, 6f, 6f, borderColor, 1.0f);

        Color titleColor = isDark
            ? Colors.White
            : Color.FromArgb(255, 20, 20, 20);
        ds.DrawText(
            $"Others ({others.Count:N0} items)",
            new Rect(tipX + 12f, tipY + 8f, cardW - 20f, 18f),
            titleColor,
            _tooltipTitleFormat);

        Color pathColor = isDark
            ? Color.FromArgb(160, 190, 190, 200)
            : Color.FromArgb(160, 100, 100, 115);
        ds.DrawText(
            "Merged small files and directories",
            new Rect(tipX + 12f, tipY + 28f, cardW - 20f, 15f),
            pathColor,
            _tooltipSubFormat);
    }

    private static void DrawDiagonalHatch(
        CanvasDrawingSession ds, Rect rect, Color color)
    {
        float spacing = 9f;
        float xMin = (float)rect.X;
        float yMin = (float)rect.Y;
        float xMax = (float)(rect.X + rect.Width);
        float yMax = (float)(rect.Y + rect.Height);

        // Draw clipped diagonal lines across rect
        for (float offset = -(float)rect.Height;
             offset < rect.Width;
             offset += spacing)
        {
            float x1 = xMin + offset;
            float y1 = yMin;
            float x2 = x1 + (float)rect.Height;
            float y2 = yMax;

            // Clip line to rect
            if (x1 < xMin)
            {
                y1 += (xMin - x1);
                x1 = xMin;
            }
            if (x2 > xMax)
            {
                y2 -= (x2 - xMax);
                x2 = xMax;
            }

            if (x1 < xMax && y1 < yMax && x2 > xMin && y2 > yMin)
            {
                ds.DrawLine(
                    new Vector2(x1, y1),
                    new Vector2(x2, y2),
                    color, 1.2f);
            }
        }
    }

    private void OnPointerMoved(object sender, PointerRoutedEventArgs e)
    {
        var pt = e.GetCurrentPoint(_canvas).Position;
        _pointerPosition = new Vector2((float)pt.X, (float)pt.Y);
        _isPointerOver = true;

        var hit = TreemapLayout.Hit(
            _cachedTiles, (float)pt.X, (float)pt.Y);

        if (hit != _hoveredTile)
        {
            _hoveredTile = hit;
        }
        _canvas.Invalidate();
    }

    private void OnPointerExited(object sender, PointerRoutedEventArgs e)
    {
        _isPointerOver = false;
        _hoveredTile = null;
        _canvas.Invalidate();
    }

    private void OnPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        var pt = e.GetCurrentPoint(_canvas).Position;
        var hit = TreemapLayout.Hit(
            _cachedTiles, (float)pt.X, (float)pt.Y);

        if (hit != null
            && hit.Kind is TileKind.Node
            && _viewModel?.RootNode != null)
        {
            var node = _viewModel.RootNode.Resolve(hit.Crumbs);
            if (node != null)
            {
                _viewModel.SelectNode(hit.Crumbs, node);
            }
        }
        else if (hit == null)
        {
            _viewModel?.ClearSelection();
        }

        _canvas.Invalidate();
    }

    private void OnDoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        var pt = e.GetPosition(_canvas);
        var hit = TreemapLayout.Hit(
            _cachedTiles, (float)pt.X, (float)pt.Y);

        if (hit != null
            && hit.Kind is TileKind.Node
            && _viewModel?.RootNode != null)
        {
            var node = _viewModel.RootNode.Resolve(hit.Crumbs);
            if (node != null)
            {
                if (node.IsDirectory)
                {
                    _viewModel.CurrentViewCrumbs = [.. hit.Crumbs];
                }
                else if (hit.Crumbs.Length > 0)
                {
                    // Double-clicking a file drills into its containing folder
                    var parentCrumbs = hit.Crumbs[..^1];
                    _viewModel.CurrentViewCrumbs = [.. parentCrumbs];
                }
            }
        }
    }


    private void OnPointerWheelChanged(
        object sender, PointerRoutedEventArgs e)
    {
        var props = e.GetCurrentPoint(_canvas).Properties;
        int delta = props.MouseWheelDelta;

        if (delta < 0 && _viewModel?.CurrentViewCrumbs.Count > 0)
        {
            // Scroll down: navigate up
            _viewModel.NavigateUp();
        }
    }

    private string PathForCrumbs(ReadOnlySpan<int> crumbs)
    {
        if (_viewModel?.RootNode == null
            || string.IsNullOrEmpty(_viewModel.ScannedRoot))
            return string.Empty;

        var chain = _viewModel.RootNode.ResolveChain(crumbs);
        var parts = chain.Skip(1).Select(n => n.Name);
        return System.IO.Path.Combine(
            _viewModel.ScannedRoot,
            System.IO.Path.Combine(parts.ToArray()));
    }

    private static bool AreCrumbsEqual(int[]? a, int[]? b)
    {
        if (a == null && b == null) return true;
        if (a == null || b == null) return false;
        if (a.Length != b.Length) return false;
        for (int i = 0; i < a.Length; i++)
        {
            if (a[i] != b[i]) return false;
        }
        return true;
    }
}
