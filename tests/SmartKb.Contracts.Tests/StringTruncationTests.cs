namespace SmartKb.Contracts.Tests;

public class StringTruncationTests
{
    // --- TruncationLimits ---

    [Fact]
    public void TruncationLimits_HasExpectedValues()
    {
        Assert.Equal(4000, TruncationLimits.ErrorDetail);
        Assert.Equal(500, TruncationLimits.DiagnosticBody);
        Assert.Equal(200, TruncationLimits.ErrorBodyShort);
        Assert.Equal(200, TruncationLimits.SnippetPreview);
        Assert.Equal(100, TruncationLimits.SessionTitle);
    }

    // --- Truncate extension ---

    [Fact]
    public void Truncate_ShortString_ReturnsOriginal()
    {
        var input = "hello";
        Assert.Equal("hello", input.Truncate(10));
    }

    [Fact]
    public void Truncate_ExactLength_ReturnsOriginal()
    {
        var input = "hello";
        Assert.Equal("hello", input.Truncate(5));
    }

    [Fact]
    public void Truncate_LongString_TruncatesToMaxLength()
    {
        var input = "hello world";
        Assert.Equal("hello", input.Truncate(5));
    }

    [Fact]
    public void Truncate_WithSuffix_AppendsSuffix()
    {
        var input = "hello world";
        Assert.Equal("hello...", input.Truncate(5, "..."));
    }

    [Fact]
    public void Truncate_WithSuffix_ShortString_NoSuffix()
    {
        var input = "hi";
        Assert.Equal("hi", input.Truncate(5, "..."));
    }

    [Fact]
    public void Truncate_EmptyString_ReturnsEmpty()
    {
        Assert.Equal("", "".Truncate(10));
    }

    [Fact]
    public void Truncate_NullString_ReturnsNull()
    {
        string? input = null;
        Assert.Null(input!.Truncate(10));
    }

    [Fact]
    public void Truncate_ZeroMaxLength_ReturnsEmpty()
    {
        Assert.Equal("", "hello".Truncate(0));
    }

    [Fact]
    public void Truncate_ZeroMaxLength_WithSuffix_ReturnsSuffix()
    {
        Assert.Equal("...", "hello".Truncate(0, "..."));
    }
}
