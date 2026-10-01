namespace CodeCompare.Core.Diff;

/// <summary>
/// Linear-space implementation of Eugene Myers' O(ND) difference algorithm
/// ("An O(ND) Difference Algorithm and Its Variations", 1986), using the
/// middle-snake divide and conquer so memory stays O(N + M) even for large files.
/// Sequences are compared as integer ids so the caller controls equality.
/// </summary>
internal static class MyersDiff
{
    public readonly record struct Edit(int LeftStart, int LeftCount, int RightStart, int RightCount);

    public static List<Edit> Diff(int[] a, int[] b)
    {
        var modifiedA = new bool[a.Length + 2];
        var modifiedB = new bool[b.Length + 2];
        int max = a.Length + b.Length + 1;
        var down = new int[2 * max + 2];
        var up = new int[2 * max + 2];

        Lcs(a, 0, a.Length, modifiedA, b, 0, b.Length, modifiedB, down, up);
        return CreateEdits(modifiedA, a.Length, modifiedB, b.Length);
    }

    private static void Lcs(int[] a, int lowerA, int upperA, bool[] modifiedA,
                            int[] b, int lowerB, int upperB, bool[] modifiedB,
                            int[] down, int[] up)
    {
        // Skip the common prefix and suffix.
        while (lowerA < upperA && lowerB < upperB && a[lowerA] == b[lowerB]) { lowerA++; lowerB++; }
        while (lowerA < upperA && lowerB < upperB && a[upperA - 1] == b[upperB - 1]) { upperA--; upperB--; }

        if (lowerA == upperA)
        {
            while (lowerB < upperB) modifiedB[lowerB++] = true;
        }
        else if (lowerB == upperB)
        {
            while (lowerA < upperA) modifiedA[lowerA++] = true;
        }
        else
        {
            var (x, y) = ShortestMiddleSnake(a, lowerA, upperA, b, lowerB, upperB, down, up);
            Lcs(a, lowerA, x, modifiedA, b, lowerB, y, modifiedB, down, up);
            Lcs(a, x, upperA, modifiedA, b, y, upperB, modifiedB, down, up);
        }
    }

    private static (int X, int Y) ShortestMiddleSnake(int[] a, int lowerA, int upperA,
                                                      int[] b, int lowerB, int upperB,
                                                      int[] down, int[] up)
    {
        int max = a.Length + b.Length + 1;
        int downK = lowerA - lowerB;
        int upK = upperA - upperB;
        int delta = (upperA - lowerA) - (upperB - lowerB);
        bool oddDelta = (delta & 1) != 0;
        int downOffset = max - downK;
        int upOffset = max - upK;
        int maxD = ((upperA - lowerA + upperB - lowerB) / 2) + 1;

        down[downOffset + downK + 1] = lowerA;
        up[upOffset + upK - 1] = upperA;

        for (int d = 0; d <= maxD; d++)
        {
            // Forward search.
            for (int k = downK - d; k <= downK + d; k += 2)
            {
                int x;
                if (k == downK - d)
                {
                    x = down[downOffset + k + 1];
                }
                else
                {
                    x = down[downOffset + k - 1] + 1;
                    if (k < downK + d && down[downOffset + k + 1] >= x)
                        x = down[downOffset + k + 1];
                }

                int y = x - k;
                while (x < upperA && y < upperB && a[x] == b[y]) { x++; y++; }
                down[downOffset + k] = x;

                if (oddDelta && upK - d < k && k < upK + d && up[upOffset + k] <= down[downOffset + k])
                    return (down[downOffset + k], down[downOffset + k] - k);
            }

            // Reverse search.
            for (int k = upK - d; k <= upK + d; k += 2)
            {
                int x;
                if (k == upK + d)
                {
                    x = up[upOffset + k - 1];
                }
                else
                {
                    x = up[upOffset + k + 1] - 1;
                    if (k > upK - d && up[upOffset + k - 1] < x)
                        x = up[upOffset + k - 1];
                }

                int y = x - k;
                while (x > lowerA && y > lowerB && a[x - 1] == b[y - 1]) { x--; y--; }
                up[upOffset + k] = x;

                if (!oddDelta && downK - d <= k && k <= downK + d && up[upOffset + k] <= down[downOffset + k])
                    return (down[downOffset + k], down[downOffset + k] - k);
            }
        }

        throw new InvalidOperationException("Diff algorithm failed to find a middle snake.");
    }

    private static List<Edit> CreateEdits(bool[] modifiedA, int lengthA, bool[] modifiedB, int lengthB)
    {
        var edits = new List<Edit>();
        int lineA = 0, lineB = 0;

        while (lineA < lengthA || lineB < lengthB)
        {
            if (lineA < lengthA && !modifiedA[lineA] && lineB < lengthB && !modifiedB[lineB])
            {
                lineA++;
                lineB++;
                continue;
            }

            int startA = lineA, startB = lineB;
            while (lineA < lengthA && (lineB >= lengthB || modifiedA[lineA])) lineA++;
            while (lineB < lengthB && (lineA >= lengthA || modifiedB[lineB])) lineB++;

            if (startA < lineA || startB < lineB)
                edits.Add(new Edit(startA, lineA - startA, startB, lineB - startB));
        }

        return edits;
    }
}
