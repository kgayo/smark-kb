using System.Net;
using System.Text.Json;
using SmartKb.Contracts.Connectors;
using SmartKb.Contracts.Enums;
using SmartKb.Contracts.Models;
using SmartKb.Contracts.Services;

namespace SmartKb.Contracts.Tests;

public class SharePointWebhookManagerTests
{
    [Fact]
    public void Type_ReturnsSharePoint()
    {
        var manager = CreateManager();
        Assert.Equal(ConnectorType.SharePoint, manager.Type);
    }

    [Fact]
    public void GenerateClientState_Returns44CharBase64()
    {
        var state = SharePointWebhookManager.GenerateClientState();
        Assert.NotEmpty(state);
        Assert.Equal(44, state.Length); // 32 bytes → Base64 = 44 chars
    }

    [Fact]
    public void GenerateClientState_UniqueEachCall()
    {
        var s1 = SharePointWebhookManager.GenerateClientState();
        var s2 = SharePointWebhookManager.GenerateClientState();
        Assert.NotEqual(s1, s2);
    }

    [Fact]
    public async Task RegisterAsync_InvalidConfig_ReturnsEmpty()
    {
        var manager = CreateManager();
        var ctx = new WebhookRegistrationContext(
            Guid.NewGuid(), "t1", null, "secret", "https://callback.example.com");

        var results = await manager.RegisterAsync(ctx);
        Assert.Empty(results);
    }

    [Fact]
    public async Task RegisterAsync_NoSecret_ReturnsEmpty()
    {
        var manager = CreateManager();
        var ctx = new WebhookRegistrationContext(
            Guid.NewGuid(), "t1", CreateSourceConfigJson(), null, "https://callback.example.com");

        var results = await manager.RegisterAsync(ctx);
        Assert.Empty(results);
    }

    [Fact]
    public async Task RegisterAsync_CreatesSubscriptionPerDrive()
    {
        var callLog = new List<string>();
        var handler = new DelegatingMockHandler(request =>
        {
            var url = request.RequestUri?.PathAndQuery ?? "";
            callLog.Add($"{request.Method}:{url}");

            if (url.Contains("oauth2/v2.0/token"))
                return JsonResponse(HttpStatusCode.OK, new { access_token = "tok", token_type = "Bearer", expires_in = 3600 });

            if (url.Contains("sites/contoso.sharepoint.com"))
                return JsonResponse(HttpStatusCode.OK, new { id = "site-1" });

            if (url.Contains("sites/site-1/drives"))
                return JsonResponse(HttpStatusCode.OK, new { value = new[] { new { id = "d1", name = "Docs" }, new { id = "d2", name = "Archives" } } });

            if (url.Contains("subscriptions") && request.Method == HttpMethod.Post)
                return JsonResponse(HttpStatusCode.Created, new { id = $"sub-{Guid.NewGuid():N}", resource = "/drives/d1/root" });

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });

        var manager = CreateManager(handler);
        var ctx = new WebhookRegistrationContext(
            Guid.NewGuid(), "t1", CreateSourceConfigJson(), "secret",
            "https://smartkb-api.example.com");

        var results = await manager.RegisterAsync(ctx);

        // Should create one subscription per drive.
        Assert.Equal(2, results.Count);
        Assert.All(results, r =>
        {
            Assert.NotEmpty(r.ExternalSubscriptionId);
            Assert.Contains("/api/webhooks/msgraph/", r.CallbackUrl);
            Assert.NotNull(r.WebhookSecret);
            Assert.StartsWith("driveItem.changed.", r.EventType);
        });
    }

