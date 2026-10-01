using System.Text;

namespace CodeCompare.Core.Diff;

/// <summary>Writes diffs in the standard unified format understood by git, patch and most review tools.</summary>
public static class UnifiedDiff
{
    public static void Write(TextWriter writer, string leftName, string rightName,
                             IReadOnlyList<string> left, IReadOnlyList<string> right,
                             DiffResult result, int context = 3)
    {
        var blocks = result.Blocks;
        if (blocks.Count == 0) return;

        writer.WriteLine($"--- {leftName}");
        writer.WriteLine($"+++ {rightName}");

        int i = 0;
        while (i < blocks.Count)
        {
            // Group blocks whose surrounding context would overlap into one hunk.
            int j = i;
            while (j + 1 < blocks.Count && blocks[j + 1].LeftStart - blocks[j].LeftEnd <= 2 * context) j++;

            var first = blocks[i];
            var last = blocks[j];
            int leftStart = Math.Max(0, first.LeftStart - context);
            int rightStart = leftStart + (first.RightStart - first.LeftStart);
            int leftEnd = Math.Min(left.Count, last.LeftEnd + context);
            int rightEnd = leftEnd + (last.RightEnd - last.LeftEnd);

            var hunk = new StringBuilder();
            int l = leftStart;
            for (int k = i; k <= j; k++)
            {
                var b = blocks[k];
                for (; l < b.LeftStart; l++) hunk.Append(' ').Append(left[l]).Append('\n');
                for (int x = 0; x < b.LeftCount; x++) hunk.Append('-').Append(left[b.LeftStart + x]).Append('\n');
                for (int x = 0; x < b.RightCount; x++) hunk.Append('+').Append(right[b.RightStart + x]).Append('\n');
                l = b.LeftEnd;
            }
            for (; l < leftEnd; l++) hunk.Append(' ').Append(left[l]).Append('\n');

            writer.WriteLine($"@@ -{Range(leftStart, leftEnd - leftStart)} +{Range(rightStart, rightEnd - rightStart)} @@");
            writer.Write(hunk.ToString());
            i = j + 1;
        }
    }

    private static string Range(int start, int length) => length switch
    {
        0 => $"{start},0",
        1 => $"{start + 1}",
        _ => $"{start + 1},{length}",
    };
}
