using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using SmartKb.Contracts.Connectors;
using SmartKb.Contracts.Enums;
using SmartKb.Contracts.Models;
using SmartKb.Contracts.Services;

namespace SmartKb.Contracts.Tests;

public class ClickUpWebhookManagerTests
{
    [Fact]
    public void Type_ReturnsClickUp()
    {
        var manager = CreateManager();
        Assert.Equal(ConnectorType.ClickUp, manager.Type);
    }

    [Fact]
    public async Task RegisterAsync_NoConfig_ReturnsEmpty()
    {
        var manager = CreateManager();
        var context = new WebhookRegistrationContext(
            Guid.NewGuid(), "t1", null, "token", "https://smartkb.example.com");

        var results = await manager.RegisterAsync(context);
        Assert.Empty(results);
    }

    [Fact]
    public async Task RegisterAsync_NoSecret_ReturnsEmpty()
    {
        var config = CreateConfigJson();
        var manager = CreateManager();
        var context = new WebhookRegistrationContext(
            Guid.NewGuid(), "t1", config, null, "https://smartkb.example.com");

        var results = await manager.RegisterAsync(context);
        Assert.Empty(results);
    }

    [Fact]
    public async Task RegisterAsync_Success_ReturnsResults()
    {
        var responses = new Dictionary<string, (HttpStatusCode, string)>
        {
            ["POST:api/v2/team/12345/webhook"] = (HttpStatusCode.OK, """
                {"id": "wh-1", "webhook": {"id": "wh-1", "endpoint": "https://test/api/webhooks/clickup/xxx", "events": ["taskCreated","taskUpdated"], "status": "active"}}
            """),
        };

        var handler = new RoutingMockHandler(responses);
        var manager = CreateManager(handler);
        var config = CreateConfigJson();
        var connectorId = Guid.NewGuid();
        var context = new WebhookRegistrationContext(
            connectorId, "t1", config, "token", "https://smartkb.example.com");

        var results = await manager.RegisterAsync(context);

        // ClickUp registers one webhook with multiple events; we get one result per event type.
        Assert.NotEmpty(results);
        var first = results[0];
        Assert.Equal("wh-1", first.ExternalSubscriptionId);
        Assert.Contains("/api/webhooks/clickup/", first.CallbackUrl);
        Assert.NotNull(first.WebhookSecret);
    }

