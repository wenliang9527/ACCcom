using System.Text.RegularExpressions;
using Microsoft.Extensions.Caching.Memory;
using ACCcom.Core.Models;

namespace ACCcom.Core.Services;

/// <summary>
/// 模式匹配工具类，提供统一的匹配逻辑，支持正则表达式缓存
/// </summary>
public static class PatternMatcher
{
    private static readonly MemoryCache _regexCache = new(new MemoryCacheOptions
    {
        SizeLimit = 100,
        CompactionPercentage = 0.25
    });

    /// <summary>
    /// 检查日志条目是否匹配指定模式
    /// </summary>
    public static bool Matches(LogEntry entry, string pattern, string matchMode, bool matchHex, string? direction = null)
    {
        if (!string.IsNullOrEmpty(direction) &&
            !string.Equals(entry.Direction, direction, StringComparison.OrdinalIgnoreCase))
            return false;

        var target = matchHex ? entry.RawHex : entry.Text;
        if (string.IsNullOrEmpty(target))
            return false;

        return MatchesPattern(target, pattern, matchMode);
    }

    /// <summary>
    /// 检查目标字符串是否匹配指定模式
    /// </summary>
    public static bool MatchesPattern(string target, string pattern, string matchMode)
    {
        // OrdinalIgnoreCase comparisons instead of matchMode.ToLowerInvariant() switch:
        // this runs per packet per rule (triggers, waiters) and ToLowerInvariant
        // allocated a fresh string on every call.
        if (matchMode.Equals("exact", StringComparison.OrdinalIgnoreCase))
            return string.Equals(target, pattern, StringComparison.OrdinalIgnoreCase);
        if (matchMode.Equals("regex", StringComparison.OrdinalIgnoreCase))
            return TryRegexMatch(target, pattern);
        return target.Contains(pattern, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// 尝试正则表达式匹配，使用缓存提升性能
    /// </summary>
    public static bool TryRegexMatch(string input, string pattern)
    {
        var regex = GetOrCompileRegex(pattern);
        if (regex == null)
            return false;

        try
        {
            return regex.IsMatch(input);
        }
        catch (RegexMatchTimeoutException)
        {
            return false;
        }
    }

    /// <summary>
    /// 获取或编译正则表达式（带缓存）
    /// </summary>
    private static Regex? GetOrCompileRegex(string pattern)
    {
        var cacheKey = "regex_" + pattern;

        if (_regexCache.TryGetValue(cacheKey, out Regex? cached))
            return cached;

        Regex? regex;
        try
        {
            regex = new Regex(pattern, RegexOptions.IgnoreCase | RegexOptions.Compiled);
        }
        catch (ArgumentException)
        {
            // 缓存负结果，避免对无效模式的重复编译尝试
            var failOptions = new MemoryCacheEntryOptions()
                .SetSize(1)
                .SetSlidingExpiration(TimeSpan.FromMinutes(5))
                .SetPriority(CacheItemPriority.Low);
            _regexCache.Set(cacheKey, (Regex?)null, failOptions);
            return null;
        }

        var options = new MemoryCacheEntryOptions()
            .SetSize(1)
            .SetSlidingExpiration(TimeSpan.FromMinutes(30))
            .SetPriority(CacheItemPriority.Normal);

        _regexCache.Set(cacheKey, regex, options);
        return regex;
    }


}
