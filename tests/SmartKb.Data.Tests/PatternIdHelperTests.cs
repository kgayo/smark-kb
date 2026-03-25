using SmartKb.Data;

namespace SmartKb.Data.Tests;

public class PatternIdHelperTests
{
    [Fact]
    public void ExtractPatternIds_NullInput_ReturnsEmpty()
    {
        Assert.Empty(PatternIdHelper.ExtractPatternIds(null));
    }

    [Fact]
    public void ExtractPatternIds_EmptyString_ReturnsEmpty()
    {
        Assert.Empty(PatternIdHelper.ExtractPatternIds(""));
    }

    [Fact]
    public void ExtractPatternIds_EmptyArray_ReturnsEmpty()
    {
        Assert.Empty(PatternIdHelper.ExtractPatternIds("[]"));
    }

    [Fact]
    public void ExtractPatternIds_NoPatternIds_ReturnsEmpty()
    {
        var json = """["chunk-001", "chunk-002", "evidence-003"]""";
        Assert.Empty(PatternIdHelper.ExtractPatternIds(json));
    }

    [Fact]
    public void ExtractPatternIds_ExtractsPatternPrefixedIds()
    {
        var json = """["pattern-abc", "chunk-001", "pattern-def"]""";
        var result = PatternIdHelper.ExtractPatternIds(json);

        Assert.Equal(2, result.Count);
        Assert.Contains("pattern-abc", result);
        Assert.Contains("pattern-def", result);
    }

    [Fact]
    public void ExtractPatternIds_CaseInsensitivePrefix()
    {
        var json = """["Pattern-ABC", "PATTERN-DEF", "pattern-ghi"]""";
        var result = PatternIdHelper.ExtractPatternIds(json);

        Assert.Equal(3, result.Count);
    }

    [Fact]
    public void ExtractPatternIds_CaseInsensitiveHashSet()
    {
        var json = """["pattern-ABC", "Pattern-abc"]""";
        var result = PatternIdHelper.ExtractPatternIds(json);

        // Both refer to the same pattern (case-insensitive), so HashSet should deduplicate
        Assert.Single(result);
    }

    [Fact]
    public void ExtractPatternIds_MalformedJson_ReturnsEmpty()
    {
        Assert.Empty(PatternIdHelper.ExtractPatternIds("not json at all"));
    }

    [Fact]
    public void ExtractPatternIds_MalformedJson_LogsWarning()
    {
        var logger = new CapturingLogger();
        PatternIdHelper.ExtractPatternIds("{invalid}", logger);

        Assert.Single(logger.Entries);
        Assert.Equal(Microsoft.Extensions.Logging.LogLevel.Warning, logger.Entries[0].Level);
        Assert.Contains("ExtractPatternIds", logger.Entries[0].Message);
    }

    [Fact]
    public void ExtractPatternIds_NullLogger_DoesNotThrow()
    {
        // Malformed JSON with null logger should not throw
        var result = PatternIdHelper.ExtractPatternIds("{invalid}", logger: null);
        Assert.Empty(result);
    }

    private sealed class CapturingLogger : Microsoft.Extensions.Logging.ILogger
    {
        public List<(Microsoft.Extensions.Logging.LogLevel Level, string Message)> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(Microsoft.Extensions.Logging.LogLevel logLevel) => true;

        public void Log<TState>(
            Microsoft.Extensions.Logging.LogLevel logLevel,
            Microsoft.Extensions.Logging.EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            Entries.Add((logLevel, formatter(state, exception)));
        }
    }
}
