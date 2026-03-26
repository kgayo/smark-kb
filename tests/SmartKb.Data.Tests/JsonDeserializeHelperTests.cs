using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace SmartKb.Data.Tests;

public sealed class JsonDeserializeHelperTests
{
    private static readonly JsonSerializerOptions CamelCase = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly ILogger _logger = NullLogger.Instance;

    // --- Deserialize<T> tests ---

    [Fact]
    public void Deserialize_NullInput_ReturnsFallback()
    {
        var result = JsonDeserializeHelper.Deserialize<List<string>>(null, CamelCase, _logger, []);
        Assert.Empty(result);
    }

    [Fact]
    public void Deserialize_EmptyInput_ReturnsFallback()
    {
        var result = JsonDeserializeHelper.Deserialize<List<string>>("", CamelCase, _logger, []);
        Assert.Empty(result);
    }

    [Fact]
    public void Deserialize_ValidJson_ReturnsList()
    {
        var result = JsonDeserializeHelper.Deserialize<List<string>>("[\"a\",\"b\"]", CamelCase, _logger, []);
        Assert.Equal(["a", "b"], result);
    }

    [Fact]
    public void Deserialize_MalformedJson_ReturnsFallbackAndLogs()
    {
        var capturingLogger = new CapturingLogger();
        var result = JsonDeserializeHelper.Deserialize<List<string>>("not json", CamelCase, capturingLogger, []);

        Assert.Empty(result);
        Assert.Single(capturingLogger.Entries);
        Assert.Equal(LogLevel.Warning, capturingLogger.Entries[0].Level);
    }

    [Fact]
    public void Deserialize_NullJsonValue_ReturnsFallback()
    {
        var result = JsonDeserializeHelper.Deserialize<List<string>>("null", CamelCase, _logger, []);
        Assert.Empty(result);
    }

    [Fact]
    public void Deserialize_ComplexType_Works()
    {
        var json = "[{\"name\":\"test\",\"value\":42}]";
        var result = JsonDeserializeHelper.Deserialize<List<TestItem>>(json, CamelCase, _logger, []);

        Assert.Single(result);
        Assert.Equal("test", result[0].Name);
        Assert.Equal(42, result[0].Value);
    }

    [Fact]
    public void Deserialize_NullOptions_UsesDefaults()
    {
        var json = "[\"x\"]";
        var result = JsonDeserializeHelper.Deserialize<List<string>>(json, null, _logger, []);
        Assert.Equal(["x"], result);
    }

    [Fact]
    public void Deserialize_CallerMemberName_IncludedInLogMessage()
    {
        var capturingLogger = new CapturingLogger();
        JsonDeserializeHelper.Deserialize<List<string>>("bad", CamelCase, capturingLogger, []);

        Assert.Contains("Deserialize_CallerMemberName_IncludedInLogMessage", capturingLogger.Entries[0].Message);
    }

    // --- DeserializeOrNull<T> tests ---

    [Fact]
    public void DeserializeOrNull_NullInput_ReturnsNull()
    {
        var result = JsonDeserializeHelper.DeserializeOrNull<List<string>>(null, CamelCase, _logger);
        Assert.Null(result);
    }

    [Fact]
    public void DeserializeOrNull_EmptyInput_ReturnsNull()
    {
        var result = JsonDeserializeHelper.DeserializeOrNull<List<string>>("", CamelCase, _logger);
        Assert.Null(result);
    }

    [Fact]
    public void DeserializeOrNull_ValidJson_ReturnsObject()
    {
        var result = JsonDeserializeHelper.DeserializeOrNull<List<string>>("[\"a\"]", CamelCase, _logger);
        Assert.NotNull(result);
        Assert.Equal(["a"], result);
    }

    [Fact]
    public void DeserializeOrNull_MalformedJson_ReturnsNullAndLogs()
    {
        var capturingLogger = new CapturingLogger();
        var result = JsonDeserializeHelper.DeserializeOrNull<List<string>>("{{bad", CamelCase, capturingLogger);

        Assert.Null(result);
        Assert.Single(capturingLogger.Entries);
        Assert.Equal(LogLevel.Warning, capturingLogger.Entries[0].Level);
    }

    // --- Nullable logger tests ---

    [Fact]
    public void Deserialize_NullLogger_ReturnsFallbackOnMalformedJson()
    {
        var result = JsonDeserializeHelper.Deserialize<List<string>>("bad json", CamelCase, null, []);
        Assert.Empty(result);
    }

    [Fact]
    public void DeserializeOrNull_NullLogger_ReturnsNullOnMalformedJson()
    {
        var result = JsonDeserializeHelper.DeserializeOrNull<List<string>>("bad json", CamelCase, null);
        Assert.Null(result);
    }

    // --- DeserializeStringList tests ---

    [Fact]
    public void DeserializeStringList_ValidJson_ReturnsList()
    {
        var result = JsonDeserializeHelper.DeserializeStringList("[\"alpha\",\"beta\"]");
        Assert.Equal(["alpha", "beta"], result);
    }

    [Fact]
    public void DeserializeStringList_NullInput_ReturnsEmptyList()
    {
        var result = JsonDeserializeHelper.DeserializeStringList(null);
        Assert.Empty(result);
    }

    [Fact]
    public void DeserializeStringList_MalformedJson_ReturnsEmptyAndLogs()
    {
        var capturingLogger = new CapturingLogger();
        var result = JsonDeserializeHelper.DeserializeStringList("not json", capturingLogger);

        Assert.Empty(result);
        Assert.Single(capturingLogger.Entries);
        Assert.Equal(LogLevel.Warning, capturingLogger.Entries[0].Level);
    }

    // --- DeserializeStringListCaseInsensitive tests ---

    [Fact]
    public void DeserializeStringListCaseInsensitive_ValidJson_ReturnsList()
    {
        var result = JsonDeserializeHelper.DeserializeStringListCaseInsensitive("[\"x\",\"y\"]");
        Assert.Equal(["x", "y"], result);
    }

    [Fact]
    public void DeserializeStringListCaseInsensitive_NullInput_ReturnsEmptyList()
    {
        var result = JsonDeserializeHelper.DeserializeStringListCaseInsensitive(null);
        Assert.Empty(result);
    }

    // --- DeserializeStringDictionary tests ---

    [Fact]
    public void DeserializeStringDictionary_ValidJson_ReturnsDictionary()
    {
        var result = JsonDeserializeHelper.DeserializeStringDictionary("{\"key1\":\"val1\",\"key2\":null}");
        Assert.Equal(2, result.Count);
        Assert.Equal("val1", result["key1"]);
        Assert.Null(result["key2"]);
    }

    [Fact]
    public void DeserializeStringDictionary_NullInput_ReturnsEmptyDictionary()
    {
        var result = JsonDeserializeHelper.DeserializeStringDictionary(null);
        Assert.Empty(result);
    }

    [Fact]
    public void DeserializeStringDictionary_MalformedJson_ReturnsEmptyAndLogs()
    {
        var capturingLogger = new CapturingLogger();
        var result = JsonDeserializeHelper.DeserializeStringDictionary("{bad}", capturingLogger);

        Assert.Empty(result);
        Assert.Single(capturingLogger.Entries);
        Assert.Equal(LogLevel.Warning, capturingLogger.Entries[0].Level);
    }

    // --- Test helpers ---

    private sealed record TestItem(string Name, int Value);

    private sealed class CapturingLogger : ILogger
    {
        public List<(LogLevel Level, string Message)> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            Entries.Add((logLevel, formatter(state, exception)));
        }
    }
}
