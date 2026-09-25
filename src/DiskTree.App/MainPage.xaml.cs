using System.Diagnostics;
using Windows.Storage.Pickers;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using DiskTree.App.Rendering;
using DiskTree.App.ViewModels;
using DiskTree.Core.Classify;
using DiskTree.Core.Insights;
using DiskTree.Core.Removal;
using DiskTree.Core.Tree;

namespace DiskTree.App;

public sealed partial class MainPage : Page
{
    private readonly MainViewModel _viewModel = new();
    private bool _wasScanning;
    private Microsoft.UI.Xaml.Media.Animation.Storyboard? _fadeStoryboard;

    public record CandidateDisplay(Candidate Candidate, string BytesText)
    {
        public Finding Finding => Candidate.Finding;
        public string PathText => Candidate.Path;
    }

    public MainPage()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        TreemapCanvas.AttachViewModel(_viewModel);
        _viewModel.PropertyChanged += OnViewModelPropertyChanged;

        PopulateDriveList();

        // Don't auto-scan — let the user choose a drive or folder first
        _viewModel.ScanStatusText = "Select a drive or folder to begin scanning.";
    }

    private void PopulateDriveList()
    {
        DriveMenuFlyout.Items.Clear();

        try
        {
            var drives = DriveInfo.GetDrives().Where(d => d.IsReady);
            foreach (var drive in drives)
            {
                string label = $"{drive.Name} ({drive.VolumeLabel})";
                var item = new MenuFlyoutItem
                {
                    Text = label,
                    Tag = drive.RootDirectory.FullName
                };
                item.Click += async (_, _) =>
                {
                    DriveDropdown.Content = drive.Name;
                    await _viewModel.StartScanAsync((string)item.Tag);
                };
                DriveMenuFlyout.Items.Add(item);
            }
        }
        catch
        {
            // Ignore drive enumeration errors
        }
    }

    private void OnViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            UpdateUI();
        });
    }

    private void StartProgressBarFadeOut()
    {
        _fadeStoryboard?.Stop();

        var anim = new Microsoft.UI.Xaml.Media.Animation.DoubleAnimation
        {
            From = ScanProgressBar.Opacity > 0 ? ScanProgressBar.Opacity : 1.0,
            To = 0.0,
            Duration = TimeSpan.FromMilliseconds(500),
            EasingFunction = new Microsoft.UI.Xaml.Media.Animation.CubicEase
            {
                EasingMode = Microsoft.UI.Xaml.Media.Animation.EasingMode.EaseOut
            }
        };

        _fadeStoryboard = new Microsoft.UI.Xaml.Media.Animation.Storyboard();
        _fadeStoryboard.Children.Add(anim);
        Microsoft.UI.Xaml.Media.Animation.Storyboard.SetTarget(anim, ScanProgressBar);
        Microsoft.UI.Xaml.Media.Animation.Storyboard.SetTargetProperty(anim, "Opacity");

        _fadeStoryboard.Completed += (_, _) =>
        {
            ScanProgressBar.Opacity = 0.0;
            ScanProgressBar.Value = 0.0;
            _fadeStoryboard = null;
        };

        _fadeStoryboard.Begin();
    }

    private void UpdateUI()
    {
        // 1. Breadcrumbs
        PathBreadcrumbBar.ItemsSource = _viewModel.BreadcrumbItems;

        // 2. Scan Status & Top-Edge Progress Bar
        StatusMessageText.Text = _viewModel.ScanStatusText;
        ScanProgressRing.IsActive = _viewModel.IsScanning;

        if (_viewModel.IsScanning)
        {
            _fadeStoryboard?.Stop();
            _fadeStoryboard = null;
            ScanProgressBar.Opacity = 1.0;

            if (_viewModel.Space.HasValue && _viewModel.Space.Value.Used > 0)
            {
                ScanProgressBar.IsIndeterminate = false;
                ScanProgressBar.Value = _viewModel.ScanProgressPercent;
            }
            else
            {
                ScanProgressBar.IsIndeterminate = true;
            }

            _wasScanning = true;
        }
        else if (_wasScanning)
        {
            _wasScanning = false;
            ScanProgressBar.IsIndeterminate = false;
            ScanProgressBar.Value = 100;

            StartProgressBarFadeOut();
        }
        else if (ScanProgressBar.Opacity > 0 && _fadeStoryboard == null)
        {
            ScanProgressBar.Opacity = 0.0;
            ScanProgressBar.Value = 0.0;
        }

        // 3. Selection
        if (_viewModel.SelectedNode != null)
        {
            SelectedNameText.Text = _viewModel.SelectedNode.Name;
            SelectedPathText.Text = _viewModel.SelectedPath;
            SelectedSizeText.Text = DisktreePalette.FormatBytes(_viewModel.SelectedNode.Bytes);
            SelectedFilesText.Text = $"{_viewModel.SelectedNode.Files:N0} ({_viewModel.SelectedNode.Dirs:N0} dirs)";
            SelectedCategoryText.Text = _viewModel.SelectedNode.Category.Label();
            SelectedReclaimText.Text = _viewModel.SelectedNode.Reclaim.HasValue
                ? _viewModel.SelectedNode.Reclaim.Value.Label()
                : "No";

            ToggleMarkButton.IsEnabled = true;
            DrillInButton.IsEnabled = true;
            OpenExplorerButton.IsEnabled = !string.IsNullOrEmpty(_viewModel.SelectedPath);

            if (_viewModel.IsSelectedMarked)
            {
                MarkButtonText.Text = "Unmark";
                MarkButtonIcon.Glyph = "\uE711";
            }
            else
            {
                MarkButtonText.Text = "Mark for Removal";
                MarkButtonIcon.Glyph = "\uE74D";
            }
        }
        else
        {
            SelectedNameText.Text = "No selection";
            SelectedPathText.Text = string.Empty;
            SelectedSizeText.Text = "-";
            SelectedFilesText.Text = "-";
            SelectedCategoryText.Text = "-";
            SelectedReclaimText.Text = "No";

            ToggleMarkButton.IsEnabled = false;
            DrillInButton.IsEnabled = false;
            OpenExplorerButton.IsEnabled = false;
        }

        // 4. "Worth a Look" items
        var items = _viewModel.WorthALookItems
            .Select(c => new CandidateDisplay(c, DisktreePalette.FormatBytes(c.Bytes)))
            .ToList();
        WorthALookList.ItemsSource = items;

        // 5. Volume Space
        if (_viewModel.Space.HasValue)
        {
            var sp = _viewModel.Space.Value;
            VolumeProgressBar.Value = sp.UsedFraction * 100.0;
            FreeSpaceNowText.Text = $"{DisktreePalette.FormatBytes(sp.Available)} free of {DisktreePalette.FormatBytes(sp.Total)}";

            if (_viewModel.ProjectedSpace.HasValue)
            {
                var proj = _viewModel.ProjectedSpace.Value;
                FreeSpaceProjectedText.Text = $"{DisktreePalette.FormatBytes(proj.Available)} (+{DisktreePalette.FormatBytes(_viewModel.MarkedBytes)})";
            }
        }

        // 6. Review & Removal button
        ReviewRemovalButton.IsEnabled = _viewModel.HasMarks;
        ReviewButtonText.Text = $"Review Marks ({_viewModel.MarkedCount} - {DisktreePalette.FormatBytes(_viewModel.MarkedBytes)})";
    }

    private async void OnBrowseFolderClicked(object sender, RoutedEventArgs e)
    {
        var picker = new FolderPicker();
        var mainWindow = App.MainWindowInstance;
        if (mainWindow != null)
        {
            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(mainWindow);
            WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);
        }

        picker.FileTypeFilter.Add("*");
        var folder = await picker.PickSingleFolderAsync();
        if (folder != null)
        {
            DriveDropdown.Content = folder.Name;
            await _viewModel.StartScanAsync(folder.Path);
        }
    }

    private async void OnRescanClicked(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrEmpty(_viewModel.ScannedRoot))
        {
            await _viewModel.StartScanAsync(_viewModel.ScannedRoot);
        }
    }

    private void OnBreadcrumbItemClicked(BreadcrumbBar sender, BreadcrumbBarItemClickedEventArgs args)
    {
        _viewModel.NavigateToDepth(args.Index);
    }

    private void OnMetricChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is RadioButtons rb && rb.SelectedItem is RadioButton radio)
        {
            _viewModel.Metric = radio.Tag?.ToString() == "Files" ? Metric.Files : Metric.Bytes;
        }
    }

    private void OnSearchTextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
    {
        _viewModel.SearchQuery = sender.Text;
    }

    private void OnToggleMarkClicked(object sender, RoutedEventArgs e)
    {
        _viewModel.ToggleMarkForSelection();
    }

    private void OnDrillInClicked(object sender, RoutedEventArgs e)
    {
        _viewModel.DrillIntoCurrentSelection();
    }

    private void OnOpenInExplorerClicked(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrEmpty(_viewModel.SelectedPath) && Path.Exists(_viewModel.SelectedPath))
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments = $"/select,\"{_viewModel.SelectedPath}\"",
                    UseShellExecute = true
                });
            }
            catch
            {
                // Fallback to opening folder directly
                Process.Start(new ProcessStartInfo
                {
                    FileName = _viewModel.SelectedPath,
                    UseShellExecute = true
                });
            }
        }
    }

    private void OnWorthALookSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (WorthALookList.SelectedItem is CandidateDisplay disp && _viewModel.RootNode != null)
        {
            var node = _viewModel.RootNode.Resolve(disp.Candidate.Crumbs);
            if (node != null)
            {
                _viewModel.SelectNode(disp.Candidate.Crumbs, node);
            }
        }
    }

    private void OnMarkCandidateClicked(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is CandidateDisplay disp)
        {
            _viewModel.MarkCandidate(disp.Candidate);
        }
    }

    private async void OnReviewRemovalClicked(object sender, RoutedEventArgs e)
    {
        var plan = _viewModel.BuildRemovalPlan();

        RemovalSummaryText.Text = $"Marked {plan.Targets.Count} outer items ({DisktreePalette.FormatBytes(plan.TotalBytes)} to reclaim).";
        RemovalTargetsList.ItemsSource = plan.Targets;

        RemovalCoveredText.Text = plan.Covered.Count > 0
            ? $"{plan.Covered.Count} sub-items covered by parent removals."
            : string.Empty;

        RemovalBlockedText.Text = plan.Blocked.Count > 0
            ? $"{plan.Blocked.Count} items blocked by safety guardrails (e.g. system files or drive root)."
            : string.Empty;

        RemovalDialog.XamlRoot = XamlRoot;
        var result = await RemovalDialog.ShowAsync();

        if (result == ContentDialogResult.Primary)
        {
            var mode = RemovalModeRadios.SelectedIndex == 1 ? RemovalMode.Permanent : RemovalMode.RecycleBin;

            var progress = new Progress<(string Path, bool Success, string? Error)>(report =>
            {
                StatusMessageText.Text = report.Success
                    ? $"Removed: {report.Path}"
                    : $"Failed to remove: {report.Path} ({report.Error})";
            });

            await RemovalEngine.ExecuteAsync(plan, mode, progress);

            _viewModel.ClearMarks();
            _viewModel.RefreshSpace();

            // Rescan to reflect changes
            await _viewModel.StartScanAsync(_viewModel.ScannedRoot);
        }
    }
}
