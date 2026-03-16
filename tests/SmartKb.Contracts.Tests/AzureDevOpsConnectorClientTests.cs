using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using SmartKb.Contracts.Connectors;
using SmartKb.Contracts.Enums;
using SmartKb.Contracts.Models;

namespace SmartKb.Contracts.Tests;

public class AzureDevOpsConnectorClientTests
{
    [Fact]
    public void Type_ReturnsAzureDevOps()
    {
        var client = CreateClient();
        Assert.Equal(ConnectorType.AzureDevOps, client.Type);
    }

    // --- ParseSourceConfig tests ---

    [Fact]
    public void ParseSourceConfig_ValidJson_ReturnsConfig()
    {
        var json = JsonSerializer.Serialize(new AzureDevOpsSourceConfig
        {
            OrganizationUrl = "https://dev.azure.com/myorg",
            Projects = ["Project1", "Project2"],
            IngestWorkItems = true,
            IngestWikiPages = false,
            BatchSize = 100,
        });

        var config = AzureDevOpsConnectorClient.ParseSourceConfig(json);

        Assert.NotNull(config);
        Assert.Equal("https://dev.azure.com/myorg", config.OrganizationUrl);
        Assert.Equal(2, config.Projects.Count);
        Assert.True(config.IngestWorkItems);
        Assert.False(config.IngestWikiPages);
        Assert.Equal(100, config.BatchSize);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not-json")]
    public void ParseSourceConfig_InvalidInput_ReturnsNull(string? input)
    {
        var result = AzureDevOpsConnectorClient.ParseSourceConfig(input);
        Assert.Null(result);
    }

    [Fact]
    public void ParseSourceConfig_DefaultValues()
    {
        var json = """{"organizationUrl":"https://dev.azure.com/test"}""";
        var config = AzureDevOpsConnectorClient.ParseSourceConfig(json);

        Assert.NotNull(config);
        Assert.True(config.IngestWorkItems);
        Assert.True(config.IngestWikiPages);
        Assert.Equal(200, config.BatchSize);
        Assert.Empty(config.Projects);
        Assert.Empty(config.WorkItemTypes);
        Assert.Empty(config.AreaPaths);
    }

    // --- ComputeHash tests ---

    [Fact]
    public void ComputeHash_DeterministicOutput()
    {
        var hash1 = AzureDevOpsConnectorClient.ComputeHash("test input");
        var hash2 = AzureDevOpsConnectorClient.ComputeHash("test input");
        Assert.Equal(hash1, hash2);
    }

    [Fact]
    public void ComputeHash_DifferentInputs_DifferentHashes()
    {
        var hash1 = AzureDevOpsConnectorClient.ComputeHash("input A");
        var hash2 = AzureDevOpsConnectorClient.ComputeHash("input B");
        Assert.NotEqual(hash1, hash2);
    }

    [Fact]
    public void ComputeHash_ReturnsLowercaseHex()
    {
        var hash = AzureDevOpsConnectorClient.ComputeHash("test");
        Assert.Matches("^[0-9a-f]+$", hash);
        Assert.Equal(64, hash.Length); // SHA256 = 32 bytes = 64 hex chars
    }

    // --- StripHtml tests ---

    [Theory]
    [InlineData("", "")]
    [InlineData("plain text", "plain text")]
    [InlineData("<p>paragraph</p>", "paragraph")]
    [InlineData("<div><b>bold</b> text</div>", "bold text")]
    [InlineData("<p>line1</p><p>line2</p>", "line1 line2")]
    [InlineData("text with <a href='url'>link</a> inside", "text with link inside")]
    public void StripHtml_RemovesTags(string input, string expected)
    {
        var result = AzureDevOpsConnectorClient.StripHtml(input);
        Assert.Equal(expected, result);
    }

    [Fact]
    public void StripHtml_CollapsesWhitespace()
    {
        var result = AzureDevOpsConnectorClient.StripHtml("<p>word1</p>   <p>word2</p>");
        Assert.DoesNotContain("  ", result);
    }

    // --- Checkpoint tests ---

    [Fact]
    public void AdoCheckpoint_Roundtrip()
    {
        var ts = new DateTimeOffset(2026, 3, 15, 10, 30, 0, TimeSpan.Zero);
        var cp = new AdoCheckpoint(2, AdoFetchPhase.WikiPages, ts);

        var serialized = cp.Serialize();
        var parsed = AdoCheckpoint.Parse(serialized);

        Assert.NotNull(parsed);
        Assert.Equal(2, parsed.ProjectIndex);
        Assert.Equal(AdoFetchPhase.WikiPages, parsed.Phase);
        Assert.NotNull(parsed.LastModified);
        Assert.Equal(ts, parsed.LastModified.Value);
    }

    [Fact]
    public void AdoCheckpoint_Parse_NullInput_ReturnsNull()
    {
        Assert.Null(AdoCheckpoint.Parse(null));
        Assert.Null(AdoCheckpoint.Parse(""));
        Assert.Null(AdoCheckpoint.Parse("   "));
    }

    [Fact]
    public void AdoCheckpoint_Parse_InvalidFormat_ReturnsNull()
    {
        Assert.Null(AdoCheckpoint.Parse("not-valid"));
        Assert.Null(AdoCheckpoint.Parse("abc|WorkItems|2026-01-01"));
    }

    [Fact]
    public void AdoCheckpoint_Serialize_NoLastModified()
    {
        var cp = new AdoCheckpoint(0, AdoFetchPhase.WorkItems, null);
        var serialized = cp.Serialize();
        Assert.Equal("0|WorkItems|", serialized);

        var parsed = AdoCheckpoint.Parse(serialized);
        Assert.NotNull(parsed);
        Assert.Null(parsed.LastModified);
    }

    // --- TestConnectionAsync tests ---

    [Fact]
    public async Task TestConnectionAsync_NoSourceConfig_ReturnsFalse()
    {
        var client = CreateClient();
        var result = await client.TestConnectionAsync("t1", null, "pat-token");
        Assert.False(result.Success);
        Assert.Contains("source configuration", result.Message);
    }

    [Fact]
    public async Task TestConnectionAsync_NoSecret_ReturnsFalse()
    {
        var config = CreateSourceConfigJson();
        var client = CreateClient();
        var result = await client.TestConnectionAsync("t1", config, null);
        Assert.False(result.Success);
        Assert.Contains("credentials", result.Message);
    }

    [Fact]
    public async Task TestConnectionAsync_SuccessfulConnection()
    {
        var handler = new MockHttpHandler(HttpStatusCode.OK, """{"count":1,"value":[{"name":"MyProject"}]}""");
        var client = CreateClient(handler);
        var config = CreateSourceConfigJson();

        var result = await client.TestConnectionAsync("t1", config, "my-pat");

        Assert.True(result.Success);
        Assert.Contains("Successfully connected", result.Message);
    }

    [Fact]
    public async Task TestConnectionAsync_Unauthorized()
    {
        var handler = new MockHttpHandler(HttpStatusCode.Unauthorized, "Access denied");
        var client = CreateClient(handler);
        var config = CreateSourceConfigJson();

        var result = await client.TestConnectionAsync("t1", config, "bad-pat");

        Assert.False(result.Success);
        Assert.Contains("401", result.Message);
    }

    // --- FetchAsync tests ---

    [Fact]
    public async Task FetchAsync_InvalidConfig_ReturnsError()
    {
        var client = CreateClient();
        var result = await client.FetchAsync("t1", null, null, "pat", null, true);

        Assert.Empty(result.Records);
        Assert.Single(result.Errors);
        Assert.Contains("source configuration", result.Errors[0]);
    }

    [Fact]
    public async Task FetchAsync_NoSecret_ReturnsError()
    {
        var config = CreateSourceConfigJson();
        var client = CreateClient();
        var result = await client.FetchAsync("t1", config, null, null, null, true);

        Assert.Empty(result.Records);
        Assert.Contains("credentials", result.Errors[0]);
    }

    [Fact]
    public async Task FetchAsync_WorkItems_BackfillMode()
    {
        var responses = new Dictionary<string, (HttpStatusCode, string)>
        {
            // WIQL query returns 2 work item IDs.
            ["POST:MyProject/_apis/wit/wiql"] = (HttpStatusCode.OK, """
                {"workItems":[{"id":1},{"id":2}]}
            """),
            // Work item details.
            ["GET:_apis/wit/workitems?ids=1,2"] = (HttpStatusCode.OK, """
                {"value":[
                    {"id":1,"fields":{"System.Title":"Bug 1","System.Description":"<p>Desc 1</p>","System.WorkItemType":"Bug","System.State":"Active","System.AreaPath":"MyProject\\Team1","System.CreatedDate":"2026-01-01T00:00:00Z","System.ChangedDate":"2026-03-01T00:00:00Z","System.Tags":"tag1; tag2"}},
                    {"id":2,"fields":{"System.Title":"Task 2","System.Description":"Desc 2","System.WorkItemType":"Task","System.State":"Closed","System.AreaPath":"MyProject\\Team2","System.CreatedDate":"2026-02-01T00:00:00Z","System.ChangedDate":"2026-03-10T00:00:00Z","System.Tags":""}}
                ]}
            """),
        };

        var config = CreateSourceConfigJson(ingestWiki: false);
        var handler = new RoutingMockHandler(responses);
        var client = CreateClient(handler);

        var result = await client.FetchAsync("t1", config, null, "pat", null, true);

        Assert.Equal(2, result.Records.Count);
        Assert.False(result.HasMore);
        Assert.NotNull(result.NewCheckpoint);
        Assert.Empty(result.Errors);

        var bug = result.Records.First(r => r.EvidenceId == "ado-wi-1");
        Assert.Equal("Bug 1", bug.Title);
        Assert.Equal("Desc 1", bug.TextContent);
        Assert.Equal(SourceType.WorkItem, bug.SourceType);
        Assert.Equal(EvidenceStatus.Open, bug.Status);
        Assert.Equal("MyProject\\Team1", bug.ProductArea);
        Assert.Contains("MyProject\\Team1", bug.Permissions.AllowedGroups);
        Assert.Equal(AccessVisibility.Restricted, bug.Permissions.Visibility);
        Assert.Equal(ConnectorType.AzureDevOps, bug.SourceSystem);
        Assert.Equal("t1", bug.TenantId);
        Assert.Contains("/MyProject/_workitems/edit/1", bug.SourceLocator.Url);
        Assert.Equal(2, bug.Tags.Count);

        var task = result.Records.First(r => r.EvidenceId == "ado-wi-2");
        Assert.Equal("Task 2", task.Title);
        Assert.Equal(SourceType.Task, task.SourceType);
        Assert.Equal(EvidenceStatus.Closed, task.Status);
    }

    [Fact]
    public async Task FetchAsync_WikiPages()
    {
        var responses = new Dictionary<string, (HttpStatusCode, string)>
        {
            // List wikis.
            ["GET:MyProject/_apis/wiki/wikis"] = (HttpStatusCode.OK, """
                {"value":[{"id":"wiki-1","name":"MyWiki"}]}
            """),
            // List pages (recursive).
            ["GET:MyProject/_apis/wiki/wikis/wiki-1/pages?api-version=7.1&recursionLevel=full"] = (HttpStatusCode.OK, """
                {"id":0,"path":"/","subPages":[
                    {"id":1,"path":"/Getting-Started","subPages":[]},
                    {"id":2,"path":"/API-Reference","subPages":[]}
                ]}
            """),
            // Page content for each.
            ["GET:MyProject/_apis/wiki/wikis/wiki-1/pages?path=%2FGetting-Started"] = (HttpStatusCode.OK, """
                {"id":1,"path":"/Getting-Started","content":"# Getting Started\nWelcome to our wiki."}
            """),
            ["GET:MyProject/_apis/wiki/wikis/wiki-1/pages?path=%2FAPI-Reference"] = (HttpStatusCode.OK, """
                {"id":2,"path":"/API-Reference","content":"# API Reference\nEndpoints listed here."}
            """),
        };

        var config = CreateSourceConfigJson(ingestWorkItems: false);
        var handler = new RoutingMockHandler(responses);
        var client = CreateClient(handler);

        var result = await client.FetchAsync("t1", config, null, "pat", null, true);

        Assert.Equal(2, result.Records.Count);

        var page1 = result.Records.First(r => r.EvidenceId == "ado-wiki-wiki-1-1");
        Assert.Equal("Getting Started", page1.Title);
        Assert.Contains("Welcome to our wiki", page1.TextContent);
        Assert.Equal(SourceType.WikiPage, page1.SourceType);
        Assert.Equal(AccessVisibility.Internal, page1.Permissions.Visibility);
        Assert.Contains("/MyWiki/", page1.SourceLocator.Url);

        var page2 = result.Records.First(r => r.EvidenceId == "ado-wiki-wiki-1-2");
        Assert.Equal("API Reference", page2.Title);
    }

    [Fact]
    public async Task FetchAsync_Checkpoint_PreservesState()
    {
        // First call completes with a checkpoint.
        var responses1 = new Dictionary<string, (HttpStatusCode, string)>
        {
            ["POST:MyProject/_apis/wit/wiql"] = (HttpStatusCode.OK, """
                {"workItems":[{"id":10}]}
            """),
            ["GET:_apis/wit/workitems?ids=10"] = (HttpStatusCode.OK, """
                {"value":[
                    {"id":10,"fields":{"System.Title":"Item 10","System.Description":"","System.WorkItemType":"Bug","System.State":"Active","System.AreaPath":"","System.CreatedDate":"2026-03-15T00:00:00Z","System.ChangedDate":"2026-03-15T10:30:00Z","System.Tags":""}}
                ]}
            """),
        };

        var config = CreateSourceConfigJson(ingestWiki: false);
        var handler = new RoutingMockHandler(responses1);
        var client = CreateClient(handler);

        var result1 = await client.FetchAsync("t1", config, null, "pat", null, true);
        Assert.NotNull(result1.NewCheckpoint);
        Assert.Single(result1.Records);

        // Second call uses checkpoint for incremental fetch.
        var parsed = AdoCheckpoint.Parse(result1.NewCheckpoint);
        Assert.NotNull(parsed);
        Assert.NotNull(parsed.LastModified);
    }

    [Fact]
    public async Task PreviewAsync_ReturnsLimitedRecords()
    {
        var responses = new Dictionary<string, (HttpStatusCode, string)>
        {
            ["POST:MyProject/_apis/wit/wiql"] = (HttpStatusCode.OK, """
                {"workItems":[{"id":1}]}
            """),
            ["GET:_apis/wit/workitems?ids=1"] = (HttpStatusCode.OK, """
                {"value":[
                    {"id":1,"fields":{"System.Title":"Item 1","System.Description":"Content","System.WorkItemType":"Bug","System.State":"Active","System.AreaPath":"","System.CreatedDate":"2026-01-01T00:00:00Z","System.ChangedDate":"2026-01-01T00:00:00Z","System.Tags":""}}
                ]}
            """),
        };

        var config = CreateSourceConfigJson(ingestWiki: false);
        var handler = new RoutingMockHandler(responses);
        var client = CreateClient(handler);

        var records = await client.PreviewAsync("t1", config, null, "pat", 5);

        Assert.Single(records);
        Assert.Equal("Item 1", records[0].Title);
    }

    [Fact]
    public async Task PreviewAsync_NoConfig_ReturnsEmpty()
    {
        var client = CreateClient();
        var records = await client.PreviewAsync("t1", null, null, "pat", 5);
        Assert.Empty(records);
    }

    // --- AzureDevOpsSourceConfig model tests ---

    [Fact]
    public void SourceConfig_DefaultsAreCorrect()
    {
        var config = new AzureDevOpsSourceConfig { OrganizationUrl = "https://dev.azure.com/test" };
        Assert.True(config.IngestWorkItems);
        Assert.True(config.IngestWikiPages);
        Assert.Equal(200, config.BatchSize);
        Assert.Empty(config.Projects);
        Assert.Empty(config.WorkItemTypes);
        Assert.Empty(config.AreaPaths);
    }

    // --- Helpers ---

    private static AzureDevOpsConnectorClient CreateClient(HttpMessageHandler? handler = null)
    {
        var factory = new TestHttpClientFactory(handler ?? new MockHttpHandler(HttpStatusCode.OK, "{}"));
        var logger = new LoggerFactory().CreateLogger<AzureDevOpsConnectorClient>();
        return new AzureDevOpsConnectorClient(factory, logger);
    }

    private static string CreateSourceConfigJson(
        string org = "https://dev.azure.com/testorg",
        bool ingestWorkItems = true,
        bool ingestWiki = true)
    {
        return JsonSerializer.Serialize(new AzureDevOpsSourceConfig
        {
            OrganizationUrl = org,
            Projects = ["MyProject"],
            IngestWorkItems = ingestWorkItems,
            IngestWikiPages = ingestWiki,
        }, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
    }
}

// --- Test infrastructure ---

internal class TestHttpClientFactory : IHttpClientFactory
{
    private readonly HttpMessageHandler _handler;

    public TestHttpClientFactory(HttpMessageHandler handler) => _handler = handler;

    public HttpClient CreateClient(string name) => new(_handler, disposeHandler: false);
}

internal class MockHttpHandler : HttpMessageHandler
{
    private readonly HttpStatusCode _statusCode;
    private readonly string _responseBody;

    public MockHttpHandler(HttpStatusCode statusCode, string responseBody)
    {
        _statusCode = statusCode;
        _responseBody = responseBody;
    }

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        return Task.FromResult(new HttpResponseMessage(_statusCode)
        {
            Content = new StringContent(_responseBody, Encoding.UTF8, "application/json"),
        });
    }
}

/// <summary>
/// Routes HTTP requests to different responses based on method + URL prefix matching.
/// Keys use format "METHOD:urlFragment" — matches if the request URL contains the fragment.
/// </summary>
internal class RoutingMockHandler : HttpMessageHandler
{
    private readonly Dictionary<string, (HttpStatusCode Status, string Body)> _routes;

    public RoutingMockHandler(Dictionary<string, (HttpStatusCode, string)> routes)
    {
        _routes = routes;
    }

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var url = request.RequestUri?.PathAndQuery ?? "";
        var method = request.Method.Method;

        // Find matching routes; prefer the longest (most specific) fragment match.
        (HttpStatusCode Status, string Body)? bestMatch = null;
        int bestLength = -1;

        foreach (var (key, value) in _routes)
        {
            var parts = key.Split(':', 2);
            if (parts.Length != 2) continue;

            var routeMethod = parts[0];
            var routeFragment = parts[1];

            if (method == routeMethod && url.Contains(routeFragment) && routeFragment.Length > bestLength)
            {
                bestMatch = value;
                bestLength = routeFragment.Length;
            }
        }

        if (bestMatch.HasValue)
        {
            return Task.FromResult(new HttpResponseMessage(bestMatch.Value.Status)
            {
                Content = new StringContent(bestMatch.Value.Body, Encoding.UTF8, "application/json"),
            });
        }

        // Default: return 404 for unmatched routes.
        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound)
        {
            Content = new StringContent($"No mock route for {method} {url}", Encoding.UTF8, "text/plain"),
        });
    }
}