    [Fact]
    public async Task RegisterAsync_RegistersAllEventTypes()
    {
        var handler = new AsyncDelegatingMockHandler(async (request, ct) =>
        {
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    """{"id":"wh-1","webhook":{"id":"wh-1","endpoint":"test","events":[],"status":"active"}}""",
                    Encoding.UTF8, "application/json"),
            };
        });

        var config = CreateConfigJson();
        var manager = CreateManager(handler);
        var context = new WebhookRegistrationContext(
            Guid.NewGuid(), "t1", config, "token", "https://smartkb.example.com");

        var results = await manager.RegisterAsync(context);

        // 5 supported event types: taskCreated, taskUpdated, taskDeleted, taskStatusUpdated, taskCommentPosted
        Assert.Equal(5, results.Count);
    }

    [Fact]
    public async Task DeregisterAsync_NoConfig_DoesNotThrow()
    {
        var manager = CreateManager();
        var context = new WebhookDeregistrationContext(
            Guid.NewGuid(), "t1", null, "token", ["wh-1"]);

        await manager.DeregisterAsync(context); // Should not throw.
    }

    [Fact]
    public async Task DeregisterAsync_CallsDeleteForWebhook()
    {
        var deletedPaths = new List<string>();
        var handler = new AsyncDelegatingMockHandler(async (request, ct) =>
        {
            if (request.Method == HttpMethod.Delete)
            {
                deletedPaths.Add(request.RequestUri?.PathAndQuery ?? "");
            }
            return new HttpResponseMessage(HttpStatusCode.OK);
        });

        var config = CreateConfigJson();
        var manager = CreateManager(handler);
        var context = new WebhookDeregistrationContext(
            Guid.NewGuid(), "t1", config, "token", ["wh-1", "wh-1", "wh-2"]);

        await manager.DeregisterAsync(context);

        // Deduplicates wh-1, so should call DELETE for wh-1 and wh-2 = 2 calls.
        Assert.Equal(2, deletedPaths.Count);
        Assert.Contains(deletedPaths, p => p.Contains("wh-1"));
        Assert.Contains(deletedPaths, p => p.Contains("wh-2"));
    }

    // --- Signature verification tests ---

    [Fact]
    public void ValidateSignature_NoSecret_ReturnsTrue()
    {
        Assert.True(ClickUpWebhookManager.ValidateSignature("body", "sig", null));
        Assert.True(ClickUpWebhookManager.ValidateSignature("body", "sig", ""));
    }

    [Fact]
    public void ValidateSignature_NoSignatureHeader_ReturnsFalse()
    {
        Assert.False(ClickUpWebhookManager.ValidateSignature("body", null, "secret"));
        Assert.False(ClickUpWebhookManager.ValidateSignature("body", "", "secret"));
    }

    [Fact]
    public void ValidateSignature_ValidHmac_ReturnsTrue()
    {
        var secret = "test-secret";
        var body = """{"webhook_id":"wh-1","event":"taskCreated","task_id":"abc123"}""";

        // Compute the expected HMAC.
        var keyBytes = Encoding.UTF8.GetBytes(secret);
        var bodyBytes = Encoding.UTF8.GetBytes(body);
        var hmac = System.Security.Cryptography.HMACSHA256.HashData(keyBytes, bodyBytes);
        var signature = Convert.ToHexString(hmac).ToLowerInvariant();

        Assert.True(ClickUpWebhookManager.ValidateSignature(body, signature, secret));
    }

    [Fact]
    public void ValidateSignature_InvalidHmac_ReturnsFalse()
    {
        var body = "test body";
        Assert.False(ClickUpWebhookManager.ValidateSignature(body, "invalid-signature", "secret"));
    }

    [Fact]
    public void ValidateSignature_CaseInsensitive()
    {
        var secret = "my-secret";
        var body = "test";

        var keyBytes = Encoding.UTF8.GetBytes(secret);
        var bodyBytes = Encoding.UTF8.GetBytes(body);
        var hmac = System.Security.Cryptography.HMACSHA256.HashData(keyBytes, bodyBytes);
        var signature = Convert.ToHexString(hmac).ToUpperInvariant(); // uppercase

        Assert.True(ClickUpWebhookManager.ValidateSignature(body, signature, secret));
    }

    [Fact]
    public void GenerateWebhookSecret_ReturnsBase64_32Bytes()
    {
        var secret = ClickUpWebhookManager.GenerateWebhookSecret();
        Assert.NotNull(secret);

        var decoded = Convert.FromBase64String(secret);
        Assert.Equal(32, decoded.Length);
    }

    [Fact]
    public void GenerateWebhookSecret_IsUnique()
    {
        var s1 = ClickUpWebhookManager.GenerateWebhookSecret();
        var s2 = ClickUpWebhookManager.GenerateWebhookSecret();
        Assert.NotEqual(s1, s2);
    }

    // --- ResolveEventTypes tests ---

    [Fact]
    public void ResolveEventTypes_IngestTasks_ReturnsAllTaskEvents()
    {
        var config = new ClickUpSourceConfig { WorkspaceId = "1", IngestTasks = true };
        var events = ClickUpWebhookManager.ResolveEventTypes(config);
        Assert.Equal(5, events.Count);
        Assert.Contains("taskCreated", events);
        Assert.Contains("taskUpdated", events);
        Assert.Contains("taskDeleted", events);
        Assert.Contains("taskStatusUpdated", events);
        Assert.Contains("taskCommentPosted", events);
    }

    [Fact]
    public void ResolveEventTypes_NoIngest_ReturnsFallbackEvents()
    {
        var config = new ClickUpSourceConfig { WorkspaceId = "1", IngestTasks = false, IngestDocs = false };
        var events = ClickUpWebhookManager.ResolveEventTypes(config);
        // Fallback: returns all supported events.
        Assert.Equal(5, events.Count);
    }

    // --- Helpers ---

    private static ClickUpWebhookManager CreateManager(HttpMessageHandler? handler = null)
    {
        var factory = new TestHttpClientFactory(handler ?? new MockHttpHandler(HttpStatusCode.OK, "{}"));
        var logger = new LoggerFactory().CreateLogger<ClickUpWebhookManager>();
        return new ClickUpWebhookManager(factory, logger);
    }

    private static string CreateConfigJson()
    {
        return JsonSerializer.Serialize(new ClickUpSourceConfig
        {
            WorkspaceId = "12345",
        }, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
    }
}
