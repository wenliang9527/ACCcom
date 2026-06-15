using System.Text.RegularExpressions;
using ACCcom.Core.Models;

namespace ACCcom.Core.Services;

/// <summary>
/// 模式匹配工具类，提供统一的匹配逻辑
/// </summary>
public static class PatternMatcher
{
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
        return matchMode.ToLowerInvariant() switch
        {
            "exact" => string.Equals(target, pattern, StringComparison.OrdinalIgnoreCase),
            "regex" => TryRegexMatch(target, pattern),
            _ => target.Contains(pattern, StringComparison.OrdinalIgnoreCase)
        };
    }

    /// <summary>
    /// 尝试正则表达式匹配
    /// </summary>
    public static bool TryRegexMatch(string input, string pattern)
    {
        try
        {
            return Regex.IsMatch(input, pattern, RegexOptions.IgnoreCase);
        }
        catch (RegexParseException)
        {
            return false;
        }
    }
}
