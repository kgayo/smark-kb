using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using SmartKb.Contracts.Connectors;
using SmartKb.Contracts.Enums;
using SmartKb.Contracts.Models;
using SmartKb.Contracts.Services;

namespace SmartKb.Contracts.Tests.Connectors;

public class ClickUpExternalCreationTests
{
    [Fact]
    public async Task CreateExternalTask_ReturnsError_WhenInvalidConfig()
    {
        var client = CreateClient();
        var request = MakeRequest();

        var result = await client.CreateExternalWorkItemAsync("not-json", "token", request);

        Assert.False(result.Success);
        Assert.Contains("Invalid source configuration", result.ErrorDetail);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public async Task CreateExternalTask_ReturnsError_WhenNoSecret(string? secret)
    {
        var client = CreateClient();
        var config = CreateSourceConfigJson();
        var request = MakeRequest();

        var result = await client.CreateExternalWorkItemAsync(config, secret!, request);

        Assert.False(result.Success);
        Assert.Contains("No credentials", result.ErrorDetail);
    }

    [Fact]
    public async Task CreateExternalTask_Success_ReturnsIdAndUrl()
    {
        var responseBody = """{"id":"abc123","name":"Test Escalation","url":"https://app.clickup.com/t/abc123"}""";
        var handler = new MockHttpHandler(HttpStatusCode.OK, responseBody);
        var client = CreateClient(handler);
        var config = CreateSourceConfigJson(listIds: ["list-1"]);
        var request = MakeRequest(targetListId: "list-1");

        var result = await client.CreateExternalWorkItemAsync(config, "token", request);

        Assert.True(result.Success);
        Assert.Equal("abc123", result.ExternalId);
        Assert.Equal("https://app.clickup.com/t/abc123", result.ExternalUrl);
        Assert.Null(result.ErrorDetail);
    }

    [Fact]
    public async Task CreateExternalTask_Success_FallsBackToFirstConfiguredList()
    {
        var responseBody = """{"id":"xyz789","name":"Test","url":"https://app.clickup.com/t/xyz789"}""";
        var handler = new MockHttpHandler(HttpStatusCode.OK, responseBody);
        var client = CreateClient(handler);
        var config = CreateSourceConfigJson(listIds: ["fallback-list"]);
        // No TargetListId in request — should use "fallback-list" from config.
        var request = MakeRequest(targetListId: null);

        var result = await client.CreateExternalWorkItemAsync(config, "token", request);

        Assert.True(result.Success);
        Assert.Equal("xyz789", result.ExternalId);
    }

    [Fact]
    public async Task CreateExternalTask_Success_GeneratesUrlWhenMissing()
    {
        // Response without a URL — should generate one from the task ID.
        var responseBody = """{"id":"task999","name":"Test"}""";
        var handler = new MockHttpHandler(HttpStatusCode.OK, responseBody);
        var client = CreateClient(handler);
        var config = CreateSourceConfigJson(listIds: ["list-1"]);
        var request = MakeRequest(targetListId: "list-1");

        var result = await client.CreateExternalWorkItemAsync(config, "token", request);

        Assert.True(result.Success);
        Assert.Equal("task999", result.ExternalId);
        Assert.Equal("https://app.clickup.com/t/task999", result.ExternalUrl);
    }

    [Fact]
    public async Task CreateExternalTask_ApiFailure_ReturnsError()
    {
        var handler = new MockHttpHandler(HttpStatusCode.Forbidden, """{"err":"Unauthorized","ECODE":"OAUTH_025"}""");
        var client = CreateClient(handler);
        var config = CreateSourceConfigJson(listIds: ["list-1"]);
        var request = MakeRequest(targetListId: "list-1");

        var result = await client.CreateExternalWorkItemAsync(config, "token", request);

        Assert.False(result.Success);
        Assert.Contains("403", result.ErrorDetail);
        Assert.Null(result.ExternalId);
        Assert.Null(result.ExternalUrl);
    }

    [Theory]
    [InlineData("P1", 1)]
    [InlineData("P2", 2)]
    [InlineData("P3", 3)]
    [InlineData("P4", 4)]
    [InlineData("Unknown", 3)] // Default to 3
    public async Task CreateExternalTask_MapsSeverityToPriority(string severity, int expectedPriority)
    {
        var captureHandler = new CaptureRequestHandler(HttpStatusCode.OK,
            """{"id":"t1","name":"Test","url":"https://app.clickup.com/t/t1"}""");
        var client = CreateClient(captureHandler);
        var config = CreateSourceConfigJson(listIds: ["list-1"]);
        var request = new ExternalWorkItemRequest
        {
            Title = "Test",
            Description = "Desc",
            Severity = severity,
            TargetListId = "list-1",
        };

        var result = await client.CreateExternalWorkItemAsync(config, "token", request);

        Assert.True(result.Success);
        Assert.NotNull(captureHandler.LastRequestBody);
        Assert.Contains($"\"priority\":{expectedPriority}", captureHandler.LastRequestBody);
    }

    [Fact]
    public async Task CreateExternalTask_ConnectionError_ReturnsError()
    {
        var handler = new ThrowingHandler(new HttpRequestException("DNS resolution failed"));
        var client = CreateClient(handler);
        var config = CreateSourceConfigJson(listIds: ["list-1"]);
        var request = MakeRequest(targetListId: "list-1");

        var result = await client.CreateExternalWorkItemAsync(config, "token", request);

        Assert.False(result.Success);
        Assert.Contains("Connection error", result.ErrorDetail);
        Assert.Contains("DNS resolution failed", result.ErrorDetail);
    }

    // --- Helpers ---

    private static ExternalWorkItemRequest MakeRequest(string? targetListId = "list-1") => new()
    {
        Title = "Test Escalation Task",
        Description = "## Escalation: Test\n\nDescription content.",
        Severity = "P2",
        TargetListId = targetListId,
    };

    private static ClickUpConnectorClient CreateClient(HttpMessageHandler? handler = null)
    {
        var factory = new TestHttpClientFactory(handler ?? new MockHttpHandler(HttpStatusCode.OK, "{}"));
        var logger = new LoggerFactory().CreateLogger<ClickUpConnectorClient>();
        return new ClickUpConnectorClient(factory, logger);
    }

    private static string CreateSourceConfigJson(
        string workspaceId = "12345",
        IReadOnlyList<string>? listIds = null)
    {
        return JsonSerializer.Serialize(new ClickUpSourceConfig
        {
            WorkspaceId = workspaceId,
            ListIds = listIds ?? [],
        }, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
    }
}
