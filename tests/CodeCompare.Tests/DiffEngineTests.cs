using CodeCompare.Core.Diff;

namespace CodeCompare.Tests;

public class DiffEngineTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void MyersDiff_ProducesValidMinimalEditScript(int seed)
    {
        var random = new Random(seed);
        for (int iteration = 0; iteration < 300; iteration++)
        {
            int[] a = RandomSequence(random, random.Next(0, 40));
            int[] b = random.Next(3) == 0 ? RandomSequence(random, random.Next(0, 40)) : Mutate(random, a);

            var edits = MyersDiff.Diff(a, b);

            // Everything outside the edits must line up as equal elements.
            int ia = 0, ib = 0, editSize = 0;
            foreach (var e in edits)
            {
                Assert.Equal(e.LeftStart - ia, e.RightStart - ib);
                for (; ia < e.LeftStart; ia++, ib++) Assert.Equal(a[ia], b[ib]);
                ia += e.LeftCount;
                ib += e.RightCount;
                editSize += e.LeftCount + e.RightCount;
            }
            Assert.Equal(a.Length - ia, b.Length - ib);
            for (; ia < a.Length; ia++, ib++) Assert.Equal(a[ia], b[ib]);

            // The script must be minimal: |A| + |B| - 2 * LCS.
            Assert.Equal(a.Length + b.Length - 2 * Lcs(a, b), editSize);
        }
    }

    [Fact]
    public void Compare_CountsAddedDeletedModified()
    {
        string[] left = ["a", "b", "c", "d"];
        string[] right = ["a", "B", "c", "d", "e"];

        var result = DiffEngine.Compare(left, right, TextCompareOptions.Exact);

        Assert.Equal(1, result.Modified);
        Assert.Equal(1, result.Added);
        Assert.Equal(0, result.Deleted);
    }

    [Fact]
    public void Compare_HonoursWhitespaceCaseAndBlankLineOptions()
    {
        string[] left = ["  int x = 1;", "", "Return x;"];
        string[] right = ["int  x=1;  ", "return x;"];

        Assert.True(DiffEngine.Compare(left, right, TextCompareOptions.Exact).HasDifferences);

        var lenient = new TextCompareOptions(WhitespaceMode.IgnoreAll, IgnoreCase: true, IgnoreBlankLines: true);
        Assert.False(DiffEngine.Compare(left, right, lenient).HasDifferences);
    }

    [Fact]
    public void BuildRows_AlignsBothSides()
    {
        string[] left = ["one", "two", "three"];
        string[] right = ["one", "2", "three", "four"];
        var result = DiffEngine.Compare(left, right, TextCompareOptions.Exact);

        var rows = DiffEngine.BuildRows(left, right, result, TextCompareOptions.Exact);

        Assert.Equal([DiffRowKind.Equal, DiffRowKind.Modified, DiffRowKind.Equal, DiffRowKind.Inserted], rows.Select(r => r.Kind));
        Assert.Equal(4, rows[3].RightNumber);
        Assert.Null(rows[3].LeftNumber);
    }

    [Fact]
    public void IntraLine_HighlightsOnlyChangedWords()
    {
        var (left, right) = DiffEngine.IntraLine("var total = price * qty;", "var total = price * quantity;", TextCompareOptions.Exact);

        Assert.Equal("qty", Assert.Single(left, s => s.Kind == SegmentKind.Changed).Text);
        Assert.Equal("quantity", Assert.Single(right, s => s.Kind == SegmentKind.Changed).Text);
    }

    [Fact]
    public void CollapseUnchanged_KeepsContextAroundChanges()
    {
        string[] left = Enumerable.Range(0, 50).Select(i => $"line {i}").ToArray();
        string[] right = left.ToArray();
        right[25] = "changed";
        var result = DiffEngine.Compare(left, right, TextCompareOptions.Exact);

        var rows = DiffEngine.CollapseUnchanged(DiffEngine.BuildRows(left, right, result, TextCompareOptions.Exact), context: 3);

        Assert.Equal(1 + 3 + 1 + 3 + 1, rows.Count);
        Assert.Equal(22, rows[0].HiddenRows!.Count);
        Assert.Equal(DiffRowKind.Modified, rows[4].Kind);
    }

    [Fact]
    public void UnifiedDiff_WritesStandardHunks()
    {
        string[] left = ["a", "b", "c"];
        string[] right = ["a", "x", "c", "d"];
        var result = DiffEngine.Compare(left, right, TextCompareOptions.Exact);
        var writer = new StringWriter { NewLine = "\n" };

        UnifiedDiff.Write(writer, "a/f.txt", "b/f.txt", left, right, result);

        Assert.Equal("--- a/f.txt\n+++ b/f.txt\n@@ -1,3 +1,4 @@\n a\n-b\n+x\n c\n+d\n", writer.ToString());
    }

    private static int[] RandomSequence(Random random, int length) =>
        Enumerable.Range(0, length).Select(_ => random.Next(6)).ToArray();

    private static int[] Mutate(Random random, int[] source)
    {
        var list = source.ToList();
        int changes = random.Next(0, 6);
        for (int i = 0; i < changes; i++)
        {
            int op = random.Next(3);
            if (op == 0 || list.Count == 0) list.Insert(random.Next(list.Count + 1), random.Next(6));
            else if (op == 1) list.RemoveAt(random.Next(list.Count));
            else list[random.Next(list.Count)] = random.Next(6);
        }
        return list.ToArray();
    }

    private static int Lcs(int[] a, int[] b)
    {
        var dp = new int[a.Length + 1, b.Length + 1];
        for (int i = 1; i <= a.Length; i++)
            for (int j = 1; j <= b.Length; j++)
                dp[i, j] = a[i - 1] == b[j - 1] ? dp[i - 1, j - 1] + 1 : Math.Max(dp[i - 1, j], dp[i, j - 1]);
        return dp[a.Length, b.Length];
    }
}
