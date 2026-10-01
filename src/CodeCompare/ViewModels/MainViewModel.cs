using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Windows.Data;
using CodeCompare.Core.Code;
using CodeCompare.Core.Comparison;
using CodeCompare.Core.Diff;
using CodeCompare.Infrastructure;

namespace CodeCompare.ViewModels;

public sealed record LocRow(string Label, int Left, int Right)
{
    public int Delta => Right - Left;
}

public sealed class MainViewModel : ObservableObject
{
    private const string CustomCategory = "Custom";
    private const int MaxRecent = 12;

    private readonly AppSettings _settings;
    private CancellationTokenSource? _cts;

    private string _leftPath;
    private string _rightPath;
    private string _newExtension = "";
    private bool _includeAllFiles;
    private bool _recursive;
    private int _whitespaceIndex;
    private bool _ignoreCase;
    private bool _ignoreBlankLines;
    private string _excludedFolders;
    private string _excludedFiles;

    private bool _isBusy;
    private bool _isIndeterminate;
    private double _progress;
    private string _statusText = "Choose two folders and press Compare (F5).";
    private CompareResult? _lastResult;
    private ObservableCollection<FileComparison> _files = [];
    private ICollectionView? _filesView;
    private CompareSummary _summary = CompareSummary.Compute([]);
    private IReadOnlyList<LocRow> _locRows = [];

    private bool _showIdentical;
    private bool _showDifferent = true;
    private bool _showLeftOnly = true;
    private bool _showRightOnly = true;
    private string _searchText = "";

    public MainViewModel(AppSettings settings)
    {
        _settings = settings;
        _leftPath = settings.LeftPath;
        _rightPath = settings.RightPath;
        _includeAllFiles = settings.IncludeAllFiles;
        _recursive = settings.Recursive;
        _whitespaceIndex = (int)settings.Whitespace;
        _ignoreCase = settings.IgnoreCase;
        _ignoreBlankLines = settings.IgnoreBlankLines;
        _excludedFolders = settings.ExcludedFolders;
        _excludedFiles = settings.ExcludedFiles;
        _showIdentical = settings.ShowIdentical;

        RecentLeft = new ObservableCollection<string>(settings.RecentLeft);
        RecentRight = new ObservableCollection<string>(settings.RecentRight);

        foreach (var info in Languages.Catalog)
            Extensions.Add(new ExtensionItem(info.Extension, info.Language, info.Category, info.DefaultSelected));
        foreach (var ext in settings.CustomExtensions)
            AddExtensionItem(ext, check: false);

        var selected = settings.SelectedExtensions is null ? null : new HashSet<string>(settings.SelectedExtensions, StringComparer.OrdinalIgnoreCase);
        foreach (var item in Extensions)
        {
            item.IsChecked = selected?.Contains(item.Extension) ?? item.DefaultSelected;
            item.PropertyChanged += (_, _) => OnPropertyChanged(nameof(ExtensionSummary));
        }

        ExtensionsView = CollectionViewSource.GetDefaultView(Extensions);
        ExtensionsView.GroupDescriptions.Add(new PropertyGroupDescription(nameof(ExtensionItem.Category)));

        FilesView = CreateFilesView(_files);
    }

    // ---------- Folders ----------

    public string LeftPath
    {
        get => _leftPath;
        set => SetProperty(ref _leftPath, value);
    }

    public string RightPath
    {
        get => _rightPath;
        set => SetProperty(ref _rightPath, value);
    }

    public ObservableCollection<string> RecentLeft { get; }
    public ObservableCollection<string> RecentRight { get; }

    // ---------- File types ----------

    public ObservableCollection<ExtensionItem> Extensions { get; } = [];
    public ICollectionView ExtensionsView { get; }

    public string NewExtension
    {
        get => _newExtension;
        set => SetProperty(ref _newExtension, value);
    }

    public string ExtensionSummary => IncludeAllFiles
        ? "All files are included"
        : $"{Extensions.Count(e => e.IsChecked)} of {Extensions.Count} selected";

    public bool IncludeAllFiles
    {
        get => _includeAllFiles;
        set
        {
            if (SetProperty(ref _includeAllFiles, value)) OnPropertyChanged(nameof(ExtensionSummary));
        }
    }

