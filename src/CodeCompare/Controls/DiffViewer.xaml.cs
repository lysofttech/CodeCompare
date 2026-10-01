using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using CodeCompare.Core.Code;
using CodeCompare.Core.Comparison;
using CodeCompare.Core.Diff;
using CodeCompare.Core.IO;
using CodeCompare.Infrastructure;

namespace CodeCompare.Controls;

public partial class DiffViewer : UserControl
{
    private const long MaxDisplayBytes = 64L * 1024 * 1024;
    private const int ContextLines = 3;

    public static readonly DependencyProperty WrapModeProperty = DependencyProperty.Register(
        nameof(WrapMode), typeof(TextWrapping), typeof(DiffViewer), new PropertyMetadata(TextWrapping.Wrap));

    public static readonly DependencyProperty CodeFontSizeProperty = DependencyProperty.Register(
        nameof(CodeFontSize), typeof(double), typeof(DiffViewer), new PropertyMetadata(12.0, (d, _) => ((DiffViewer)d).UpdateLineNumberWidth()));

    public static readonly DependencyProperty LineNumberWidthProperty = DependencyProperty.Register(
        nameof(LineNumberWidth), typeof(GridLength), typeof(DiffViewer), new PropertyMetadata(new GridLength(48)));

    private string? _leftPath;
    private string? _rightPath;
    private DiffResult? _result;
    private List<DiffRow> _allRows = [];
    private List<DiffRow> _displayRows = [];
    private List<int> _changeStarts = [];
    private int _maxLineNumber;
    private int _loadVersion;
    private bool _suppressOptionEvents;
    private ScrollViewer? _scrollViewer;

    public DiffViewer()
    {
        InitializeComponent();
        Loaded += (_, _) => AttachScrollViewer();
        ShowMessage("Select a file to see its differences.");
        UpdateHeaders(null, null, null, null);
    }

    /// <summary>Raised when the user asks to open the current diff in a separate window.</summary>
    public event EventHandler? PopOutRequested;

    public TextWrapping WrapMode
    {
        get => (TextWrapping)GetValue(WrapModeProperty);
        set => SetValue(WrapModeProperty, value);
    }

    public double CodeFontSize
    {
        get => (double)GetValue(CodeFontSizeProperty);
        set => SetValue(CodeFontSizeProperty, value);
    }

    public GridLength LineNumberWidth
    {
        get => (GridLength)GetValue(LineNumberWidthProperty);
        set => SetValue(LineNumberWidthProperty, value);
    }

    public bool ShowPopOutButton
    {
        get => PopOutButton.Visibility == Visibility.Visible;
        set => PopOutButton.Visibility = value ? Visibility.Visible : Visibility.Collapsed;
    }

    public bool WordWrap
    {
        get => WrapToggle.IsChecked == true;
        set
        {
            WrapToggle.IsChecked = value;
            WrapMode = value ? TextWrapping.Wrap : TextWrapping.NoWrap;
        }
    }

    public bool ChangesOnly
    {
        get => ChangesOnlyToggle.IsChecked == true;
        set
        {
            ChangesOnlyToggle.IsChecked = value;
            RebuildDisplay(keepPosition: false);
        }
    }

    public TextCompareOptions Options
    {
        get => new((WhitespaceMode)Math.Max(0, WhitespaceBox.SelectedIndex), IgnoreCaseBox.IsChecked == true, IgnoreBlankBox.IsChecked == true);
        set
        {
            _suppressOptionEvents = true;
            WhitespaceBox.SelectedIndex = (int)value.Whitespace;
            IgnoreCaseBox.IsChecked = value.IgnoreCase;
            IgnoreBlankBox.IsChecked = value.IgnoreBlankLines;
            _suppressOptionEvents = false;
        }
    }

    public string? LeftPath => _leftPath;
    public string? RightPath => _rightPath;

    public Task ShowComparisonAsync(FileComparison? item) => ShowFilesAsync(item?.LeftPath, item?.RightPath);

    public Task ShowFilesAsync(string? leftPath, string? rightPath)
    {
        _leftPath = leftPath;
        _rightPath = rightPath;
        return ReloadAsync(keepPosition: false);
    }

    public Task ReloadAsync() => ReloadAsync(keepPosition: true);

    // ---------- Navigation ----------

    public void NextChange()
    {
        if (_changeStarts.Count == 0) return;
        int anchor = CurrentAnchor();
        int next = _changeStarts.FindIndex(s => s > anchor);
        NavigateToChange(next >= 0 ? next : _changeStarts.Count - 1);
    }

