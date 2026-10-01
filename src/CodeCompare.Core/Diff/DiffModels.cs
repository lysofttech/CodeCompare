namespace CodeCompare.Core.Diff;

public enum WhitespaceMode
{
    /// <summary>Whitespace must match exactly.</summary>
    Exact,
    /// <summary>Indentation and trailing whitespace are ignored.</summary>
    IgnoreLeadingTrailing,
    /// <summary>All whitespace is ignored.</summary>
    IgnoreAll,
}

public sealed record TextCompareOptions(
    WhitespaceMode Whitespace = WhitespaceMode.Exact,
    bool IgnoreCase = false,
    bool IgnoreBlankLines = false)
{
    public static TextCompareOptions Exact { get; } = new();
}

/// <summary>A contiguous run of changed lines. Line indexes are zero-based.</summary>
public sealed record DiffBlock(int LeftStart, int LeftCount, int RightStart, int RightCount, bool Ignored)
{
    public int LeftEnd => LeftStart + LeftCount;
    public int RightEnd => RightStart + RightCount;
}

public sealed class DiffResult
{
    public required IReadOnlyList<DiffBlock> Blocks { get; init; }

    /// <summary>Lines that only exist on the right side.</summary>
    public int Added { get; init; }

    /// <summary>Lines that only exist on the left side.</summary>
    public int Deleted { get; init; }

    /// <summary>Lines that were changed in place (paired left/right lines).</summary>
    public int Modified { get; init; }

    public int ChangeCount => Blocks.Count(b => !b.Ignored);

    public bool HasDifferences => Added + Deleted + Modified > 0;
}

public enum DiffRowKind
{
    Equal,
    Modified,
    Deleted,
    Inserted,
    /// <summary>Changed lines that the current options say to ignore (e.g. blank lines).</summary>
    Ignored,
    /// <summary>Placeholder for a run of unchanged lines hidden in "changes only" mode.</summary>
    Collapsed,
}

/// <summary>How one half of a side-by-side row should be rendered.</summary>
public enum DiffSide
{
    Normal,
    Changed,
    Empty,
    Ignored,
    Collapsed,
}

public enum SegmentKind
{
    Normal,
    Changed,
}

public readonly record struct TextSegment(string Text, SegmentKind Kind);

public sealed class DiffRow
{
    public DiffRowKind Kind { get; init; }

    /// <summary>1-based line number on the left, or null when the left side is empty.</summary>
    public int? LeftNumber { get; init; }

    /// <summary>1-based line number on the right, or null when the right side is empty.</summary>
    public int? RightNumber { get; init; }

    public string LeftText { get; init; } = "";
    public string RightText { get; init; } = "";

    public IReadOnlyList<TextSegment> LeftSegments { get; init; } = [];
    public IReadOnlyList<TextSegment> RightSegments { get; init; } = [];

    /// <summary>Index of the <see cref="DiffBlock"/> this row belongs to, or -1 for unchanged rows.</summary>
    public int BlockIndex { get; init; } = -1;

    /// <summary>For <see cref="DiffRowKind.Collapsed"/> rows: the rows that are hidden.</summary>
    public IReadOnlyList<DiffRow>? HiddenRows { get; init; }

    public bool IsChange => Kind is DiffRowKind.Modified or DiffRowKind.Deleted or DiffRowKind.Inserted;

    public DiffSide LeftSide => Kind switch
    {
        DiffRowKind.Modified or DiffRowKind.Deleted => DiffSide.Changed,
        DiffRowKind.Inserted => DiffSide.Empty,
        DiffRowKind.Ignored => LeftNumber is null ? DiffSide.Empty : DiffSide.Ignored,
        DiffRowKind.Collapsed => DiffSide.Collapsed,
        _ => DiffSide.Normal,
    };

    public DiffSide RightSide => Kind switch
    {
        DiffRowKind.Modified or DiffRowKind.Inserted => DiffSide.Changed,
        DiffRowKind.Deleted => DiffSide.Empty,
        DiffRowKind.Ignored => RightNumber is null ? DiffSide.Empty : DiffSide.Ignored,
        DiffRowKind.Collapsed => DiffSide.Collapsed,
        _ => DiffSide.Normal,
    };

    public string CollapsedText => HiddenRows is { Count: var n } ? $"⋯  {n:N0} unchanged line{(n == 1 ? "" : "s")}  (double-click to expand)  ⋯" : "";
}
