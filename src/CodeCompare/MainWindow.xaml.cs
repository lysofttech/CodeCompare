using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using CodeCompare.Core.Comparison;
using CodeCompare.Core.Reports;
using CodeCompare.Infrastructure;
using CodeCompare.ViewModels;
using Microsoft.Win32;

namespace CodeCompare;

public partial class MainWindow : Window
{
    private readonly AppSettings _settings;
    private readonly MainViewModel _vm;

    public MainWindow()
    {
        InitializeComponent();

        _settings = AppSettings.Load();
        _vm = new MainViewModel(_settings);
        DataContext = _vm;

        Width = _settings.WindowWidth;
        Height = _settings.WindowHeight;
        if (_settings.WindowMaximized) WindowState = WindowState.Maximized;
        OptionsColumn.Width = new GridLength(Math.Clamp(_settings.OptionsPanelWidth, 230, 600));

        Viewer.Options = _vm.TextOptions;
        Viewer.WordWrap = _settings.WordWrap;
        Viewer.ChangesOnly = _settings.ChangesOnly;
        Viewer.CodeFontSize = _settings.DiffFontSize;
    }

    /// <summary>Starts a comparison of two folders passed on the command line.</summary>
    public void StartComparison(string left, string right)
    {
        _vm.LeftPath = left;
        _vm.RightPath = right;
        Dispatcher.BeginInvoke(async () => await RunCompareAsync());
    }

    private async Task RunCompareAsync()
    {
        try
        {
            await _vm.CompareAsync();
        }
        catch (InvalidOperationException ex)
        {
            MessageBox.Show(this, ex.Message, "Code Compare", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            MessageBox.Show(this, ex.Message, "Code Compare", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        EmptyHint.Visibility = _vm.HasResults ? Visibility.Collapsed : Visibility.Visible;
        Viewer.Options = _vm.TextOptions;

        // Jump straight to the first file that has differences.
        var first = FilesGrid.Items.OfType<FileComparison>().FirstOrDefault(f => f.Status != FileStatus.Identical);
        if (first is not null)
        {
            FilesGrid.SelectedItem = first;
            FilesGrid.ScrollIntoView(first);
        }
        else
        {
            await Viewer.ShowComparisonAsync(null);
        }
    }

    private FileComparison? SelectedFile => FilesGrid.SelectedItem as FileComparison;

    private List<FileComparison> SelectedFiles => FilesGrid.SelectedItems.OfType<FileComparison>().ToList();

    // ---------- Folder selection ----------

    private void BrowseLeft_Click(object sender, RoutedEventArgs e)
    {
        if (PickFolder("Choose the left (original) folder", _vm.LeftPath) is { } path) _vm.LeftPath = path;
    }

    private void BrowseRight_Click(object sender, RoutedEventArgs e)
    {
        if (PickFolder("Choose the right (modified) folder", _vm.RightPath) is { } path) _vm.RightPath = path;
    }

    private string? PickFolder(string title, string current)
    {
        var dialog = new OpenFolderDialog { Title = title };
        if (Directory.Exists(current)) dialog.InitialDirectory = current;
        return dialog.ShowDialog(this) == true ? dialog.FolderName : null;
    }

    private void Path_PreviewDragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Link : DragDropEffects.None;
        e.Handled = true;
    }

    private void LeftPath_PreviewDrop(object sender, DragEventArgs e)
    {
        if (DroppedFolders(e) is { Length: > 0 } folders)
        {
            _vm.LeftPath = folders[0];
            if (folders.Length > 1) _vm.RightPath = folders[1];
        }
        e.Handled = true;
    }

    private void RightPath_PreviewDrop(object sender, DragEventArgs e)
    {
        if (DroppedFolders(e) is { Length: > 0 } folders) _vm.RightPath = folders[0];
        e.Handled = true;
    }

    private static string[]? DroppedFolders(DragEventArgs e) =>
        (e.Data.GetData(DataFormats.FileDrop) as string[])?
            .Select(p => Directory.Exists(p) ? p : Path.GetDirectoryName(p) ?? p)
            .ToArray();

    private async void Swap_Click(object sender, RoutedEventArgs e)
    {
        _vm.Swap();
        if (_vm.HasResults) await RunCompareAsync();
    }

    private async void Compare_Click(object sender, RoutedEventArgs e) => await RunCompareAsync();

    private void Cancel_Click(object sender, RoutedEventArgs e) => _vm.Cancel();

    // ---------- File types and options ----------

    private void SelectAll_Click(object sender, RoutedEventArgs e) => _vm.SetAllExtensions(true);

    private void SelectNone_Click(object sender, RoutedEventArgs e) => _vm.SetAllExtensions(false);

    private void SelectDefaults_Click(object sender, RoutedEventArgs e) => _vm.ResetExtensions();

    private void ToggleCategory_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is string category) _vm.ToggleCategory(category);
    }

