using ACCcom.Core.Models;

namespace ACCcom.Core.Services;

/// <summary>
/// Positional line-by-line diff of two text files. Produces the display rows for
/// the CompareWindow (line-numbered, mismatch flagged) and the match counts.
/// Pure computation with no UI dependencies so it can run off the UI thread and
/// be unit-tested.
/// </summary>
public static class DiffEngine
{
    public static (List<DiffRow> RowsA, List<DiffRow> RowsB, int Matching, int Different) BuildDiff(string[] linesA, string[] linesB)
    {
        int maxCount = Math.Max(linesA.Length, linesB.Length);
        var rowsA = new List<DiffRow>(maxCount);
        var rowsB = new List<DiffRow>(maxCount);
        int matching = 0, different = 0;

        for (int i = 0; i < maxCount; i++)
        {
            var a = i < linesA.Length ? linesA[i] : "";
            var b = i < linesB.Length ? linesB[i] : "";

            bool same = string.Equals(a, b, StringComparison.Ordinal);
            if (same) matching++; else different++;

            rowsA.Add(new DiffRow($"[{i + 1}] {a}", !same));
            rowsB.Add(new DiffRow($"[{i + 1}] {b}", !same));
        }

        return (rowsA, rowsB, matching, different);
    }
}
