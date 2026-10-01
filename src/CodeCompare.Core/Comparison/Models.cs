using CodeCompare.Core.Code;
using CodeCompare.Core.Diff;

namespace CodeCompare.Core.Comparison;

public enum FileStatus
{
    Identical,
    Different,
    LeftOnly,
    RightOnly,
    Error,
}

public sealed class CompareOptions
{
    public required string LeftRoot { get; init; }
    public required string RightRoot { get; init; }

    /// <summary>Lower-case extensions including the dot; "" matches files without an extension.</summary>
    public IReadOnlySet<string> Extensions { get; init; } = new HashSet<string>();

    public bool IncludeAllFiles { get; init; }
    public bool Recursive { get; init; } = true;

    /// <summary>Folder name patterns (wildcards allowed) that are skipped entirely.</summary>
    public IReadOnlyList<string> ExcludedFolders { get; init; } = [];

    /// <summary>File name patterns (wildcards allowed) that are skipped.</summary>
    public IReadOnlyList<string> ExcludedFiles { get; init; } = [];

    public TextCompareOptions Text { get; init; } = TextCompareOptions.Exact;

    /// <summary>Files larger than this are compared byte-for-byte only.</summary>
    public long MaxTextFileBytes { get; init; } = 32L * 1024 * 1024;
}

public sealed class FileComparison
{
    public required string RelativePath { get; init; }
    public string? LeftPath { get; init; }
    public string? RightPath { get; init; }
    public FileStatus Status { get; set; }
    public bool IsBinary { get; set; }
    public string? Note { get; set; }

    public long? LeftSize { get; set; }
    public long? RightSize { get; set; }
    public DateTime? LeftModified { get; set; }
    public DateTime? RightModified { get; set; }

    public LocStats? LeftLoc { get; set; }
    public LocStats? RightLoc { get; set; }

    public int Added { get; set; }
    public int Deleted { get; set; }
    public int Modified { get; set; }

    public string Extension => Languages.GetExtension(RelativePath);
    public string Language { get; init; } = "";

    public string FileName => Path.GetFileName(RelativePath);

    public string Folder
    {
        get
        {
            string? dir = Path.GetDirectoryName(RelativePath);
            return string.IsNullOrEmpty(dir) ? "" : dir + Path.DirectorySeparatorChar;
        }
    }

    public int? LeftCode => LeftLoc?.Code;
    public int? RightCode => RightLoc?.Code;
    public int? LeftLines => LeftLoc?.Total;
    public int? RightLines => RightLoc?.Total;

    public int? CodeDelta => LeftLoc is null && RightLoc is null ? null : (RightLoc?.Code ?? 0) - (LeftLoc?.Code ?? 0);

    public int ChangedLines => Added + Deleted + Modified;

    public bool IsNewer(bool left) => (LeftModified, RightModified) switch
    {
        ({ } l, { } r) => left ? l > r : r > l,
        _ => false,
    };

    public string StatusText => Status switch
    {
        FileStatus.Identical => "Identical",
        FileStatus.Different => IsBinary ? "Binary differs" : "Different",
        FileStatus.LeftOnly => "Left only",
        FileStatus.RightOnly => "Right only",
        _ => "Error",
    };
}

public sealed record CompareProgress(int Done, int Total, string Message);

public sealed class CompareResult
{
    public required CompareOptions Options { get; init; }
    public required IReadOnlyList<FileComparison> Files { get; init; }
    public TimeSpan Elapsed { get; init; }
}
