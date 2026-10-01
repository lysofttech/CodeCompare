using CodeCompare.Core.Code;

namespace CodeCompare.Core.Comparison;

public sealed class LanguageStat
{
    public string Language { get; init; } = "";
    public string Extensions { get; init; } = "";
    public int LeftFiles { get; init; }
    public int RightFiles { get; init; }
    public LocStats Left { get; init; }
    public LocStats Right { get; init; }
    public int Added { get; init; }
    public int Deleted { get; init; }
    public int Modified { get; init; }

    public int CodeDelta => Right.Code - Left.Code;
}

public sealed class CompareSummary
{
    public int Total { get; init; }
    public int Identical { get; init; }
    public int Different { get; init; }
    public int LeftOnly { get; init; }
    public int RightOnly { get; init; }
    public int Errors { get; init; }
    public int LeftFiles { get; init; }
    public int RightFiles { get; init; }
    public LocStats Left { get; init; }
    public LocStats Right { get; init; }
    public int Added { get; init; }
    public int Deleted { get; init; }
    public int Modified { get; init; }
    public IReadOnlyList<LanguageStat> Languages { get; init; } = [];

    public int CodeDelta => Right.Code - Left.Code;

    public static CompareSummary Compute(IReadOnlyCollection<FileComparison> files)
    {
        var languages = files
            .GroupBy(f => f.Language, StringComparer.OrdinalIgnoreCase)
            .Select(g => new LanguageStat
            {
                Language = g.Key,
                Extensions = string.Join(" ", g.Select(f => f.Extension.Length == 0 ? "(none)" : f.Extension).Distinct().Order()),
                LeftFiles = g.Count(f => f.LeftPath is not null),
                RightFiles = g.Count(f => f.RightPath is not null),
                Left = Sum(g.Select(f => f.LeftLoc)),
                Right = Sum(g.Select(f => f.RightLoc)),
                Added = g.Sum(f => f.Added),
                Deleted = g.Sum(f => f.Deleted),
                Modified = g.Sum(f => f.Modified),
            })
            .OrderByDescending(s => Math.Max(s.Left.Code, s.Right.Code))
            .ThenBy(s => s.Language)
            .ToList();

        return new CompareSummary
        {
            Total = files.Count,
            Identical = files.Count(f => f.Status == FileStatus.Identical),
            Different = files.Count(f => f.Status == FileStatus.Different),
            LeftOnly = files.Count(f => f.Status == FileStatus.LeftOnly),
            RightOnly = files.Count(f => f.Status == FileStatus.RightOnly),
            Errors = files.Count(f => f.Status == FileStatus.Error),
            LeftFiles = files.Count(f => f.LeftPath is not null),
            RightFiles = files.Count(f => f.RightPath is not null),
            Left = Sum(files.Select(f => f.LeftLoc)),
            Right = Sum(files.Select(f => f.RightLoc)),
            Added = files.Sum(f => f.Added),
            Deleted = files.Sum(f => f.Deleted),
            Modified = files.Sum(f => f.Modified),
            Languages = languages,
        };
    }

    private static LocStats Sum(IEnumerable<LocStats?> values)
    {
        var total = new LocStats();
        foreach (var v in values)
            if (v is { } s) total += s;
        return total;
    }
}
