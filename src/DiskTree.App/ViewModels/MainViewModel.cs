using System.ComponentModel;
using System.Runtime.CompilerServices;
using DiskTree.Core.Classify;
using DiskTree.Core.Filter;
using DiskTree.Core.Insights;
using DiskTree.Core.Removal;
using DiskTree.Core.Scan;
using DiskTree.Core.Space;
using DiskTree.Core.Tree;

namespace DiskTree.App.ViewModels;

public sealed class MainViewModel : INotifyPropertyChanged
{
    private string _scannedRoot = string.Empty;
    private Node? _rootNode;
    private List<int> _currentViewCrumbs = [];
    private List<int>? _selectedCrumbs;
    private Node? _selectedNode;
    private string _selectedPath = string.Empty;
    private readonly Dictionary<string, Target> _markedTargets = new(StringComparer.OrdinalIgnoreCase);
    private ulong _markedBytes;
    private SpaceInfo? _space;
    private List<Candidate> _worthALookItems = [];
    private string _searchQuery = string.Empty;
    private FilterMatches? _currentFilter;
    private Metric _metric = Metric.Bytes;
    private bool _isScanning;
    private string _scanStatusText = "Ready";
    private ulong _scanErrorCount;
    private ScanProgress? _activeScanProgress;

    public event PropertyChangedEventHandler? PropertyChanged;

    public string ScannedRoot
    {
        get => _scannedRoot;
        set => SetField(ref _scannedRoot, value);
    }

    public Node? RootNode
    {
        get => _rootNode;
        private set
        {
            if (SetField(ref _rootNode, value))
            {
                OnPropertyChanged(nameof(CurrentViewNode));
                OnPropertyChanged(nameof(BreadcrumbItems));
            }
        }
    }

    public List<int> CurrentViewCrumbs
    {
        get => _currentViewCrumbs;
        set
        {
            if (SetField(ref _currentViewCrumbs, value))
            {
                OnPropertyChanged(nameof(CurrentViewNode));
                OnPropertyChanged(nameof(BreadcrumbItems));
            }
        }
    }

    public Node? CurrentViewNode => RootNode?.Resolve(CurrentViewCrumbs.ToArray());

    public List<int>? SelectedCrumbs
    {
        get => _selectedCrumbs;
        private set => SetField(ref _selectedCrumbs, value);
    }

    public Node? SelectedNode
    {
        get => _selectedNode;
        private set
        {
            if (SetField(ref _selectedNode, value))
            {
                OnPropertyChanged(nameof(IsNodeSelected));
                OnPropertyChanged(nameof(IsSelectedMarked));
            }
        }
    }

    public bool IsNodeSelected => SelectedNode != null;

    public string SelectedPath
    {
        get => _selectedPath;
        private set => SetField(ref _selectedPath, value);
    }

    public IReadOnlyCollection<Target> MarkedTargets => _markedTargets.Values;

    public ulong MarkedBytes
    {
        get => _markedBytes;
        private set
        {
            if (SetField(ref _markedBytes, value))
            {
                OnPropertyChanged(nameof(HasMarks));
                OnPropertyChanged(nameof(MarkedCount));
                OnPropertyChanged(nameof(ProjectedSpace));
            }
        }
    }

    public bool HasMarks => _markedTargets.Count > 0;
    public int MarkedCount => _markedTargets.Count;

    public bool IsSelectedMarked => !string.IsNullOrEmpty(SelectedPath) && _markedTargets.ContainsKey(SelectedPath);

    public SpaceInfo? Space
    {
        get => _space;
        private set
        {
            if (SetField(ref _space, value))
            {
                OnPropertyChanged(nameof(ProjectedSpace));
            }
        }
    }

    public SpaceInfo? ProjectedSpace => Space?.AfterRemoving(MarkedBytes);

    public List<Candidate> WorthALookItems
    {
        get => _worthALookItems;
        private set => SetField(ref _worthALookItems, value);
    }

    public string SearchQuery
    {
        get => _searchQuery;
        set
        {
            if (SetField(ref _searchQuery, value))
            {
                UpdateFilter();
            }
        }
    }

    public FilterMatches? CurrentFilter
    {
        get => _currentFilter;
        private set => SetField(ref _currentFilter, value);
    }

    public Metric Metric
    {
        get => _metric;
        set
        {
            if (SetField(ref _metric, value))
            {
                if (RootNode != null)
                {
                    TreeAggregation.Aggregate(RootNode, _metric);
                    OnPropertyChanged(nameof(RootNode));
                    OnPropertyChanged(nameof(CurrentViewNode));
                }
            }
        }
    }

    public bool IsScanning
    {
        get => _isScanning;
        private set => SetField(ref _isScanning, value);
    }

    public string ScanStatusText
    {
        get => _scanStatusText;
        set => SetField(ref _scanStatusText, value);
    }

    public ulong ScanErrorCount
    {
        get => _scanErrorCount;
        private set => SetField(ref _scanErrorCount, value);
    }

    public IReadOnlyList<string> BreadcrumbItems
    {
        get
        {
            if (RootNode == null) return [];
            var chain = RootNode.ResolveChain(CurrentViewCrumbs.ToArray());
            return chain.Select(n => n.Name).ToList();
        }
    }

