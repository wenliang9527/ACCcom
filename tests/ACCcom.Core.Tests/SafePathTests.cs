using ACCcom.Core.Services;
using Xunit;

namespace ACCcom.Core.Tests;

public class SafePathTests
{
    private static readonly string BaseDir = Path.Combine(Path.GetTempPath(), "acccom_safepath_test");

    [Fact]
    public void TryCombineUnder_RejectsParentTraversal()
    {
        Assert.False(SafePath.TryCombineUnder(BaseDir, "..\\evil.csx", out _));
        Assert.False(SafePath.TryCombineUnder(BaseDir, "../evil.csx", out _));
        Assert.False(SafePath.TryCombineUnder(BaseDir, "sub/../../evil.csx", out _));
    }

    [Fact]
    public void TryCombineUnder_RejectsRootedAndAbsolutePaths()
    {
        Assert.False(SafePath.TryCombineUnder(BaseDir, "C:\\Windows\\evil.csx", out _));
        Assert.False(SafePath.TryCombineUnder(BaseDir, "/etc/passwd", out _));
        Assert.False(SafePath.TryCombineUnder(BaseDir, "\\evil.csx", out _));
    }

    [Fact]
    public void TryCombineUnder_RejectsEmptyAndDotSegments()
    {
        Assert.False(SafePath.TryCombineUnder(BaseDir, "", out _));
        Assert.False(SafePath.TryCombineUnder(BaseDir, "   ", out _));
        Assert.False(SafePath.TryCombineUnder(BaseDir, ".", out _));
        Assert.False(SafePath.TryCombineUnder(BaseDir, "..", out _));
        Assert.False(SafePath.TryCombineUnder("", "ok.csx", out _));
    }

    [Fact]
    public void TryCombineUnder_RejectsInvalidFileNameChars()
    {
        Assert.False(SafePath.TryCombineUnder(BaseDir, "bad<name>.csx", out _));
        Assert.False(SafePath.TryCombineUnder(BaseDir, "bad:name.csx", out _));
        Assert.False(SafePath.TryCombineUnder(BaseDir, "bad\"name.csx", out _));
    }

    [Fact]
    public void TryCombineUnder_AcceptsPlainFileName_AndStaysUnderBase()
    {
        Assert.True(SafePath.TryCombineUnder(BaseDir, "myparser.csx", out var path));
        Assert.StartsWith(Path.GetFullPath(BaseDir) + Path.DirectorySeparatorChar, path);
        Assert.Equal("myparser.csx", Path.GetFileName(path));
    }

    [Fact]
    public void TryCombineUnder_AcceptsSubdirectoryName_WhenPlainNameIncludesDot()
    {
        // A plain name may contain dots but no separators; it must still resolve
        // strictly under the base directory.
        Assert.True(SafePath.TryCombineUnder(BaseDir, "v1.2.csx", out var path));
        Assert.StartsWith(Path.GetFullPath(BaseDir) + Path.DirectorySeparatorChar, path);
    }

    [Fact]
    public void IsPlainFileName_RejectsAllTraversalSyntax()
    {
        Assert.False(SafePath.IsPlainFileName("..\\evil"));
        Assert.False(SafePath.IsPlainFileName("../evil"));
        Assert.False(SafePath.IsPlainFileName("a/b"));
        Assert.False(SafePath.IsPlainFileName("c:\\x"));
        Assert.False(SafePath.IsPlainFileName(".."));
        Assert.False(SafePath.IsPlainFileName("a\0b"));
    }

    [Fact]
    public void IsPlainFileName_AcceptsNormalNames()
    {
        Assert.True(SafePath.IsPlainFileName("parser.csx"));
        Assert.True(SafePath.IsPlainFileName("session_20260905.jsonl"));
        Assert.True(SafePath.IsPlainFileName("my parser v2.csx"));
    }
}