    public void PreviousChange()
    {
        if (_changeStarts.Count == 0) return;
        int anchor = CurrentAnchor();
        int previous = _changeStarts.FindLastIndex(s => s < anchor);
        NavigateToChange(previous >= 0 ? previous : 0);
    }

    private int CurrentAnchor()
    {
        if (RowsList.SelectedIndex >= 0) return RowsList.SelectedIndex;
        return _scrollViewer is null ? -1 : (int)_scrollViewer.VerticalOffset - 1;
    }

    private void NavigateToChange(int changeIndex)
    {
        if (changeIndex < 0 || changeIndex >= _changeStarts.Count) return;
        int row = _changeStarts[changeIndex];
        RowsList.SelectedIndex = row;
        ScrollToRow(Math.Max(0, row - ContextLines));
        RowsList.Focus();
    }

    private void ScrollToRow(int row)
    {
        AttachScrollViewer();
        _scrollViewer?.ScrollToVerticalOffset(row);
    }

    // ---------- Loading ----------

    private sealed record LoadedDiff(TextFile? Left, TextFile? Right, DiffResult? Result, List<DiffRow> Rows,
                                     LocStats? LeftLoc, LocStats? RightLoc, string? Message);

    private async Task ReloadAsync(bool keepPosition)
    {
        int version = ++_loadVersion;
        string? left = _leftPath, right = _rightPath;
        var options = Options;

        if (left is null && right is null)
        {
            ClearRows();
            UpdateHeaders(null, null, null, null);
            ShowMessage("Select a file to see its differences.");
            return;
        }

        LoadedDiff loaded;
        try
        {
            loaded = await Task.Run(() => Load(left, right, options));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            if (version != _loadVersion) return;
            ClearRows();
            UpdateHeaders(null, null, null, null);
            ShowMessage("Could not read the file:\n" + ex.Message);
            return;
        }

        if (version != _loadVersion) return; // a newer request superseded this one

        UpdateHeaders(loaded.Left, loaded.Right, loaded.LeftLoc, loaded.RightLoc);
        if (loaded.Message is not null)
        {
            ClearRows();
            ShowMessage(loaded.Message);
            return;
        }

        _result = loaded.Result;
        _allRows = loaded.Rows;
        _maxLineNumber = Math.Max(loaded.Left?.Lines.Length ?? 0, loaded.Right?.Lines.Length ?? 0);
        UpdateStats();
        MessageText.Visibility = Visibility.Collapsed;
        RowsList.Visibility = Visibility.Visible;
        RebuildDisplay(keepPosition);
    }

    private static LoadedDiff Load(string? leftPath, string? rightPath, TextCompareOptions options)
    {
        foreach (var path in new[] { leftPath, rightPath })
        {
            if (path is null) continue;
            long size = new FileInfo(path).Length;
            if (size > MaxDisplayBytes)
                return new LoadedDiff(null, null, null, [], null, null, $"This file is too large to display ({size / (1024 * 1024):N0} MB).");
        }

        var left = leftPath is null ? null : TextFile.Load(leftPath);
        var right = rightPath is null ? null : TextFile.Load(rightPath);

        if (left?.IsBinary == true || right?.IsBinary == true)
        {
            string message = left is null || right is null
                ? "Binary file – no text to show."
                : File.ReadAllBytes(leftPath!).AsSpan().SequenceEqual(File.ReadAllBytes(rightPath!))
                    ? "Binary files are identical."
                    : "Binary files differ.";
            return new LoadedDiff(left, right, null, [], null, null, message);
        }

        string[] leftLines = left?.Lines ?? [];
        string[] rightLines = right?.Lines ?? [];
        var result = DiffEngine.Compare(leftLines, rightLines, options);
        var rows = DiffEngine.BuildRows(leftLines, rightLines, result, options);
        var language = Languages.Get(leftPath ?? rightPath!);

        return new LoadedDiff(left, right, result, rows,
            left is null ? null : LineCounter.Count(leftLines, language),
            right is null ? null : LineCounter.Count(rightLines, language),
            null);
    }

    private void RebuildDisplay(bool keepPosition)
    {
        if (_result is null) return;

        double offset = keepPosition ? _scrollViewer?.VerticalOffset ?? 0 : 0;
        _displayRows = ChangesOnly && _result.HasDifferences ? DiffEngine.CollapseUnchanged(_allRows, ContextLines) : _allRows;
        SetRows(_displayRows);

        if (keepPosition)
            Dispatcher.BeginInvoke(() => ScrollToRow((int)offset), DispatcherPriority.Loaded);
        else if (_changeStarts.Count > 0)
            Dispatcher.BeginInvoke(() => NavigateToChange(0), DispatcherPriority.Loaded);
        else
            Dispatcher.BeginInvoke(() => ScrollToRow(0), DispatcherPriority.Loaded);
    }