    public async Task StartScanAsync(string directoryPath)
    {
        if (string.IsNullOrWhiteSpace(directoryPath) || !Directory.Exists(directoryPath))
            return;

        _activeScanProgress?.Cancel();

        IsScanning = true;
        ScannedRoot = Path.GetFullPath(directoryPath);
        ScanStatusText = $"Scanning {ScannedRoot}...";
        ScanErrorCount = 0;
        _markedTargets.Clear();
        MarkedBytes = 0;

        Space = VolumeSpace.GetSpaceInfo(ScannedRoot);

        var progress = new ScanProgress();
        _activeScanProgress = progress;

        using var cts = new CancellationTokenSource();
        var timer = new System.Timers.Timer(150);
        timer.Elapsed += (_, _) =>
        {
            var snap = progress.Snapshot();
            string phase = snap.Phase != null
                ? $"[{snap.Phase}] "
                : "";
            ScanStatusText = $"{phase}Scanned {snap.Files:N0} files, {snap.Dirs:N0} dirs ({Rendering.DisktreePalette.FormatBytes(snap.Bytes)})...";
            ScanErrorCount = snap.Errors;
        };
        timer.Start();

        try
        {
            var options = new ScanOptions
            {
                Metric = Metric,
                IncludeHidden = true,
                DedupHardlinks = true
            };

            var root = await DirectoryScanner.ScanAsync(ScannedRoot, options, progress, cts.Token);
            timer.Stop();

            RootNode = root;
            CurrentViewCrumbs = [];
            SelectNode([], root);

            long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            WorthALookItems = InsightFinder.WorthALook(root, now, limit: 8);

            var snap = progress.Snapshot();
            ScanStatusText = $"Scanned {snap.Files:N0} files, {snap.Dirs:N0} dirs in {ScannedRoot} ({Rendering.DisktreePalette.FormatBytes(root.Bytes)})";
            ScanErrorCount = snap.Errors;
        }
        catch (Exception ex)
        {
            timer.Stop();
            ScanStatusText = $"Scan failed: {ex.Message}";
        }
        finally
        {
            timer.Dispose();
            IsScanning = false;
        }
    }

    public void SelectNode(int[] crumbs, Node node)
    {
        SelectedCrumbs = [.. crumbs];
        SelectedNode = node;
        SelectedPath = BuildPath(crumbs);
        OnPropertyChanged(nameof(IsSelectedMarked));
    }

    public void ClearSelection()
    {
        SelectedCrumbs = null;
        SelectedNode = null;
        SelectedPath = string.Empty;
    }

    public void DrillIntoCurrentSelection()
    {
        if (SelectedNode != null && SelectedCrumbs != null)
        {
            if (SelectedNode.IsDirectory)
            {
                CurrentViewCrumbs = [.. SelectedCrumbs];
            }
            else if (SelectedCrumbs.Count > 0)
            {
                CurrentViewCrumbs = [.. SelectedCrumbs.Take(SelectedCrumbs.Count - 1)];
            }
        }
    }


    public void NavigateToDepth(int indexInChain)
    {
        if (indexInChain == 0)
        {
            CurrentViewCrumbs = [];
        }
        else if (indexInChain > 0 && indexInChain <= CurrentViewCrumbs.Count)
        {
            CurrentViewCrumbs = CurrentViewCrumbs.Take(indexInChain).ToList();
        }
    }

    public void NavigateUp()
    {
        if (CurrentViewCrumbs.Count > 0)
        {
            CurrentViewCrumbs.RemoveAt(CurrentViewCrumbs.Count - 1);
            OnPropertyChanged(nameof(CurrentViewCrumbs));
            OnPropertyChanged(nameof(CurrentViewNode));
            OnPropertyChanged(nameof(BreadcrumbItems));
        }
    }

    public void ToggleMarkForSelection()
    {
        if (SelectedNode == null || string.IsNullOrEmpty(SelectedPath))
            return;

        if (_markedTargets.ContainsKey(SelectedPath))
        {
            _markedTargets.Remove(SelectedPath);
        }
        else
        {
            var target = new Target(SelectedPath, SelectedNode.Bytes, SelectedNode.IsDirectory, false);
            _markedTargets[SelectedPath] = target;
        }

        MarkedBytes = _markedTargets.Values.Aggregate(0UL, (acc, t) => acc + t.Bytes);
        OnPropertyChanged(nameof(IsSelectedMarked));
        OnPropertyChanged(nameof(MarkedTargets));
    }

    public void MarkCandidate(Candidate candidate)
    {
        if (RootNode == null) return;
        var node = RootNode.Resolve(candidate.Crumbs);
        if (node == null) return;

        string path = BuildPath(candidate.Crumbs);
        _markedTargets[path] = new Target(path, node.Bytes, node.IsDirectory, false);
        MarkedBytes = _markedTargets.Values.Aggregate(0UL, (acc, t) => acc + t.Bytes);
        OnPropertyChanged(nameof(IsSelectedMarked));
        OnPropertyChanged(nameof(MarkedTargets));
    }

    public void ClearMarks()
    {
        _markedTargets.Clear();
        MarkedBytes = 0;
        OnPropertyChanged(nameof(IsSelectedMarked));
        OnPropertyChanged(nameof(MarkedTargets));
    }

    public RemovalPlan BuildRemovalPlan()
    {
        return RemovalEngine.BuildPlan(_markedTargets.Values, ScannedRoot);
    }

    public void RefreshSpace()
    {
        if (!string.IsNullOrEmpty(ScannedRoot))
        {
            Space = VolumeSpace.GetSpaceInfo(ScannedRoot);
        }
    }

    private void UpdateFilter()
    {
        if (RootNode == null || string.IsNullOrWhiteSpace(SearchQuery))
        {
            CurrentFilter = null;
            return;
        }

        CurrentFilter = FilterMatches.Filter(RootNode, [], SearchQuery);
    }

    private string BuildPath(ReadOnlySpan<int> crumbs)
    {
        if (RootNode == null) return string.Empty;
        var chain = RootNode.ResolveChain(crumbs);
        var parts = chain.Skip(1).Select(n => n.Name);
        return Path.Combine(ScannedRoot, Path.Combine(parts.ToArray()));
    }

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