    private void AddExtension_Click(object sender, RoutedEventArgs e) => _vm.AddExtensions();

    private void NewExtension_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        _vm.AddExtensions();
        e.Handled = true;
    }

    private void RestoreExclusions_Click(object sender, RoutedEventArgs e)
    {
        _vm.ExcludedFolders = AppSettings.DefaultExcludedFolders;
        _vm.ExcludedFiles = AppSettings.DefaultExcludedFiles;
    }

    // ---------- File list ----------

    private async void FilesGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (FilesGrid.SelectedItems.Count > 1) return; // keep showing the current diff while multi-selecting
        await Viewer.ShowComparisonAsync(SelectedFile);
    }

    private void FilesGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        // Ignore double-clicks on column headers (used for resizing).
        if (e.OriginalSource is DependencyObject source && FindParent<DataGridRow>(source) is null) return;
        OpenDiffWindow(SelectedFile);
    }

    private void OpenDiffWindow(FileComparison? item)
    {
        if (item is null) return;
        new DiffWindow(item, Viewer.Options, Viewer.WordWrap, Viewer.ChangesOnly, Viewer.CodeFontSize) { Owner = this }.Show();
    }

    private void Viewer_PopOutRequested(object? sender, EventArgs e) => OpenDiffWindow(SelectedFile);

    private void OpenDiffWindow_Click(object sender, RoutedEventArgs e) => OpenDiffWindow(SelectedFile);

    private void OpenLeftFile_Click(object sender, RoutedEventArgs e) => Shell.Open(SelectedFile?.LeftPath);

    private void OpenRightFile_Click(object sender, RoutedEventArgs e) => Shell.Open(SelectedFile?.RightPath);

    private void ExploreLeftFile_Click(object sender, RoutedEventArgs e) => Shell.ShowInExplorer(SelectedFile?.LeftPath);

    private void ExploreRightFile_Click(object sender, RoutedEventArgs e) => Shell.ShowInExplorer(SelectedFile?.RightPath);

    private void CopyRelativePath_Click(object sender, RoutedEventArgs e)
    {
        var paths = SelectedFiles.Select(f => f.RelativePath).ToList();
        if (paths.Count > 0) Clipboard.SetText(string.Join(Environment.NewLine, paths));
    }

    private void CopyFullPaths_Click(object sender, RoutedEventArgs e)
    {
        var paths = SelectedFiles.SelectMany(f => new[] { f.LeftPath, f.RightPath }).OfType<string>().ToList();
        if (paths.Count > 0) Clipboard.SetText(string.Join(Environment.NewLine, paths));
    }

    private void CopyLeftToRight_Click(object sender, RoutedEventArgs e) => CopyFiles(leftToRight: true);

    private void CopyRightToLeft_Click(object sender, RoutedEventArgs e) => CopyFiles(leftToRight: false);

    private async void CopyFiles(bool leftToRight)
    {
        var items = SelectedFiles
            .Where(f => (leftToRight ? f.LeftPath : f.RightPath) is not null && f.Status != FileStatus.Identical)
            .ToList();
        if (items.Count == 0)
        {
            MessageBox.Show(this, "None of the selected files can be copied in that direction (they are identical or missing on the source side).",
                "Copy files", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        string direction = leftToRight ? "left to right" : "right to left";
        string target = leftToRight ? "right" : "left";
        string preview = string.Join("\n", items.Take(10).Select(f => "  " + f.RelativePath)) + (items.Count > 10 ? $"\n  … and {items.Count - 10} more" : "");
        var answer = MessageBox.Show(this,
            $"Copy {items.Count} file(s) from {direction}?\n\n{preview}\n\nFiles on the {target} side will be overwritten. This cannot be undone.",
            "Copy files", MessageBoxButton.OKCancel, MessageBoxImage.Warning, MessageBoxResult.Cancel);
        if (answer != MessageBoxResult.OK) return;

        try
        {
            var updated = _vm.CopyFiles(items, leftToRight);
            _vm.StatusText = $"Copied {updated.Count} file(s) from {direction}.";
            if (updated.FirstOrDefault() is { } first) FilesGrid.SelectedItem = first;
            await Viewer.ShowComparisonAsync(SelectedFile);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            MessageBox.Show(this, ex.Message, "Copy failed", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void Recompare_Click(object sender, RoutedEventArgs e)
    {
        FileComparison? last = null;
        foreach (var item in SelectedFiles) last = _vm.Refresh(item) ?? last;
        if (last is not null) FilesGrid.SelectedItem = last;
        await Viewer.ShowComparisonAsync(SelectedFile);
    }

    private void SelectAdjacentChangedFile(int direction)
    {
        var items = FilesGrid.Items.OfType<FileComparison>().ToList();
        if (items.Count == 0) return;

        int index = SelectedFile is null ? (direction > 0 ? -1 : items.Count) : items.IndexOf(SelectedFile);
        for (int i = index + direction; i >= 0 && i < items.Count; i += direction)
        {
            if (items[i].Status == FileStatus.Identical) continue;
            FilesGrid.SelectedItem = items[i];
            FilesGrid.ScrollIntoView(items[i]);
            return;
        }
    }

    // ---------- Export ----------

    private void Export_Click(object sender, RoutedEventArgs e)
    {
        var menu = ExportButton.ContextMenu!;
        menu.PlacementTarget = ExportButton;
        menu.Placement = PlacementMode.Bottom;
        menu.IsOpen = true;
    }

    private async void ExportHtml_Click(object sender, RoutedEventArgs e)
    {
        if (_vm.LastResult is not { } result) return;
        var path = AskSavePath("HTML report|*.html", "CodeCompare-report.html");
        if (path is null) return;

        var include = MessageBox.Show(this, "Include side-by-side diffs of every changed file in the report?",
            "HTML report", MessageBoxButton.YesNoCancel, MessageBoxImage.Question, MessageBoxResult.Yes);
        if (include == MessageBoxResult.Cancel) return;

        var files = VisibleFiles();
        await ExportAsync(path, () => ReportExporter.ExportHtml(result, files, path, include == MessageBoxResult.Yes), openAfter: true);
    }

    private async void ExportCsv_Click(object sender, RoutedEventArgs e)
    {
        var path = AskSavePath("CSV file|*.csv", "CodeCompare-files.csv");
        if (path is null) return;
        var files = VisibleFiles();
        await ExportAsync(path, () => ReportExporter.ExportCsv(files, path), openAfter: false);
    }

    private async void ExportPatch_Click(object sender, RoutedEventArgs e)
    {
        var path = AskSavePath("Patch file|*.patch;*.diff", "CodeCompare.patch");
        if (path is null) return;
        var files = VisibleFiles();
        await ExportAsync(path, () => ReportExporter.ExportPatch(files, path), openAfter: false);
    }

    /// <summary>Exports cover the files currently shown, so filters can be used to narrow a report.</summary>
    private List<FileComparison> VisibleFiles() => FilesGrid.Items.OfType<FileComparison>().ToList();

    private string? AskSavePath(string filter, string fileName)
    {
        var dialog = new SaveFileDialog { Filter = filter, FileName = fileName, AddExtension = true };
        return dialog.ShowDialog(this) == true ? dialog.FileName : null;
    }

    private async Task ExportAsync(string path, Action export, bool openAfter)
    {
        _vm.StatusText = "Exporting…";
        try
        {
            await Task.Run(export);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _vm.StatusText = "Export failed.";
            MessageBox.Show(this, ex.Message, "Export failed", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        _vm.StatusText = $"Exported to {path}";
        if (openAfter &&
            MessageBox.Show(this, $"Saved to:\n{path}\n\nOpen it now?", "Export complete", MessageBoxButton.YesNo, MessageBoxImage.Information) == MessageBoxResult.Yes)
        {
            Shell.Open(path);
        }
    }

    // ---------- Keyboard and lifetime ----------

    private async void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        bool shift = Keyboard.Modifiers == ModifierKeys.Shift;
        switch (e.Key)
        {
            case Key.F5:
                e.Handled = true;
                await RunCompareAsync();
                break;
            case Key.Escape when _vm.IsBusy:
                _vm.Cancel();
                e.Handled = true;
                break;
            case Key.F7:
                if (shift) Viewer.PreviousChange(); else Viewer.NextChange();
                e.Handled = true;
                break;
            case Key.F8:
                SelectAdjacentChangedFile(shift ? -1 : 1);
                e.Handled = true;
                break;
            case Key.F when Keyboard.Modifiers == ModifierKeys.Control:
                SearchBox.Focus();
                SearchBox.SelectAll();
                e.Handled = true;
                break;
        }
    }

    private void Window_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        _vm.Cancel();
        _settings.WindowMaximized = WindowState == WindowState.Maximized;
        if (WindowState == WindowState.Normal)
        {
            _settings.WindowWidth = Width;
            _settings.WindowHeight = Height;
        }
        _settings.OptionsPanelWidth = OptionsColumn.ActualWidth;
        _settings.WordWrap = Viewer.WordWrap;
        _settings.ChangesOnly = Viewer.ChangesOnly;
        _settings.DiffFontSize = Viewer.CodeFontSize;
        _vm.SaveSettings();
    }

    private static T? FindParent<T>(DependencyObject child) where T : DependencyObject
    {
        for (var current = child; current is not null;)
        {
            if (current is T match) return match;
            // Text runs are not visuals, so walk the logical tree for them.
            current = current is System.Windows.Media.Visual
                ? System.Windows.Media.VisualTreeHelper.GetParent(current)
                : LogicalTreeHelper.GetParent(current);
        }
        return null;
    }
}
