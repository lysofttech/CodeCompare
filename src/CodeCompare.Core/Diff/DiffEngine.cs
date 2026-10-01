using System.Text;

namespace CodeCompare.Core.Diff;

public static class DiffEngine
{
    /// <summary>Lines longer than this are not diffed word-by-word.</summary>
    private const int MaxIntraLineLength = 4000;

    /// <summary>Below this share of common characters a modified line is shown as fully changed.</summary>
    private const double MinIntraLineSimilarity = 0.3;

    public static DiffResult Compare(IReadOnlyList<string> left, IReadOnlyList<string> right, TextCompareOptions options)
    {
        var ids = new Dictionary<string, int>(StringComparer.Ordinal);
        int[] a = ToIds(left, ids, options);
        int[] b = ToIds(right, ids, options);

        var blocks = new List<DiffBlock>();
        int added = 0, deleted = 0, modified = 0;

        foreach (var edit in MyersDiff.Diff(a, b))
        {
            int leftCount = edit.LeftCount, rightCount = edit.RightCount;
            if (options.IgnoreBlankLines)
            {
                leftCount = CountNonBlank(left, edit.LeftStart, edit.LeftCount);
                rightCount = CountNonBlank(right, edit.RightStart, edit.RightCount);
            }

            bool ignored = leftCount == 0 && rightCount == 0;
            blocks.Add(new DiffBlock(edit.LeftStart, edit.LeftCount, edit.RightStart, edit.RightCount, ignored));

            int paired = Math.Min(leftCount, rightCount);
            modified += paired;
            deleted += leftCount - paired;
            added += rightCount - paired;
        }

        return new DiffResult { Blocks = blocks, Added = added, Deleted = deleted, Modified = modified };
    }

    /// <summary>Builds aligned side-by-side rows, including word-level highlights for modified lines.</summary>
    public static List<DiffRow> BuildRows(IReadOnlyList<string> left, IReadOnlyList<string> right, DiffResult result, TextCompareOptions options)
    {
        var rows = new List<DiffRow>(Math.Max(left.Count, right.Count) + 16);
        int li = 0, ri = 0;

        for (int bi = 0; bi < result.Blocks.Count; bi++)
        {
            var block = result.Blocks[bi];
            while (li < block.LeftStart && ri < block.RightStart)
            {
                rows.Add(EqualRow(left, right, li, ri));
                li++;
                ri++;
            }

            int paired = Math.Min(block.LeftCount, block.RightCount);
            for (int k = 0; k < paired; k++)
            {
                string l = left[block.LeftStart + k], r = right[block.RightStart + k];
                if (block.Ignored)
                {
                    rows.Add(new DiffRow
                    {
                        Kind = DiffRowKind.Ignored, BlockIndex = bi,
                        LeftNumber = block.LeftStart + k + 1, RightNumber = block.RightStart + k + 1,
                        LeftText = l, RightText = r, LeftSegments = Whole(l), RightSegments = Whole(r),
                    });
                    continue;
                }

                var (ls, rs) = IntraLine(l, r, options);
                rows.Add(new DiffRow
                {
                    Kind = DiffRowKind.Modified, BlockIndex = bi,
                    LeftNumber = block.LeftStart + k + 1, RightNumber = block.RightStart + k + 1,
                    LeftText = l, RightText = r, LeftSegments = ls, RightSegments = rs,
                });
            }

            for (int k = paired; k < block.LeftCount; k++)
            {
                string l = left[block.LeftStart + k];
                rows.Add(new DiffRow
                {
                    Kind = block.Ignored ? DiffRowKind.Ignored : DiffRowKind.Deleted, BlockIndex = bi,
                    LeftNumber = block.LeftStart + k + 1, LeftText = l, LeftSegments = Whole(l),
                });
            }

            for (int k = paired; k < block.RightCount; k++)
            {
                string r = right[block.RightStart + k];
                rows.Add(new DiffRow
                {
                    Kind = block.Ignored ? DiffRowKind.Ignored : DiffRowKind.Inserted, BlockIndex = bi,
                    RightNumber = block.RightStart + k + 1, RightText = r, RightSegments = Whole(r),
                });
            }

            li = block.LeftEnd;
            ri = block.RightEnd;
        }

        while (li < left.Count && ri < right.Count)
        {
            rows.Add(EqualRow(left, right, li, ri));
            li++;
            ri++;
        }

        return rows;
    }

    /// <summary>Replaces long runs of unchanged rows with a single collapsed placeholder row.</summary>
    public static List<DiffRow> CollapseUnchanged(IReadOnlyList<DiffRow> rows, int context = 3)
    {
        var keep = new bool[rows.Count];
        for (int i = 0; i < rows.Count; i++)
        {
            if (rows[i].Kind == DiffRowKind.Equal) continue;
            int from = Math.Max(0, i - context), to = Math.Min(rows.Count - 1, i + context);
            for (int j = from; j <= to; j++) keep[j] = true;
        }

        var result = new List<DiffRow>();
        int index = 0;
        while (index < rows.Count)
        {
            if (keep[index])
            {
                result.Add(rows[index++]);
                continue;
            }

            int start = index;
            while (index < rows.Count && !keep[index]) index++;
            var hidden = new DiffRow[index - start];
            for (int k = 0; k < hidden.Length; k++) hidden[k] = rows[start + k];
            result.Add(new DiffRow { Kind = DiffRowKind.Collapsed, HiddenRows = hidden });
        }

        return result;
    }

