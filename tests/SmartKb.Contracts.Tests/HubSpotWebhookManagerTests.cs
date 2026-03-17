using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using SmartKb.Contracts.Connectors;
using SmartKb.Contracts.Enums;
using SmartKb.Contracts.Models;
using SmartKb.Contracts.Services;

namespace SmartKb.Contracts.Tests;

public class HubSpotWebhookManagerTests
{
    [Fact]
    public void Type_ReturnsHubSpot()
    {
        var manager = CreateManager();
        Assert.Equal(ConnectorType.HubSpot, manager.Type);
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
        // Mock HubSpot subscription creation responses.
        var responses = new Dictionary<string, (HttpStatusCode, string)>
        {
            ["POST:webhooks/v3/12345/subscriptions"] = (HttpStatusCode.OK, """
                {"id": 1001, "eventType": "ticket.creation", "active": true}
            """),
        };

        var handler = new RoutingMockHandler(responses);
        var manager = CreateManager(handler);
        var config = CreateConfigJson();
        var context = new WebhookRegistrationContext(
            Guid.NewGuid(), "t1", config, "token", "https://smartkb.example.com");

        var results = await manager.RegisterAsync(context);

        // Should register webhooks for ticket events (default config has tickets only).
        Assert.NotEmpty(results);
        var first = results[0];
        Assert.Equal("1001", first.ExternalSubscriptionId);
        Assert.Contains("/api/webhooks/hubspot/", first.CallbackUrl);
        Assert.NotNull(first.WebhookSecret);
    }

    [Fact]
    public async Task RegisterAsync_FiltersEventsByObjectTypes()
    {
        var callCount = 0;
        var handler = new AsyncDelegatingMockHandler(async (request, ct) =>
        {
            callCount++;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    $$"""{"id": {{callCount}}, "eventType": "test", "active": true}""",
                    Encoding.UTF8, "application/json"),
            };
        });

        var config = CreateConfigJson(objectTypes: ["tickets"]); // Only tickets
        var manager = CreateManager(handler);
        var context = new WebhookRegistrationContext(
            Guid.NewGuid(), "t1", config, "token", "https://smartkb.example.com");

        var results = await manager.RegisterAsync(context);

        // Should register 2 events: ticket.creation + ticket.propertyChange
        Assert.Equal(2, results.Count);
    }

    [Fact]
    public async Task RegisterAsync_MultipleObjectTypes_RegistersAllEvents()
    {
        var callCount = 0;
        var handler = new AsyncDelegatingMockHandler(async (request, ct) =>
        {
            callCount++;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    $$"""{"id": {{callCount}}, "eventType": "test", "active": true}""",
                    Encoding.UTF8, "application/json"),
            };
        });

        var config = CreateConfigJson(objectTypes: ["tickets", "contacts", "deals"]);
        var manager = CreateManager(handler);
        var context = new WebhookRegistrationContext(
            Guid.NewGuid(), "t1", config, "token", "https://smartkb.example.com");

        var results = await manager.RegisterAsync(context);

        // 3 object types × 2 events each = 6
        Assert.Equal(6, results.Count);
    }

    [Fact]
    public async Task DeregisterAsync_NoConfig_DoesNotThrow()
    {
        var manager = CreateManager();
        var context = new WebhookDeregistrationContext(
            Guid.NewGuid(), "t1", null, "token", ["sub-1"]);

        await manager.DeregisterAsync(context); // Should not throw.
    }

    [Fact]
    public async Task DeregisterAsync_CallsDeleteForEachSubscription()
    {
        var deletedIds = new List<string>();
        var handler = new AsyncDelegatingMockHandler(async (request, ct) =>
        {
            if (request.Method == HttpMethod.Delete)
            {
                var path = request.RequestUri?.PathAndQuery ?? "";
                deletedIds.Add(path);
            }
            return new HttpResponseMessage(HttpStatusCode.OK);
        });

        var config = CreateConfigJson();
        var manager = CreateManager(handler);
        var context = new WebhookDeregistrationContext(
            Guid.NewGuid(), "t1", config, "token", ["sub-1", "sub-2"]);

        await manager.DeregisterAsync(context);

        Assert.Equal(2, deletedIds.Count);
        Assert.Contains(deletedIds, p => p.Contains("sub-1"));
        Assert.Contains(deletedIds, p => p.Contains("sub-2"));
    }

    // --- Signature verification tests ---

    [Fact]
    public void ValidateSignature_NoSecret_ReturnsTrue()
    {
        Assert.True(HubSpotWebhookManager.ValidateSignature("body", "sig", null, null));
        Assert.True(HubSpotWebhookManager.ValidateSignature("body", "sig", "", null));
    }

    [Fact]
    public void ValidateSignature_NoSignatureHeader_ReturnsFalse()
    {
        Assert.False(HubSpotWebhookManager.ValidateSignature("body", null, "secret", null));
        Assert.False(HubSpotWebhookManager.ValidateSignature("body", "", "secret", null));
    }

    [Fact]
    public void ValidateSignature_ValidHmac_ReturnsTrue()
    {
        var secret = "test-secret";
        var body = """[{"eventId":1,"subscriptionType":"ticket.creation","objectId":123}]""";

        // Compute the expected HMAC.
        var keyBytes = Encoding.UTF8.GetBytes(secret);
        var bodyBytes = Encoding.UTF8.GetBytes(body);
        var hmac = System.Security.Cryptography.HMACSHA256.HashData(keyBytes, bodyBytes);
        var signature = Convert.ToHexString(hmac).ToLowerInvariant();

        Assert.True(HubSpotWebhookManager.ValidateSignature(body, signature, secret, null));
    }

    [Fact]
    public void ValidateSignature_InvalidHmac_ReturnsFalse()
    {
        var body = "test body";
        Assert.False(HubSpotWebhookManager.ValidateSignature(body, "invalid-signature", "secret", null));
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

        Assert.True(HubSpotWebhookManager.ValidateSignature(body, signature, secret, null));
    }

    [Fact]
    public void GenerateWebhookSecret_ReturnsBase64_32Bytes()
    {
        var secret = HubSpotWebhookManager.GenerateWebhookSecret();
        Assert.NotNull(secret);

        var decoded = Convert.FromBase64String(secret);
        Assert.Equal(32, decoded.Length);
    }

    [Fact]
    public void GenerateWebhookSecret_IsUnique()
    {
        var s1 = HubSpotWebhookManager.GenerateWebhookSecret();
        var s2 = HubSpotWebhookManager.GenerateWebhookSecret();
        Assert.NotEqual(s1, s2);
    }

    // --- Helpers ---

    private static HubSpotWebhookManager CreateManager(HttpMessageHandler? handler = null)
    {
        var factory = new TestHttpClientFactory(handler ?? new MockHttpHandler(HttpStatusCode.OK, "{}"));
        var logger = new LoggerFactory().CreateLogger<HubSpotWebhookManager>();
        return new HubSpotWebhookManager(factory, logger);
    }

    private static string CreateConfigJson(IReadOnlyList<string>? objectTypes = null)
    {
        return JsonSerializer.Serialize(new HubSpotSourceConfig
        {
            PortalId = "12345",
            ObjectTypes = objectTypes ?? ["tickets"],
        }, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
    }
}

/// <summary>
/// Async delegating mock handler that accepts a custom async handler function.
/// </summary>
internal class AsyncDelegatingMockHandler : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _handler;

    public AsyncDelegatingMockHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handler)
    {
        _handler = handler;
    }

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        return _handler(request, cancellationToken);
    }
}
