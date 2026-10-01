namespace CodeCompare.Core.Code;

public readonly record struct LocStats(int Total, int Code, int Comment, int Blank)
{
    public static LocStats operator +(LocStats a, LocStats b) =>
        new(a.Total + b.Total, a.Code + b.Code, a.Comment + b.Comment, a.Blank + b.Blank);
}

/// <summary>
/// Classifies each line as code, comment or blank. A line containing both code and a comment counts as code.
/// String literals are skipped on a single line so comment markers inside strings are not mistaken for comments.
/// </summary>
public static class LineCounter
{
    public static LocStats Count(IReadOnlyList<string> lines, LanguageDefinition language)
    {
        int code = 0, comment = 0, blank = 0;
        int openBlock = -1; // index into BlockComments while inside a multi-line comment

        foreach (string line in lines)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                blank++;
                continue;
            }

            if (openBlock < 0 && IsStartOnlyComment(line, language))
            {
                comment++;
                continue;
            }

            bool hasCode = false, hasComment = false;
            int i = 0;
            while (i < line.Length)
            {
                if (openBlock >= 0)
                {
                    hasComment = true;
                    string end = language.BlockComments[openBlock].End;
                    int e = line.IndexOf(end, i, StringComparison.Ordinal);
                    if (e < 0) break;
                    i = e + end.Length;
                    openBlock = -1;
                    continue;
                }

                char c = line[i];
                if (char.IsWhiteSpace(c))
                {
                    i++;
                    continue;
                }

                int block = MatchBlockStart(line, i, language.BlockComments);
                if (block >= 0)
                {
                    openBlock = block;
                    hasComment = true;
                    i += language.BlockComments[block].Start.Length;
                    continue;
                }

                if (StartsWithAny(line, i, language.LineComments))
                {
                    hasComment = true;
                    break;
                }

                hasCode = true;
                i = Array.IndexOf(language.StringDelimiters, c) >= 0 ? SkipString(line, i) : i + 1;
            }

            if (hasCode) code++;
            else if (hasComment) comment++;
            else blank++;
        }

        return new LocStats(lines.Count, code, comment, blank);
    }

    private static bool IsStartOnlyComment(string line, LanguageDefinition language)
    {
        if (language.StartOnlyLineComments.Length == 0) return false;

        var trimmed = line.AsSpan().TrimStart();
        foreach (string marker in language.StartOnlyLineComments)
        {
            if (!trimmed.StartsWith(marker, StringComparison.OrdinalIgnoreCase)) continue;
            // Word markers like REM must not match REMOVE; symbol markers like :: can be followed by anything.
            if (!char.IsLetter(marker[^1]) || trimmed.Length == marker.Length || !char.IsLetterOrDigit(trimmed[marker.Length]))
                return true;
        }
        return false;
    }

    private static int MatchBlockStart(string line, int index, (string Start, string End)[] blocks)
    {
        for (int b = 0; b < blocks.Length; b++)
            if (line.AsSpan(index).StartsWith(blocks[b].Start, StringComparison.Ordinal)) return b;
        return -1;
    }

    private static bool StartsWithAny(string line, int index, string[] markers)
    {
        foreach (string marker in markers)
            if (line.AsSpan(index).StartsWith(marker, StringComparison.Ordinal)) return true;
        return false;
    }

    private static int SkipString(string line, int start)
    {
        char quote = line[start];
        int i = start + 1;
        while (i < line.Length)
        {
            if (line[i] == '\\') i += 2;
            else if (line[i] == quote) return i + 1;
            else i++;
        }
        return line.Length;
    }
}