    private void SetRows(List<DiffRow> rows)
    {
        _displayRows = rows;
        _changeStarts = [];
        for (int i = 0; i < rows.Count; i++)
        {
            if (!rows[i].IsChange) continue;
            if (i == 0 || !rows[i - 1].IsChange || rows[i - 1].BlockIndex != rows[i].BlockIndex)
                _changeStarts.Add(i);
        }

        RowsList.ItemsSource = null;
        RowsList.ItemsSource = rows;
        Map.SetRows(rows);
        UpdateLineNumberWidth();
        UpdateCounter();
    }

    private void ClearRows()
    {
        _result = null;
        _allRows = [];
        _changeStarts = [];
        RowsList.ItemsSource = null;
        Map.SetRows(null);
        StatsText.Inlines.Clear();
        ChangeCounter.Text = "";
    }

    private void ShowMessage(string message)
    {
        MessageText.Text = message;
        MessageText.Visibility = Visibility.Visible;
        RowsList.Visibility = Visibility.Hidden;
    }

    // ---------- Header and status ----------

    private void UpdateHeaders(TextFile? left, TextFile? right, LocStats? leftLoc, LocStats? rightLoc)
    {
        LeftTitle.Text = _leftPath ?? (_rightPath is null ? "Left" : "(not present on the left)");
        RightTitle.Text = _rightPath ?? (_leftPath is null ? "Right" : "(not present on the right)");
        LeftTitle.ToolTip = _leftPath;
        RightTitle.ToolTip = _rightPath;
        LeftInfo.Text = Describe(left, leftLoc);
        RightInfo.Text = Describe(right, rightLoc);

        OpenLeftButton.IsEnabled = ExploreLeftButton.IsEnabled = _leftPath is not null;
        OpenRightButton.IsEnabled = ExploreRightButton.IsEnabled = _rightPath is not null;
    }

    private static string Describe(TextFile? file, LocStats? loc)
    {
        if (file is null) return " ";
        if (file.IsBinary) return "Binary";

        var parts = new List<string> { file.EncodingName };
        if (file.LineEnding.Length > 0) parts.Add(file.LineEnding);
        if (loc is { } s)
            parts.Add($"{s.Total:N0} lines  ({s.Code:N0} code · {s.Comment:N0} comment · {s.Blank:N0} blank)");
        return string.Join("  ·  ", parts);
    }

    private void UpdateStats()
    {
        StatsText.Inlines.Clear();
        if (_result is null) return;

        if (!_result.HasDifferences)
        {
            StatsText.Inlines.Add(new Run(_result.Blocks.Count == 0 ? "No differences" : "No differences (ignored changes only)")
            {
                Foreground = (Brush)FindResource("MutedTextBrush"),
            });
            return;
        }

        StatsText.Inlines.Add(new Run($"+{_result.Added:N0}") { Foreground = (Brush)FindResource("AddedTextBrush"), FontWeight = FontWeights.SemiBold });
        StatsText.Inlines.Add(new Run("  "));
        StatsText.Inlines.Add(new Run($"−{_result.Deleted:N0}") { Foreground = (Brush)FindResource("DeletedTextBrush"), FontWeight = FontWeights.SemiBold });
        StatsText.Inlines.Add(new Run("  "));
        StatsText.Inlines.Add(new Run($"~{_result.Modified:N0}") { Foreground = (Brush)FindResource("ModifiedTextBrush"), FontWeight = FontWeights.SemiBold });
        StatsText.Inlines.Add(new Run("  lines"));
        StatsText.ToolTip = $"{_result.Added:N0} added, {_result.Deleted:N0} deleted, {_result.Modified:N0} modified lines";
    }

    private void UpdateCounter()
    {
        if (_changeStarts.Count == 0)
        {
            ChangeCounter.Text = _result is null ? "" : "No changes";
            return;
        }

        int selected = RowsList.SelectedIndex;
        int current = selected < 0 ? -1 : _changeStarts.FindLastIndex(s => s <= selected);
        string total = $"{_changeStarts.Count:N0} change{(_changeStarts.Count == 1 ? "" : "s")}";
        ChangeCounter.Text = current < 0 ? total : $"{current + 1:N0} of {total}";
    }

    private void UpdateLineNumberWidth()
    {
        int digits = Math.Max(3, _maxLineNumber.ToString().Length);
        LineNumberWidth = new GridLength(Math.Ceiling(digits * CodeFontSize * 0.62 + 16));
    }

