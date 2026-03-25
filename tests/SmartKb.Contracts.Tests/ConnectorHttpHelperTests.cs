using System.Net;
using System.Text;
using System.Text.Json;
using SmartKb.Contracts;

namespace SmartKb.Contracts.Tests;

public class ConnectorHttpHelperTests
{
    private static readonly JsonSerializerOptions CamelCase = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    [Fact]
    public async Task DeserializeAsync_ReturnsDeserializedObject()
    {
        var json = """{"name":"test","value":42}""";
        using var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        };

        var result = await ConnectorHttpHelper.DeserializeAsync<TestDto>(response, CamelCase, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal("test", result!.Name);
        Assert.Equal(42, result.Value);
    }

    [Fact]
    public async Task DeserializeAsync_ReturnsNull_ForNullJsonBody()
    {
        using var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("null", Encoding.UTF8, "application/json"),
        };

        var result = await ConnectorHttpHelper.DeserializeAsync<TestDto>(response, CamelCase, CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public async Task DeserializeAsync_Throws_ForMalformedJson()
    {
        using var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("{invalid", Encoding.UTF8, "application/json"),
        };

        await Assert.ThrowsAsync<JsonException>(
            () => ConnectorHttpHelper.DeserializeAsync<TestDto>(response, CamelCase, CancellationToken.None));
    }

    [Fact]
    public async Task DeserializeAsync_RespectsJsonSerializerOptions()
    {
        var json = """{"Name":"PascalCase","Value":99}""";
        using var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        };

        // CamelCase policy won't match PascalCase property names unless case-insensitive
        var strictCamel = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
        var result = await ConnectorHttpHelper.DeserializeAsync<TestDto>(response, strictCamel, CancellationToken.None);

        Assert.NotNull(result);
        // Default JsonSerializer is case-sensitive for property names when no PropertyNameCaseInsensitive is set
        Assert.Null(result!.Name);
        Assert.Equal(0, result.Value);
    }

    [Fact]
    public async Task DeserializeAsync_PropagatesCancellation()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        using var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("{}", Encoding.UTF8, "application/json"),
        };

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => ConnectorHttpHelper.DeserializeAsync<TestDto>(response, CamelCase, cts.Token));
    }

    // --- ParseJson<T> tests ---

    [Fact]
    public void ParseJson_ReturnsNull_ForNullInput()
    {
        var result = ConnectorHttpHelper.ParseJson<TestDto>(null, CamelCase);
        Assert.Null(result);
    }

    [Fact]
    public void ParseJson_ReturnsNull_ForWhitespaceInput()
    {
        var result = ConnectorHttpHelper.ParseJson<TestDto>("   ", CamelCase);
        Assert.Null(result);
    }

    [Fact]
    public void ParseJson_ReturnsDeserializedObject()
    {
        var json = """{"name":"test","value":42}""";
        var result = ConnectorHttpHelper.ParseJson<TestDto>(json, CamelCase);

        Assert.NotNull(result);
        Assert.Equal("test", result!.Name);
        Assert.Equal(42, result.Value);
    }

    [Fact]
    public void ParseJson_ReturnsNull_ForMalformedJson()
    {
        var result = ConnectorHttpHelper.ParseJson<TestDto>("{invalid", CamelCase);
        Assert.Null(result);
    }

    [Fact]
    public void ParseJson_LogsWarning_ForMalformedJson()
    {
        var logger = new CapturingLogger();
        ConnectorHttpHelper.ParseJson<TestDto>("{invalid", CamelCase, logger);

        Assert.Single(logger.Entries);
        Assert.Equal(Microsoft.Extensions.Logging.LogLevel.Warning, logger.Entries[0].Level);
        Assert.Contains("TestDto", logger.Entries[0].Message);
    }

    [Fact]
    public void ParseJson_RespectsOptions()
    {
        var json = """{"Name":"PascalCase","Value":99}""";
        // CamelCase policy won't match PascalCase property names
        var result = ConnectorHttpHelper.ParseJson<TestDto>(json, CamelCase);

        Assert.NotNull(result);
        Assert.Null(result!.Name);
        Assert.Equal(0, result.Value);
    }

    [Fact]
    public void ParseJson_NoLogger_DoesNotThrow_ForMalformedJson()
    {
        var result = ConnectorHttpHelper.ParseJson<TestDto>("{invalid", CamelCase, logger: null);
        Assert.Null(result);
    }

    private sealed class TestDto
    {
        public string? Name { get; set; }
        public int Value { get; set; }
    }

    private sealed class CapturingLogger : Microsoft.Extensions.Logging.ILogger
    {
        public List<(Microsoft.Extensions.Logging.LogLevel Level, string Message)> Entries { get; } = new();

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
