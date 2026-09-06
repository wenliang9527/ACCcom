using ACCcom.Core.Services;
using Xunit;

namespace ACCcom.Core.Tests;

public class FileNameSanitizerTests
{
    [Fact]
    public void Sanitize_plain_name_unchanged()
    {
        Assert.Equal("ReadAnalog", FileNameSanitizer.Sanitize("ReadAnalog"));
    }

    [Fact]
    public void Sanitize_replaces_invalid_chars_with_underscore()
    {
        // '<', '>', ':', '"', '/', '\', '|', '?', '*' are invalid on Windows.
        Assert.Equal("a_b_c", FileNameSanitizer.Sanitize("a<b>c"));
        Assert.Equal("a_b_c", FileNameSanitizer.Sanitize("a|b:c"));
        Assert.Equal("a_b_c", FileNameSanitizer.Sanitize("a?b*c"));
        Assert.Equal("a_b", FileNameSanitizer.Sanitize("a\"b"));
    }

    [Fact]
    public void Sanitize_backslash_replaced()
    {
        Assert.Equal("a_b", FileNameSanitizer.Sanitize(@"a\b"));
    }

    [Fact]
    public void Sanitize_slash_replaced()
    {
        Assert.Equal("a_b", FileNameSanitizer.Sanitize("a/b"));
    }

    [Fact]
    public void Sanitize_null_returns_script()
    {
        Assert.Equal("script", FileNameSanitizer.Sanitize(null));
    }

    [Fact]
    public void Sanitize_whitespace_returns_script()
    {
        Assert.Equal("script", FileNameSanitizer.Sanitize("   "));
    }

    [Fact]
    public void Sanitize_all_invalid_returns_script()
    {
        // Everything becomes '_' — still not whitespace, so it's kept as-is.
        Assert.Equal("___", FileNameSanitizer.Sanitize("|?*"));
    }

    [Fact]
    public void Sanitize_keeps_spaces()
    {
        Assert.Equal("my script", FileNameSanitizer.Sanitize("my script"));
    }

    [Fact]
    public void Sanitize_leading_trailing_underscores_kept()
    {
        // No trimming — the caller appends ".json" and Path.Combine handles it.
        Assert.Equal("_a_", FileNameSanitizer.Sanitize("_a_"));
    }
}