    /// <summary>Adds the extensions typed by the user (separated by spaces, commas or semicolons).</summary>
    public void AddExtensions()
    {
        foreach (var raw in NewExtension.Split([' ', ',', ';'], StringSplitOptions.RemoveEmptyEntries))
        {
            string ext = raw.Trim().TrimStart('*').ToLowerInvariant();
            if (ext.Length == 0) continue;
            if (!ext.StartsWith('.')) ext = "." + ext;

            var existing = Extensions.FirstOrDefault(e => e.Extension == ext);
            if (existing is not null) existing.IsChecked = true;
            else AddExtensionItem(ext, check: true).PropertyChanged += (_, _) => OnPropertyChanged(nameof(ExtensionSummary));
        }
        NewExtension = "";
        OnPropertyChanged(nameof(ExtensionSummary));
    }

    public void SetAllExtensions(bool isChecked)
    {
        foreach (var e in Extensions) e.IsChecked = isChecked;
    }

    public void ResetExtensions()
    {
        foreach (var e in Extensions) e.IsChecked = e.DefaultSelected;
    }

    public void ToggleCategory(string category)
    {
        var items = Extensions.Where(e => e.Category == category).ToList();
        bool check = items.Any(e => !e.IsChecked);
        foreach (var e in items) e.IsChecked = check;
    }

    private ExtensionItem AddExtensionItem(string ext, bool check)
    {
        var item = new ExtensionItem(ext, Languages.Get("x" + ext).Name, CustomCategory, false) { IsChecked = check };
        Extensions.Add(item);
        return item;
    }

    // ---------- Comparison options ----------

    public bool Recursive
    {
        get => _recursive;
        set => SetProperty(ref _recursive, value);
    }

    public int WhitespaceIndex
    {
        get => _whitespaceIndex;
        set => SetProperty(ref _whitespaceIndex, value);
    }

    public bool IgnoreCase
    {
        get => _ignoreCase;
        set => SetProperty(ref _ignoreCase, value);
    }

    public bool IgnoreBlankLines
    {
        get => _ignoreBlankLines;
        set => SetProperty(ref _ignoreBlankLines, value);
    }

    public string ExcludedFolders
    {
        get => _excludedFolders;
        set => SetProperty(ref _excludedFolders, value);
    }

    public string ExcludedFiles
    {
        get => _excludedFiles;
        set => SetProperty(ref _excludedFiles, value);
    }

    public TextCompareOptions TextOptions => new((WhitespaceMode)WhitespaceIndex, IgnoreCase, IgnoreBlankLines);

    // ---------- Progress ----------

    public bool IsBusy
    {
        get => _isBusy;
        private set => SetProperty(ref _isBusy, value);
    }

    public bool IsIndeterminate
    {
        get => _isIndeterminate;
        private set => SetProperty(ref _isIndeterminate, value);
    }

    public double Progress
    {
        get => _progress;
        private set => SetProperty(ref _progress, value);
    }

    public string StatusText
    {
        get => _statusText;
        set => SetProperty(ref _statusText, value);
    }

    // ---------- Results ----------

    public CompareResult? LastResult
    {
        get => _lastResult;
        private set
        {
            if (SetProperty(ref _lastResult, value)) OnPropertyChanged(nameof(HasResults));
        }
    }

    public bool HasResults => LastResult is not null;

    public ObservableCollection<FileComparison> Files
    {
        get => _files;
        private set => SetProperty(ref _files, value);
    }

    public ICollectionView? FilesView
    {
        get => _filesView;
        private set => SetProperty(ref _filesView, value);
    }

    public CompareSummary Summary
    {
        get => _summary;
        private set
        {
            if (!SetProperty(ref _summary, value)) return;
            LocRows =
            [
                new LocRow("Code lines", value.Left.Code, value.Right.Code),
                new LocRow("Comment lines", value.Left.Comment, value.Right.Comment),
                new LocRow("Blank lines", value.Left.Blank, value.Right.Blank),
                new LocRow("Total lines", value.Left.Total, value.Right.Total),
                new LocRow("Files", value.LeftFiles, value.RightFiles),
            ];
            OnPropertyChanged(nameof(IdenticalLabel));
            OnPropertyChanged(nameof(DifferentLabel));
            OnPropertyChanged(nameof(LeftOnlyLabel));
            OnPropertyChanged(nameof(RightOnlyLabel));
            OnPropertyChanged(nameof(VisibleCountText));
        }
    }

    public IReadOnlyList<LocRow> LocRows
    {
        get => _locRows;
        private set => SetProperty(ref _locRows, value);
    }

