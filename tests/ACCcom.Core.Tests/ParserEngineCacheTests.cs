using ACCcom.Core.Services;
using Xunit;

namespace ACCcom.Core.Tests;

/// <summary>
/// parse-raw with an explicit parserName used to compile a fresh Roslyn script
/// per request (hundreds of milliseconds) and throw it away.
/// <see cref="ParserEngineCache"/> keeps engines alive keyed by the .csx file's
/// last-write time, so hot reload still invalidates. Extracted from HttpService
/// (which needs a running EmbedIO server to construct) precisely so this logic
/// is testable in isolation.
/// </summary>
public class ParserEngineCacheTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("pe-cache").FullName;

    /// <summary>A trivial but real script — Load must succeed for the cache to
    /// keep an entry, so this cannot be a no-op string. (The script body is an
    /// expression returning List&lt;FieldAnnotation&gt; with
    /// System.Collections.Generic + ACCcom.Core.Models pre-imported.)</summary>
    private const string Script = "var result = new List<FieldAnnotation>();\nreturn result;\n";

    private string WriteParser(string name)
    {
        var path = Path.Combine(_dir, name + ".csx");
        File.WriteAllText(path, Script);
        return path;
    }

    [Fact]
    public void Second_request_for_the_same_parser_reuses_the_engine()
    {
        var path = WriteParser("p1");
        using var cache = new ParserEngineCache(8);

        var first = cache.GetOrLoad("p1", path, out var err1);
        var second = cache.GetOrLoad("p1", path, out var err2);

        Assert.Null(err1);
        Assert.Null(err2);
        Assert.NotNull(first);
        Assert.Same(first, second); // 第二次不新建引擎
        Assert.Equal(1, cache.Count);
    }

    [Fact]
    public void Editing_the_script_invalidates_the_entry()
    {
        var path = WriteParser("p1");
        using var cache = new ParserEngineCache(8);

        var first = cache.GetOrLoad("p1", path, out _);

        // 热重载必须仍然生效：改文件（并确保 mtime 变化）
        File.WriteAllText(path, Script + "\n// touched\n");
        File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddSeconds(5));

        var second = cache.GetOrLoad("p1", path, out var err);

        Assert.Null(err);
        Assert.NotNull(second);
        Assert.NotSame(first, second); // 换了新引擎
        Assert.Equal(1, cache.Count);
    }

    [Fact]
    public void A_load_failure_reports_the_error_and_caches_nothing()
    {
        var path = Path.Combine(_dir, "broken.csx");
        File.WriteAllText(path, "this is not valid C# @@@");

        using var cache = new ParserEngineCache(8);
        var engine = cache.GetOrLoad("broken", path, out var err);

        Assert.Null(engine);
        Assert.NotNull(err);
        Assert.Equal(0, cache.Count);
    }

    [Fact]
    public void Cache_is_bounded_and_evicts_the_oldest_entry()
    {
        using var cache = new ParserEngineCache(2);

        var a = cache.GetOrLoad("a", WriteParser("a"), out _);
        var b = cache.GetOrLoad("b", WriteParser("b"), out _);
        var c = cache.GetOrLoad("c", WriteParser("c"), out _);

        Assert.Equal(2, cache.Count);
        Assert.NotNull(a); Assert.NotNull(b); Assert.NotNull(c);
        // 被淘汰的引擎已 Dispose；再次请求 a 会拿到新实例
        var aAgain = cache.GetOrLoad("a", WriteParser("a"), out _);
        Assert.NotSame(a, aAgain);
    }

    [Fact]
    public void A_file_that_vanishes_between_resolve_and_read_reports_an_error()
    {
        // The caller resolves the path (File.Exists passes) and then the file is
        // gone: the old HttpService code let that IOException escape the
        // (fields, error) contract entirely.
        var path = WriteParser("gone");
        using var cache = new ParserEngineCache(8);

        File.Delete(path);
        var engine = cache.GetOrLoad("gone", path, out var err);

        Assert.Null(engine);
        Assert.NotNull(err);
        Assert.Equal(0, cache.Count);
    }

    [Fact]
    public void Concurrent_requests_share_one_engine_per_parser()
    {
        var path = WriteParser("shared");
        using var cache = new ParserEngineCache(8);

        var engines = new System.Collections.Concurrent.ConcurrentBag<ParserEngine>();
        Parallel.For(0, 8, _ =>
        {
            for (int i = 0; i < 10; i++)
            {
                var engine = cache.GetOrLoad("shared", path, out var err);
                Assert.Null(err);
                engines.Add(engine!);
            }
        });

        Assert.Equal(80, engines.Count);
        Assert.Single(engines.Distinct());
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { /* best effort */ }
        GC.SuppressFinalize(this);
    }
}
