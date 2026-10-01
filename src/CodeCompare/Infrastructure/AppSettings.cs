using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using CodeCompare.Core.Diff;

namespace CodeCompare.Infrastructure;

/// <summary>User preferences persisted to %AppData%\CodeCompare\settings.json.</summary>
public sealed class AppSettings
{
    public const string DefaultExcludedFolders = "bin;obj;.git;.vs;.idea;.vscode;node_modules;packages;__pycache__;.svn;.hg;TestResults;.venv;venv";
    public const string DefaultExcludedFiles = "*.min.js;*.min.css;*.designer.cs;*.g.cs;*.g.i.cs;*.AssemblyInfo.cs";

    private static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "CodeCompare", "settings.json");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public string LeftPath { get; set; } = "";
    public string RightPath { get; set; } = "";
    public List<string> RecentLeft { get; set; } = [];
    public List<string> RecentRight { get; set; } = [];

    /// <summary>Null means "use the defaults from the catalog".</summary>
    public List<string>? SelectedExtensions { get; set; }
    public List<string> CustomExtensions { get; set; } = [];

    public bool IncludeAllFiles { get; set; }
    public bool Recursive { get; set; } = true;
    public WhitespaceMode Whitespace { get; set; } = WhitespaceMode.Exact;
    public bool IgnoreCase { get; set; }
    public bool IgnoreBlankLines { get; set; }
    public string ExcludedFolders { get; set; } = DefaultExcludedFolders;
    public string ExcludedFiles { get; set; } = DefaultExcludedFiles;

    public bool ShowIdentical { get; set; } = true;
    public bool WordWrap { get; set; } = true;
    public bool ChangesOnly { get; set; }
    public double DiffFontSize { get; set; } = 12;

    public double WindowWidth { get; set; } = 1440;
    public double WindowHeight { get; set; } = 900;
    public bool WindowMaximized { get; set; }
    public double OptionsPanelWidth { get; set; } = 300;

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath), JsonOptions) ?? new AppSettings();
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            // Corrupt or unreadable settings fall back to defaults.
        }
        return new AppSettings();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, JsonOptions));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Settings are a convenience; failing to save them is not fatal.
        }
    }
}