    private void AttachScrollViewer()
    {
        if (_scrollViewer is not null) return;
        _scrollViewer = FindChild<ScrollViewer>(RowsList);
        if (_scrollViewer is not null)
            _scrollViewer.ScrollChanged += (_, _) => Map.SetViewport(_scrollViewer.VerticalOffset, _scrollViewer.ViewportHeight);
    }

    private static T? FindChild<T>(DependencyObject parent) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T match) return match;
            if (FindChild<T>(child) is { } nested) return nested;
        }
        return null;
    }

    // ---------- Event handlers ----------

    private void Next_Click(object sender, RoutedEventArgs e) => NextChange();

    private void Previous_Click(object sender, RoutedEventArgs e) => PreviousChange();

    private void ChangesOnly_Click(object sender, RoutedEventArgs e) => RebuildDisplay(keepPosition: false);

    private void Wrap_Click(object sender, RoutedEventArgs e) => WrapMode = WordWrap ? TextWrapping.Wrap : TextWrapping.NoWrap;

    private async void Option_Changed(object sender, RoutedEventArgs e)
    {
        if (_suppressOptionEvents || !IsLoaded) return;
        await ReloadAsync(keepPosition: true);
    }

    private void FontSmaller_Click(object sender, RoutedEventArgs e) => CodeFontSize = Math.Max(8, CodeFontSize - 1);

    private void FontLarger_Click(object sender, RoutedEventArgs e) => CodeFontSize = Math.Min(32, CodeFontSize + 1);

    private void PopOut_Click(object sender, RoutedEventArgs e) => PopOutRequested?.Invoke(this, EventArgs.Empty);

    private void OpenLeft_Click(object sender, RoutedEventArgs e) => Shell.Open(_leftPath);

    private void OpenRight_Click(object sender, RoutedEventArgs e) => Shell.Open(_rightPath);

    private void ExploreLeft_Click(object sender, RoutedEventArgs e) => Shell.ShowInExplorer(_leftPath);

    private void ExploreRight_Click(object sender, RoutedEventArgs e) => Shell.ShowInExplorer(_rightPath);

    private void Map_NavigateRequested(object? sender, int row)
    {
        double viewport = _scrollViewer?.ViewportHeight ?? 0;
        ScrollToRow(Math.Max(0, row - (int)(viewport / 2)));
    }

    private void RowsList_SelectionChanged(object sender, SelectionChangedEventArgs e) => UpdateCounter();

    private void RowsList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (RowsList.SelectedItem is DiffRow { Kind: DiffRowKind.Collapsed } row)
            Expand(row);
    }

    private void RowsList_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (Keyboard.Modifiers != ModifierKeys.Control) return;
        CodeFontSize = Math.Clamp(CodeFontSize + (e.Delta > 0 ? 1 : -1), 8, 32);
        e.Handled = true;
    }

    private void Expand(DiffRow collapsed)
    {
        int index = _displayRows.IndexOf(collapsed);
        if (index < 0 || collapsed.HiddenRows is null) return;

        double offset = _scrollViewer?.VerticalOffset ?? 0;
        var rows = new List<DiffRow>(_displayRows.Count + collapsed.HiddenRows.Count);
        rows.AddRange(_displayRows.Take(index));
        rows.AddRange(collapsed.HiddenRows);
        rows.AddRange(_displayRows.Skip(index + 1));
        SetRows(rows);
        Dispatcher.BeginInvoke(() => ScrollToRow((int)offset), DispatcherPriority.Loaded);
    }

    private void ExpandAll_Click(object sender, RoutedEventArgs e)
    {
        if (_result is null) return;
        double offset = _scrollViewer?.VerticalOffset ?? 0;
        SetRows(_allRows);
        Dispatcher.BeginInvoke(() => ScrollToRow((int)offset), DispatcherPriority.Loaded);
    }

    private void CopyLeft_Click(object sender, RoutedEventArgs e) => CopySelected(left: true);

    private void CopyRight_Click(object sender, RoutedEventArgs e) => CopySelected(left: false);

    private void CopySelected(bool left)
    {
        var selected = RowsList.SelectedItems.Cast<DiffRow>()
            .OrderBy(r => _displayRows.IndexOf(r))
            .Where(r => (left ? r.LeftNumber : r.RightNumber) is not null)
            .Select(r => left ? r.LeftText : r.RightText);
        var text = string.Join(Environment.NewLine, selected);
        if (text.Length > 0) Clipboard.SetText(text);
    }
}
