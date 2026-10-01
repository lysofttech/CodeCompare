using System.Diagnostics;
using System.IO.Enumeration;
using CodeCompare.Core.Code;
using CodeCompare.Core.Diff;
using CodeCompare.Core.IO;

namespace CodeCompare.Core.Comparison;

public static class FolderComparer
{
    public static Task<CompareResult> CompareAsync(CompareOptions options, IProgress<CompareProgress>? progress, CancellationToken cancellationToken) =>
        Task.Run(() => Compare(options, progress, cancellationToken), cancellationToken);

    public static CompareResult Compare(CompareOptions options, IProgress<CompareProgress>? progress, CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();

        progress?.Report(new CompareProgress(0, 0, "Scanning left folder…"));
        var left = Scan(options.LeftRoot, options, cancellationToken);
        progress?.Report(new CompareProgress(0, 0, "Scanning right folder…"));
        var right = Scan(options.RightRoot, options, cancellationToken);

        string[] keys = left.Keys.Union(right.Keys, StringComparer.OrdinalIgnoreCase)
            .OrderBy(k => k, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var results = new FileComparison[keys.Length];
        int done = 0;
        var parallel = new ParallelOptions { CancellationToken = cancellationToken, MaxDegreeOfParallelism = Environment.ProcessorCount };

        Parallel.For(0, keys.Length, parallel, i =>
        {
            left.TryGetValue(keys[i], out string? leftPath);
            right.TryGetValue(keys[i], out string? rightPath);
            results[i] = CompareEntry(keys[i], leftPath, rightPath, options);

            int n = Interlocked.Increment(ref done);
            if (n % 25 == 0 || n == keys.Length)
                progress?.Report(new CompareProgress(n, keys.Length, $"Comparing {n:N0} of {keys.Length:N0}: {keys[i]}"));
        });

        return new CompareResult { Options = options, Files = results, Elapsed = stopwatch.Elapsed };
    }

    /// <summary>Compares one relative path; either side may be missing.</summary>
    public static FileComparison CompareEntry(string relativePath, string? leftPath, string? rightPath, CompareOptions options)
    {
        var language = Languages.Get(relativePath);
        var item = new FileComparison
        {
            RelativePath = relativePath,
            LeftPath = leftPath,
            RightPath = rightPath,
            Language = language.Name,
        };

        try
        {
            FileInfo? leftInfo = leftPath is null ? null : new FileInfo(leftPath);
            FileInfo? rightInfo = rightPath is null ? null : new FileInfo(rightPath);
            if (leftInfo is { Exists: false }) leftInfo = null;
            if (rightInfo is { Exists: false }) rightInfo = null;

            item.LeftSize = leftInfo?.Length;
            item.RightSize = rightInfo?.Length;
            item.LeftModified = leftInfo?.LastWriteTime;
            item.RightModified = rightInfo?.LastWriteTime;

            if (leftInfo is null && rightInfo is null)
            {
                item.Status = FileStatus.Error;
                item.Note = "File no longer exists";
                return item;
            }

            if (leftInfo is null || rightInfo is null)
            {
                var info = leftInfo ?? rightInfo!;
                item.Status = leftInfo is null ? FileStatus.RightOnly : FileStatus.LeftOnly;
                if (info.Length > options.MaxTextFileBytes)
                {
                    item.Note = "Too large to count lines";
                    return item;
                }

                var file = TextFile.Load(info.FullName);
                item.IsBinary = file.IsBinary;
                if (file.IsBinary) return item;

                var loc = LineCounter.Count(file.Lines, language);
                int lines = options.Text.IgnoreBlankLines ? loc.Total - loc.Blank : loc.Total;
                if (leftInfo is null) { item.RightLoc = loc; item.Added = lines; }
                else { item.LeftLoc = loc; item.Deleted = lines; }
                return item;
            }

            if (leftInfo.Length > options.MaxTextFileBytes || rightInfo.Length > options.MaxTextFileBytes)
            {
                item.Status = StreamsEqual(leftInfo, rightInfo) ? FileStatus.Identical : FileStatus.Different;
                item.Note = "Too large for a line comparison; compared byte-for-byte";
                return item;
            }

            byte[] leftBytes = File.ReadAllBytes(leftInfo.FullName);
            byte[] rightBytes = File.ReadAllBytes(rightInfo.FullName);
            bool sameBytes = leftBytes.AsSpan().SequenceEqual(rightBytes);

            var leftFile = TextFile.FromBytes(leftBytes);
            var rightFile = sameBytes ? leftFile : TextFile.FromBytes(rightBytes);

            if (leftFile.IsBinary || rightFile.IsBinary)
            {
                item.IsBinary = true;
                item.Status = sameBytes ? FileStatus.Identical : FileStatus.Different;
                return item;
            }

            item.LeftLoc = LineCounter.Count(leftFile.Lines, language);
            item.RightLoc = sameBytes ? item.LeftLoc : LineCounter.Count(rightFile.Lines, language);

            if (sameBytes)
            {
                item.Status = FileStatus.Identical;
                return item;
            }

            var diff = DiffEngine.Compare(leftFile.Lines, rightFile.Lines, options.Text);
            item.Added = diff.Added;
            item.Deleted = diff.Deleted;
            item.Modified = diff.Modified;
            item.Status = diff.HasDifferences ? FileStatus.Different : FileStatus.Identical;
            if (!diff.HasDifferences) item.Note = DescribeEquivalence(leftFile, rightFile);
            return item;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            item.Status = FileStatus.Error;
            item.Note = ex.Message;
            return item;
        }
    }

    private static string DescribeEquivalence(TextFile left, TextFile right)
    {
        var reasons = new List<string>();
        if (left.EncodingName != right.EncodingName) reasons.Add($"encoding ({left.EncodingName} vs {right.EncodingName})");
        if (left.LineEnding != right.LineEnding) reasons.Add($"line endings ({Or(left.LineEnding)} vs {Or(right.LineEnding)})");
        if (reasons.Count == 0 || !left.Lines.SequenceEqual(right.Lines)) reasons.Add("ignored whitespace/case/blank lines");
        return "Equivalent – differs only in " + string.Join(", ", reasons);

        static string Or(string eol) => eol.Length == 0 ? "none" : eol;
    }

    private static Dictionary<string, string> Scan(string root, CompareOptions options, CancellationToken cancellationToken)
    {
        if (!Directory.Exists(root))
            throw new DirectoryNotFoundException($"Folder not found: {root}");

        var files = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var pending = new Stack<string>();
        pending.Push(root);

        while (pending.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string dir = pending.Pop();

            try
            {
                foreach (string file in Directory.EnumerateFiles(dir))
                {
                    string name = Path.GetFileName(file);
                    if (Matches(name, options.ExcludedFiles)) continue;
                    if (!options.IncludeAllFiles && !options.Extensions.Contains(Languages.GetExtension(name))) continue;
                    files[Path.GetRelativePath(root, file)] = file;
                }

                if (!options.Recursive) continue;

                foreach (string sub in Directory.EnumerateDirectories(dir))
                {
                    if (Matches(Path.GetFileName(sub), options.ExcludedFolders)) continue;
                    // Skip junctions and symlinks so link cycles cannot cause endless scanning.
                    if ((File.GetAttributes(sub) & FileAttributes.ReparsePoint) != 0) continue;
                    pending.Push(sub);
                }
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
            {
                // Inaccessible folders are skipped.
            }
        }

        return files;
    }

    private static bool Matches(string name, IReadOnlyList<string> patterns)
    {
        foreach (string pattern in patterns)
            if (FileSystemName.MatchesSimpleExpression(pattern, name, ignoreCase: true)) return true;
        return false;
    }

    private static bool StreamsEqual(FileInfo a, FileInfo b)
    {
        if (a.Length != b.Length) return false;

        const int bufferSize = 1 << 16;
        using var sa = a.OpenRead();
        using var sb = b.OpenRead();
        var bufferA = new byte[bufferSize];
        var bufferB = new byte[bufferSize];
        while (true)
        {
            int readA = sa.ReadAtLeast(bufferA, bufferSize, throwOnEndOfStream: false);
            int readB = sb.ReadAtLeast(bufferB, bufferSize, throwOnEndOfStream: false);
            if (readA != readB || !bufferA.AsSpan(0, readA).SequenceEqual(bufferB.AsSpan(0, readB))) return false;
            if (readA == 0) return true;
        }
    }
}
