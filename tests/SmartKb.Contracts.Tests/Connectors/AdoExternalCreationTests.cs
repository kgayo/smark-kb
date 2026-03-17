using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using SmartKb.Contracts.Connectors;
using SmartKb.Contracts.Enums;
using SmartKb.Contracts.Models;
using SmartKb.Contracts.Services;

namespace SmartKb.Contracts.Tests.Connectors;

public class AdoExternalCreationTests
{
    [Fact]
    public async Task CreateExternalWorkItem_ReturnsError_WhenInvalidConfig()
    {
        var client = CreateClient();
        var request = MakeRequest();

        var result = await client.CreateExternalWorkItemAsync("not-json", "pat-token", request);

        Assert.False(result.Success);
        Assert.Contains("Invalid source configuration", result.ErrorDetail);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public async Task CreateExternalWorkItem_ReturnsError_WhenNoSecret(string? secret)
    {
        var client = CreateClient();
        var config = CreateSourceConfigJson();
        var request = MakeRequest();

        var result = await client.CreateExternalWorkItemAsync(config, secret!, request);

        Assert.False(result.Success);
        Assert.Contains("No credentials", result.ErrorDetail);
    }

    [Fact]
    public async Task CreateExternalWorkItem_ReturnsError_WhenNoTargetProject()
    {
        // Config with no projects configured, request with no target project.
        var config = JsonSerializer.Serialize(new AzureDevOpsSourceConfig
        {
            OrganizationUrl = "https://dev.azure.com/testorg",
            Projects = [],
        }, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });

        var client = CreateClient();
        var request = MakeRequest(targetProject: null);

        var result = await client.CreateExternalWorkItemAsync(config, "pat-token", request);

        Assert.False(result.Success);
        Assert.Contains("No target project", result.ErrorDetail);
    }

    [Fact]
    public async Task CreateExternalWorkItem_Success_ReturnsIdAndUrl()
    {
        var responseBody = """{"id":42,"fields":{"System.Title":"Test Bug"},"url":"https://dev.azure.com/testorg/_apis/wit/workitems/42"}""";
        var handler = new MockHttpHandler(HttpStatusCode.OK, responseBody);
        var client = CreateClient(handler);
        var config = CreateSourceConfigJson();
        var request = MakeRequest(targetProject: "MyProject");

        var result = await client.CreateExternalWorkItemAsync(config, "pat-token", request);

        Assert.True(result.Success);
        Assert.Equal("42", result.ExternalId);
        Assert.Contains("MyProject/_workitems/edit/42", result.ExternalUrl);
        Assert.Null(result.ErrorDetail);
    }

    [Fact]
    public async Task CreateExternalWorkItem_Success_FallsBackToFirstConfiguredProject()
    {
        var responseBody = """{"id":99,"fields":{},"url":""}""";
        var handler = new MockHttpHandler(HttpStatusCode.OK, responseBody);
        var client = CreateClient(handler);
        var config = CreateSourceConfigJson();
        // No TargetProject in request — should use "MyProject" from config.
        var request = MakeRequest(targetProject: null);

        var result = await client.CreateExternalWorkItemAsync(config, "pat-token", request);

        Assert.True(result.Success);
        Assert.Equal("99", result.ExternalId);
        Assert.Contains("MyProject/_workitems/edit/99", result.ExternalUrl);
    }

    [Fact]
    public async Task CreateExternalWorkItem_ApiFailure_ReturnsError()
    {
        var handler = new MockHttpHandler(HttpStatusCode.Forbidden, """{"message":"Access denied"}""");
        var client = CreateClient(handler);
        var config = CreateSourceConfigJson();
        var request = MakeRequest(targetProject: "MyProject");

        var result = await client.CreateExternalWorkItemAsync(config, "pat-token", request);

        Assert.False(result.Success);
        Assert.Contains("403", result.ErrorDetail);
        Assert.Contains("Access denied", result.ErrorDetail);
        Assert.Null(result.ExternalId);
        Assert.Null(result.ExternalUrl);
    }

    [Theory]
    [InlineData("P1", 1)]
    [InlineData("P2", 2)]
    [InlineData("P3", 3)]
    [InlineData("P4", 4)]
    [InlineData("Unknown", 3)] // Default to 3
    public async Task CreateExternalWorkItem_MapsSeverityToPriority(string severity, int expectedPriority)
    {
        // Capture the request body to verify priority mapping.
        var captureHandler = new CaptureRequestHandler(HttpStatusCode.OK,
            """{"id":1,"fields":{},"url":""}""");
        var client = CreateClient(captureHandler);
        var config = CreateSourceConfigJson();
        var request = new ExternalWorkItemRequest
        {
            Title = "Test",
            Description = "Desc",
            Severity = severity,
            TargetProject = "MyProject",
        };

        var result = await client.CreateExternalWorkItemAsync(config, "pat", request);

        Assert.True(result.Success);
        Assert.NotNull(captureHandler.LastRequestBody);
        // The body should contain the priority value.
        Assert.Contains($"\"value\":{expectedPriority}", captureHandler.LastRequestBody);
    }

    [Fact]
    public async Task CreateExternalWorkItem_ConnectionError_ReturnsError()
    {
        var handler = new ThrowingHandler(new HttpRequestException("Connection refused"));
        var client = CreateClient(handler);
        var config = CreateSourceConfigJson();
        var request = MakeRequest(targetProject: "MyProject");

        var result = await client.CreateExternalWorkItemAsync(config, "pat", request);

        Assert.False(result.Success);
        Assert.Contains("Connection error", result.ErrorDetail);
        Assert.Contains("Connection refused", result.ErrorDetail);
    }

    // --- Helpers ---

    private static ExternalWorkItemRequest MakeRequest(string? targetProject = "MyProject") => new()
    {
        Title = "Test Escalation Bug",
        Description = "## Escalation: Test\n\nDescription content.",
        Severity = "P2",
        TargetProject = targetProject,
        WorkItemType = "Bug",
    };

    private static AzureDevOpsConnectorClient CreateClient(HttpMessageHandler? handler = null)
    {
        var factory = new TestHttpClientFactory(handler ?? new MockHttpHandler(HttpStatusCode.OK, "{}"));
        var logger = new LoggerFactory().CreateLogger<AzureDevOpsConnectorClient>();
        return new AzureDevOpsConnectorClient(factory, logger);
    }

    private static string CreateSourceConfigJson()
    {
        return JsonSerializer.Serialize(new AzureDevOpsSourceConfig
        {
            OrganizationUrl = "https://dev.azure.com/testorg",
            Projects = ["MyProject"],
        }, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
    }
}

/// <summary>
/// Captures the request body for assertion while returning a fixed response.
/// </summary>
internal class CaptureRequestHandler : HttpMessageHandler
{
    private readonly HttpStatusCode _statusCode;
    private readonly string _responseBody;

    public string? LastRequestBody { get; private set; }

    public CaptureRequestHandler(HttpStatusCode statusCode, string responseBody)
    {
        _statusCode = statusCode;
        _responseBody = responseBody;
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (request.Content is not null)
            LastRequestBody = await request.Content.ReadAsStringAsync(cancellationToken);

        return new HttpResponseMessage(_statusCode)
        {
            Content = new StringContent(_responseBody, System.Text.Encoding.UTF8, "application/json"),
        };
    }
}

/// <summary>
/// Always throws the specified exception.
/// </summary>
internal class ThrowingHandler : HttpMessageHandler
{
    private readonly Exception _exception;

    public ThrowingHandler(Exception exception) => _exception = exception;

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        throw _exception;
    }
}