    public string IdenticalLabel => $"Identical ({Summary.Identical:N0})";
    public string DifferentLabel => $"Different ({Summary.Different + Summary.Errors:N0})";
    public string LeftOnlyLabel => $"Left only ({Summary.LeftOnly:N0})";
    public string RightOnlyLabel => $"Right only ({Summary.RightOnly:N0})";

    public string VisibleCountText
    {
        get
        {
            int visible = FilesView?.Cast<object>().Count() ?? 0;
            return visible == Files.Count ? $"{Files.Count:N0} files" : $"Showing {visible:N0} of {Files.Count:N0} files";
        }
    }

    // ---------- Filters ----------

    public bool ShowIdentical
    {
        get => _showIdentical;
        set { if (SetProperty(ref _showIdentical, value)) RefreshFilter(); }
    }

    public bool ShowDifferent
    {
        get => _showDifferent;
        set { if (SetProperty(ref _showDifferent, value)) RefreshFilter(); }
    }

    public bool ShowLeftOnly
    {
        get => _showLeftOnly;
        set { if (SetProperty(ref _showLeftOnly, value)) RefreshFilter(); }
    }

    public bool ShowRightOnly
    {
        get => _showRightOnly;
        set { if (SetProperty(ref _showRightOnly, value)) RefreshFilter(); }
    }

    public string SearchText
    {
        get => _searchText;
        set { if (SetProperty(ref _searchText, value)) RefreshFilter(); }
    }

    private ICollectionView CreateFilesView(ObservableCollection<FileComparison> files)
    {
        var view = new ListCollectionView(files) { Filter = FilterFile };
        return view;
    }

    private bool FilterFile(object o)
    {
        if (o is not FileComparison f) return false;
        bool statusVisible = f.Status switch
        {
            FileStatus.Identical => ShowIdentical,
            FileStatus.LeftOnly => ShowLeftOnly,
            FileStatus.RightOnly => ShowRightOnly,
            _ => ShowDifferent,
        };
        if (!statusVisible) return false;
        if (string.IsNullOrWhiteSpace(SearchText)) return true;

        // Every space-separated term must match the path or language.
        foreach (var term in SearchText.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            if (!f.RelativePath.Contains(term, StringComparison.OrdinalIgnoreCase) &&
                !f.Language.Contains(term, StringComparison.OrdinalIgnoreCase))
                return false;
        }
        return true;
    }

    private void RefreshFilter()
    {
        FilesView?.Refresh();
        OnPropertyChanged(nameof(VisibleCountText));
    }

    // ---------- Commands ----------

    public void Swap()
    {
        (LeftPath, RightPath) = (RightPath, LeftPath);
    }

    public void Cancel() => _cts?.Cancel();

    /// <summary>Runs the folder comparison. Throws <see cref="InvalidOperationException"/> for invalid input.</summary>
    public async Task CompareAsync()
    {
        if (IsBusy) return;

        string left = NormalizePath(LeftPath), right = NormalizePath(RightPath);
        if (left.Length == 0 || right.Length == 0)
            throw new InvalidOperationException("Please choose both a left and a right folder.");
        if (!Directory.Exists(left))
            throw new InvalidOperationException($"The left folder does not exist:\n{left}");
        if (!Directory.Exists(right))
            throw new InvalidOperationException($"The right folder does not exist:\n{right}");

        var extensions = Extensions.Where(e => e.IsChecked).Select(e => e.Extension).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (!IncludeAllFiles && extensions.Count == 0)
            throw new InvalidOperationException("Select at least one file type, or tick \"Include all files\".");

        LeftPath = left;
        RightPath = right;
        Remember(RecentLeft, left);
        Remember(RecentRight, right);

        var options = new CompareOptions
        {
            LeftRoot = left,
            RightRoot = right,
            Extensions = extensions,
            IncludeAllFiles = IncludeAllFiles,
            Recursive = Recursive,
            ExcludedFolders = SplitPatterns(ExcludedFolders),
            ExcludedFiles = SplitPatterns(ExcludedFiles),
            Text = TextOptions,
        };

        _cts = new CancellationTokenSource();
        IsBusy = true;
        IsIndeterminate = true;
        Progress = 0;

        var progress = new Progress<CompareProgress>(p =>
        {
            IsIndeterminate = p.Total == 0;
            Progress = p.Total == 0 ? 0 : 100.0 * p.Done / p.Total;
            StatusText = p.Message;
        });

        try
        {
            var result = await FolderComparer.CompareAsync(options, progress, _cts.Token);
            LastResult = result;
            Files = new ObservableCollection<FileComparison>(result.Files);
            FilesView = CreateFilesView(Files);
            Summary = CompareSummary.Compute(Files);
            StatusText = $"Compared {result.Files.Count:N0} files in {result.Elapsed.TotalSeconds:N1}s — " +
                         $"{Summary.Different:N0} different, {Summary.LeftOnly:N0} left only, {Summary.RightOnly:N0} right only, {Summary.Identical:N0} identical.";
            SaveSettings();
        }
        catch (OperationCanceledException)
        {
            StatusText = "Comparison cancelled.";
        }
        finally
        {
            IsBusy = false;
            IsIndeterminate = false;
            Progress = 0;
            _cts.Dispose();
            _cts = null;
        }
    }