    [Fact]
    public async Task RegisterAsync_HandlesApiFailure_GracefullySkips()
    {
        var handler = new DelegatingMockHandler(request =>
        {
            var url = request.RequestUri?.PathAndQuery ?? "";

            if (url.Contains("oauth2/v2.0/token"))
                return JsonResponse(HttpStatusCode.OK, new { access_token = "tok", token_type = "Bearer", expires_in = 3600 });
            if (url.Contains("sites/contoso.sharepoint.com"))
                return JsonResponse(HttpStatusCode.OK, new { id = "site-1" });
            if (url.Contains("sites/site-1/drives"))
                return JsonResponse(HttpStatusCode.OK, new { value = new[] { new { id = "d1", name = "Docs" } } });
            if (url.Contains("subscriptions") && request.Method == HttpMethod.Post)
                return new HttpResponseMessage(HttpStatusCode.Forbidden) { Content = new System.Net.Http.StringContent("Access denied") };

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });

        var manager = CreateManager(handler);
        var ctx = new WebhookRegistrationContext(
            Guid.NewGuid(), "t1", CreateSourceConfigJson(), "secret",
            "https://smartkb-api.example.com");

        var results = await manager.RegisterAsync(ctx);
        Assert.Empty(results); // Subscription creation failed — returned empty.
    }

    [Fact]
    public async Task DeregisterAsync_DeletesAllSubscriptions()
    {
        var deletedIds = new List<string>();
        var handler = new DelegatingMockHandler(request =>
        {
            var url = request.RequestUri?.PathAndQuery ?? "";

            if (url.Contains("oauth2/v2.0/token"))
                return JsonResponse(HttpStatusCode.OK, new { access_token = "tok", token_type = "Bearer", expires_in = 3600 });

            if (request.Method == HttpMethod.Delete && url.Contains("subscriptions/"))
            {
                var subId = url.Split("subscriptions/")[1].Split("?")[0];
                deletedIds.Add(subId);
                return new HttpResponseMessage(HttpStatusCode.NoContent);
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });

        var manager = CreateManager(handler);
        var ctx = new WebhookDeregistrationContext(
            Guid.NewGuid(), "t1", CreateSourceConfigJson(), "secret",
            ["sub-aaa", "sub-bbb"]);

        await manager.DeregisterAsync(ctx);

        Assert.Equal(2, deletedIds.Count);
        Assert.Contains("sub-aaa", deletedIds);
        Assert.Contains("sub-bbb", deletedIds);
    }

    [Fact]
    public async Task DeregisterAsync_HandlesDeleteFailure_Gracefully()
    {
        var handler = new DelegatingMockHandler(request =>
        {
            var url = request.RequestUri?.PathAndQuery ?? "";

            if (url.Contains("oauth2/v2.0/token"))
                return JsonResponse(HttpStatusCode.OK, new { access_token = "tok", token_type = "Bearer", expires_in = 3600 });

            if (request.Method == HttpMethod.Delete)
                return new HttpResponseMessage(HttpStatusCode.NotFound);

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });

        var manager = CreateManager(handler);
        var ctx = new WebhookDeregistrationContext(
            Guid.NewGuid(), "t1", CreateSourceConfigJson(), "secret",
            ["sub-missing"]);

        // Should not throw.
        await manager.DeregisterAsync(ctx);
    }

    // --- Helpers ---

    private static SharePointWebhookManager CreateManager(HttpMessageHandler? handler = null)
    {
        var factory = new TestHttpClientFactory(handler ?? new MockHttpHandler(HttpStatusCode.OK, "{}"));
        var logger = Microsoft.Extensions.Logging.Abstractions.NullLogger<SharePointWebhookManager>.Instance;
        return new SharePointWebhookManager(factory, logger);
    }

    private static string CreateSourceConfigJson()
    {
        return JsonSerializer.Serialize(new SharePointSourceConfig
        {
            SiteUrl = "https://contoso.sharepoint.com/sites/support",
            EntraIdTenantId = "aad-tenant-123",
            ClientId = "app-client-id",
        }, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
    }

    private static HttpResponseMessage JsonResponse<T>(HttpStatusCode status, T body)
    {
        var json = JsonSerializer.Serialize(body, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
        return new HttpResponseMessage(status)
        {
            Content = new System.Net.Http.StringContent(json, System.Text.Encoding.UTF8, "application/json"),
        };
    }
}
