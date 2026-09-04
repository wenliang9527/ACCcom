using System;
using ACCcom.Core.Services;
using Xunit;

namespace ACCcom.Core.Tests;

public class DiffEngineTests
{
    [Fact]
    public void Equal_lines_all_match_with_zero_differences()
    {
        var a = new[] { "line1", "line2", "line3" };
        var b = new[] { "line1", "line2", "line3" };

        var (rowsA, rowsB, matching, different) = DiffEngine.BuildDiff(a, b);

        Assert.Equal(3, rowsA.Count);
        Assert.Equal(3, rowsB.Count);
        Assert.Equal(3, matching);
        Assert.Equal(0, different);
        Assert.All(rowsA, r => Assert.False(r.IsDiff));
        Assert.All(rowsB, r => Assert.False(r.IsDiff));
    }

    [Fact]
    public void Differing_lines_flagged_and_counted()
    {
        var a = new[] { "same", "aaa", "same2" };
        var b = new[] { "same", "bbb", "same2" };

        var (rowsA, rowsB, matching, different) = DiffEngine.BuildDiff(a, b);

        Assert.Equal(2, matching);
        Assert.Equal(1, different);
        Assert.True(rowsA[1].IsDiff);
        Assert.True(rowsB[1].IsDiff);
        Assert.Equal("[2] aaa", rowsA[1].Display);
        Assert.Equal("[2] bbb", rowsB[1].Display);
    }

    [Fact]
    public void Unequal_lengths_pad_shorter_side_with_empty()
    {
        var a = new[] { "x", "y", "z" };
        var b = new[] { "x" };

        var (rowsA, rowsB, matching, different) = DiffEngine.BuildDiff(a, b);

        Assert.Equal(3, rowsA.Count);
        Assert.Equal(3, rowsB.Count);
        Assert.Equal(1, matching);
        Assert.Equal(2, different);
        Assert.Equal("[2] ", rowsB[1].Display);
        Assert.True(rowsB[2].IsDiff);
    }

    [Fact]
    public void Empty_inputs_produce_zero_rows()
    {
        var (rowsA, rowsB, matching, different) = DiffEngine.BuildDiff(Array.Empty<string>(), Array.Empty<string>());

        Assert.Empty(rowsA);
        Assert.Empty(rowsB);
        Assert.Equal(0, matching);
        Assert.Equal(0, different);
    }

    [Fact]
    public void One_sided_empty_input_pads_with_empty_lines()
    {
        var (rowsA, rowsB, matching, different) = DiffEngine.BuildDiff(new[] { "a" }, Array.Empty<string>());

        Assert.Single(rowsA);
        Assert.Single(rowsB);
        Assert.Equal(0, matching);
        Assert.Equal(1, different);
        Assert.True(rowsB[0].IsDiff);
        Assert.Equal("[1] ", rowsB[0].Display);
    }

    [Fact]
    public void Line_numbers_are_one_based_with_prefix()
    {
        var a = new[] { "first" };
        var b = new[] { "other" };

        var (rowsA, rowsB, _, _) = DiffEngine.BuildDiff(a, b);

        Assert.Equal("[1] first", rowsA[0].Display);
        Assert.Equal("[1] other", rowsB[0].Display);
    }

    [Fact]
    public void Large_input_completes_quickly()
    {
        const int n = 50_000;
        var a = new string[n];
        var b = new string[n];
        for (int i = 0; i < n; i++)
        {
            a[i] = $"line{i}";
            b[i] = $"line{i}";
        }

        var sw = System.Diagnostics.Stopwatch.StartNew();
        var (rowsA, rowsB, matching, different) = DiffEngine.BuildDiff(a, b);
        sw.Stop();

        Assert.Equal(n, rowsA.Count);
        Assert.Equal(n, rowsB.Count);
        Assert.Equal(n, matching);
        Assert.Equal(0, different);
        Assert.True(sw.ElapsedMilliseconds < 2000, $"took {sw.ElapsedMilliseconds}ms");
    }
}