    /// <summary>Word-level diff of a pair of modified lines.</summary>
    public static (IReadOnlyList<TextSegment> Left, IReadOnlyList<TextSegment> Right) IntraLine(string left, string right, TextCompareOptions options)
    {
        if (left.Length == 0 || right.Length == 0 || left.Length > MaxIntraLineLength || right.Length > MaxIntraLineLength)
            return (Whole(left), Whole(right));

        var leftTokens = Tokenize(left);
        var rightTokens = Tokenize(right);

        var ids = new Dictionary<string, int>(StringComparer.Ordinal);
        int[] a = TokenIds(leftTokens, ids, options);
        int[] b = TokenIds(rightTokens, ids, options);
        var edits = MyersDiff.Diff(a, b);

        var changedLeft = new bool[leftTokens.Count];
        var changedRight = new bool[rightTokens.Count];
        foreach (var e in edits)
        {
            for (int i = 0; i < e.LeftCount; i++) changedLeft[e.LeftStart + i] = true;
            for (int i = 0; i < e.RightCount; i++) changedRight[e.RightStart + i] = true;
        }

        // Whitespace changes are not highlighted when whitespace is being ignored.
        if (options.Whitespace != WhitespaceMode.Exact)
        {
            for (int i = 0; i < leftTokens.Count; i++) if (IsWhitespace(leftTokens[i])) changedLeft[i] = false;
            for (int i = 0; i < rightTokens.Count; i++) if (IsWhitespace(rightTokens[i])) changedRight[i] = false;
        }

        int commonChars = 0;
        for (int i = 0; i < leftTokens.Count; i++) if (!changedLeft[i]) commonChars += leftTokens[i].Length;
        double similarity = (double)commonChars / Math.Max(left.Length, right.Length);
        if (similarity < MinIntraLineSimilarity)
            return (Whole(left), Whole(right));

        return (Merge(leftTokens, changedLeft), Merge(rightTokens, changedRight));
    }

    internal static List<string> Tokenize(string text)
    {
        var tokens = new List<string>();
        int i = 0;
        while (i < text.Length)
        {
            char c = text[i];
            int j = i + 1;
            if (IsWordChar(c))
                while (j < text.Length && IsWordChar(text[j])) j++;
            else if (char.IsWhiteSpace(c))
                while (j < text.Length && char.IsWhiteSpace(text[j])) j++;
            tokens.Add(text[i..j]);
            i = j;
        }
        return tokens;
    }

    internal static string Normalize(string line, TextCompareOptions options)
    {
        string s = options.Whitespace switch
        {
            WhitespaceMode.IgnoreLeadingTrailing => line.Trim(),
            WhitespaceMode.IgnoreAll => RemoveWhitespace(line),
            _ => line,
        };
        return options.IgnoreCase ? s.ToUpperInvariant() : s;
    }

    private static DiffRow EqualRow(IReadOnlyList<string> left, IReadOnlyList<string> right, int li, int ri) => new()
    {
        Kind = DiffRowKind.Equal,
        LeftNumber = li + 1,
        RightNumber = ri + 1,
        LeftText = left[li],
        RightText = right[ri],
        LeftSegments = Whole(left[li]),
        RightSegments = Whole(right[ri]),
    };

    private static IReadOnlyList<TextSegment> Whole(string text) => [new TextSegment(text, SegmentKind.Normal)];

    private static List<TextSegment> Merge(List<string> tokens, bool[] changed)
    {
        var segments = new List<TextSegment>();
        var sb = new StringBuilder();
        bool current = false;
        for (int i = 0; i < tokens.Count; i++)
        {
            if (sb.Length > 0 && changed[i] != current)
            {
                segments.Add(new TextSegment(sb.ToString(), current ? SegmentKind.Changed : SegmentKind.Normal));
                sb.Clear();
            }
            current = changed[i];
            sb.Append(tokens[i]);
        }
        if (sb.Length > 0)
            segments.Add(new TextSegment(sb.ToString(), current ? SegmentKind.Changed : SegmentKind.Normal));
        return segments;
    }

    private static int[] ToIds(IReadOnlyList<string> lines, Dictionary<string, int> ids, TextCompareOptions options)
    {
        var result = new int[lines.Count];
        for (int i = 0; i < lines.Count; i++)
        {
            string key = Normalize(lines[i], options);
            if (options.IgnoreBlankLines && key.Trim().Length == 0) key = "";
            if (!ids.TryGetValue(key, out int id))
            {
                id = ids.Count;
                ids.Add(key, id);
            }
            result[i] = id;
        }
        return result;
    }

    private static int[] TokenIds(List<string> tokens, Dictionary<string, int> ids, TextCompareOptions options)
    {
        var result = new int[tokens.Count];
        for (int i = 0; i < tokens.Count; i++)
        {
            string key = tokens[i];
            if (options.Whitespace != WhitespaceMode.Exact && IsWhitespace(key)) key = " ";
            else if (options.IgnoreCase) key = key.ToUpperInvariant();
            if (!ids.TryGetValue(key, out int id))
            {
                id = ids.Count;
                ids.Add(key, id);
            }
            result[i] = id;
        }
        return result;
    }

    private static int CountNonBlank(IReadOnlyList<string> lines, int start, int count)
    {
        int n = 0;
        for (int i = start; i < start + count; i++)
            if (!string.IsNullOrWhiteSpace(lines[i])) n++;
        return n;
    }

    private static string RemoveWhitespace(string s)
    {
        int i = 0;
        while (i < s.Length && !char.IsWhiteSpace(s[i])) i++;
        if (i == s.Length) return s;

        var sb = new StringBuilder(s.Length);
        sb.Append(s, 0, i);
        for (; i < s.Length; i++)
            if (!char.IsWhiteSpace(s[i])) sb.Append(s[i]);
        return sb.ToString();
    }

    private static bool IsWordChar(char c) => char.IsLetterOrDigit(c) || c == '_';

    private static bool IsWhitespace(string token) => token.Length > 0 && char.IsWhiteSpace(token[0]);
}
