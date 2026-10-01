using System.Diagnostics;
using System.IO;

namespace CodeCompare.Infrastructure;

public static class Shell
{
    /// <summary>Opens a file with its associated application.</summary>
    public static void Open(string? path)
    {
        if (string.IsNullOrEmpty(path) || !File.Exists(path)) return;
        Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
    }

    /// <summary>Opens Explorer with the file selected.</summary>
    public static void ShowInExplorer(string? path)
    {
        if (string.IsNullOrEmpty(path)) return;
        if (File.Exists(path))
            Process.Start("explorer.exe", $"/select,\"{path}\"");
        else if (Directory.Exists(path))
            Process.Start("explorer.exe", $"\"{path}\"");
    }
}