    /// <summary>Copies the selected files from one side to the other and re-compares them.</summary>
    public IReadOnlyList<FileComparison> CopyFiles(IEnumerable<FileComparison> items, bool leftToRight)
    {
        if (LastResult is null) return [];
        var options = LastResult.Options;
        var updated = new List<FileComparison>();

        foreach (var item in items.ToList())
        {
            string? source = leftToRight ? item.LeftPath : item.RightPath;
            if (source is null) continue;

            string targetRoot = leftToRight ? options.RightRoot : options.LeftRoot;
            string target = Path.Combine(targetRoot, item.RelativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(source, target, overwrite: true);

            var fresh = FolderComparer.CompareEntry(item.RelativePath,
                leftToRight ? source : target,
                leftToRight ? target : source,
                options);
            Replace(item, fresh);
            updated.Add(fresh);
        }

        Summary = CompareSummary.Compute(Files);
        return updated;
    }

    /// <summary>Re-runs the comparison of a single file (e.g. after it was edited externally).</summary>
    public FileComparison? Refresh(FileComparison item)
    {
        if (LastResult is null) return null;
        var options = LastResult.Options;
        string leftPath = Path.Combine(options.LeftRoot, item.RelativePath);
        string rightPath = Path.Combine(options.RightRoot, item.RelativePath);
        var fresh = FolderComparer.CompareEntry(item.RelativePath,
            File.Exists(leftPath) ? leftPath : null,
            File.Exists(rightPath) ? rightPath : null,
            options);
        Replace(item, fresh);
        Summary = CompareSummary.Compute(Files);
        return fresh;
    }

    private void Replace(FileComparison old, FileComparison fresh)
    {
        int index = Files.IndexOf(old);
        if (index >= 0) Files[index] = fresh;
    }

    public void SaveSettings()
    {
        _settings.LeftPath = LeftPath;
        _settings.RightPath = RightPath;
        _settings.RecentLeft = [.. RecentLeft];
        _settings.RecentRight = [.. RecentRight];
        _settings.SelectedExtensions = Extensions.Where(e => e.IsChecked).Select(e => e.Extension).ToList();
        _settings.CustomExtensions = Extensions.Where(e => e.Category == CustomCategory).Select(e => e.Extension).ToList();
        _settings.IncludeAllFiles = IncludeAllFiles;
        _settings.Recursive = Recursive;
        _settings.Whitespace = (WhitespaceMode)WhitespaceIndex;
        _settings.IgnoreCase = IgnoreCase;
        _settings.IgnoreBlankLines = IgnoreBlankLines;
        _settings.ExcludedFolders = ExcludedFolders;
        _settings.ExcludedFiles = ExcludedFiles;
        _settings.ShowIdentical = ShowIdentical;
        _settings.Save();
    }

    private static void Remember(ObservableCollection<string> recent, string path)
    {
        var existing = recent.FirstOrDefault(p => string.Equals(p, path, StringComparison.OrdinalIgnoreCase));
        if (existing is not null) recent.Remove(existing);
        recent.Insert(0, path);
        while (recent.Count > MaxRecent) recent.RemoveAt(recent.Count - 1);
    }

    private static string NormalizePath(string path)
    {
        path = path.Trim().Trim('"');
        if (path.Length == 0) return "";
        try
        {
            return Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return path;
        }
    }

    private static List<string> SplitPatterns(string text) =>
        text.Split([';', ',', '\n', '\r'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
}
