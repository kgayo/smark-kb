using System.Net;
using System.Text;
using System.Text.Json;
using SmartKb.Contracts;
using SmartKb.Contracts.Models;

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

    // --- FetchResult.Error factory tests ---

    [Fact]
    public void FetchResult_Error_ReturnsEmptyRecordsWithSingleError()
    {
        var result = FetchResult.Error("Something went wrong.");

        Assert.Empty(result.Records);
        Assert.Equal(0, result.FailedRecords);
        Assert.Single(result.Errors);
        Assert.Equal("Something went wrong.", result.Errors[0]);
        Assert.False(result.HasMore);
        Assert.Null(result.NewCheckpoint);
    }

    // --- ComputeHash tests ---

    [Fact]
    public void ComputeHash_DeterministicOutput()
    {
        var hash1 = ConnectorHttpHelper.ComputeHash("test input");
        var hash2 = ConnectorHttpHelper.ComputeHash("test input");
        Assert.Equal(hash1, hash2);
    }

    [Fact]
    public void ComputeHash_DifferentInputs_DifferentHashes()
    {
        var hash1 = ConnectorHttpHelper.ComputeHash("input A");
        var hash2 = ConnectorHttpHelper.ComputeHash("input B");
        Assert.NotEqual(hash1, hash2);
    }

    [Fact]
    public void ComputeHash_ReturnsLowercaseHex()
    {
        var hash = ConnectorHttpHelper.ComputeHash("test");
        Assert.Matches("^[0-9a-f]+$", hash);
        Assert.Equal(64, hash.Length); // SHA256 = 32 bytes = 64 hex chars
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

    // --- ConfigureBearerClient tests ---

    [Fact]
    public void ConfigureBearerClient_SetsBaseAddress()
    {
        using var client = new HttpClient();
        ConnectorHttpHelper.ConfigureBearerClient(client, "https://api.example.com", "token123");

        Assert.Equal(new Uri("https://api.example.com/"), client.BaseAddress);
    }

    [Fact]
    public void ConfigureBearerClient_TrimsTrailingSlash()
    {
        using var client = new HttpClient();
        ConnectorHttpHelper.ConfigureBearerClient(client, "https://api.example.com/", "token123");

        Assert.Equal(new Uri("https://api.example.com/"), client.BaseAddress);
    }

    [Fact]
    public void ConfigureBearerClient_SetsBearerAuth()
    {
        using var client = new HttpClient();
        ConnectorHttpHelper.ConfigureBearerClient(client, "https://api.example.com", "my-token");

        Assert.Equal("Bearer", client.DefaultRequestHeaders.Authorization?.Scheme);
        Assert.Equal("my-token", client.DefaultRequestHeaders.Authorization?.Parameter);
    }

    [Fact]
    public void ConfigureBearerClient_SetsJsonAcceptHeader()
    {
        using var client = new HttpClient();
        ConnectorHttpHelper.ConfigureBearerClient(client, "https://api.example.com", "token");

        Assert.Contains(client.DefaultRequestHeaders.Accept,
            h => h.MediaType == "application/json");
    }

    // --- ConfigureBasicClient tests ---

    [Fact]
    public void ConfigureBasicClient_SetsBaseAddress()
    {
        using var client = new HttpClient();
        ConnectorHttpHelper.ConfigureBasicClient(client, "https://dev.azure.com/org", "pat123");

        Assert.Equal(new Uri("https://dev.azure.com/org/"), client.BaseAddress);
    }

    [Fact]
    public void ConfigureBasicClient_SetsBasicAuthWithEncodedPat()
    {
        using var client = new HttpClient();
        ConnectorHttpHelper.ConfigureBasicClient(client, "https://dev.azure.com/org", "my-pat");

        Assert.Equal("Basic", client.DefaultRequestHeaders.Authorization?.Scheme);
        var decoded = Encoding.ASCII.GetString(
            Convert.FromBase64String(client.DefaultRequestHeaders.Authorization!.Parameter!));
        Assert.Equal(":my-pat", decoded);
    }

    [Fact]
    public void ConfigureBasicClient_SetsJsonAcceptHeader()
    {
        using var client = new HttpClient();
        ConnectorHttpHelper.ConfigureBasicClient(client, "https://dev.azure.com/org", "pat");

        Assert.Contains(client.DefaultRequestHeaders.Accept,
            h => h.MediaType == "application/json");
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
