using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using SmartKb.Contracts.Connectors;
using SmartKb.Contracts.Enums;
using SmartKb.Contracts.Models;
using SmartKb.Contracts.Services;

namespace SmartKb.Contracts.Tests;

public sealed class AdoWebhookManagerTests
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    [Fact]
    public void Type_ReturnsAzureDevOps()
    {
        var manager = CreateManager();
        Assert.Equal(ConnectorType.AzureDevOps, manager.Type);
    }

    [Fact]
    public async Task RegisterAsync_ReturnsEmpty_WhenSourceConfigInvalid()
    {
        var manager = CreateManager();
        var ctx = new WebhookRegistrationContext(
            Guid.NewGuid(), "t1", null, "pat-123", "https://test.example.com");

        var results = await manager.RegisterAsync(ctx);
        Assert.Empty(results);
    }

    [Fact]
    public async Task RegisterAsync_ReturnsEmpty_WhenNoCredentials()
    {
        var config = JsonSerializer.Serialize(new AzureDevOpsSourceConfig
        {
            OrganizationUrl = "https://dev.azure.com/test",
        }, JsonOptions);

        var manager = CreateManager();
        var ctx = new WebhookRegistrationContext(
            Guid.NewGuid(), "t1", config, null, "https://test.example.com");

        var results = await manager.RegisterAsync(ctx);
        Assert.Empty(results);
    }

    [Fact]
    public async Task RegisterAsync_RegistersTwoEventTypes_WhenApiSucceeds()
    {
        var config = JsonSerializer.Serialize(new AzureDevOpsSourceConfig
        {
            OrganizationUrl = "https://dev.azure.com/testorg",
        }, JsonOptions);

        var connectorId = Guid.NewGuid();
        var responses = new Queue<HttpResponseMessage>();

        // Two successful subscription responses.
        responses.Enqueue(CreateJsonResponse(new AdoServiceHookSubscriptionResponse
        {
            Id = "sub-1",
            Status = "enabled",
            EventType = "workitem.created",
        }));
        responses.Enqueue(CreateJsonResponse(new AdoServiceHookSubscriptionResponse
        {
            Id = "sub-2",
            Status = "enabled",
            EventType = "workitem.updated",
        }));

        var handler = new QueuedResponseHandler(responses);
        var manager = CreateManager(handler);

        var ctx = new WebhookRegistrationContext(
            connectorId, "t1", config, "pat-123", "https://callback.example.com");

        var results = await manager.RegisterAsync(ctx);

        Assert.Equal(2, results.Count);
        Assert.Equal("sub-1", results[0].ExternalSubscriptionId);
        Assert.Equal("workitem.created", results[0].EventType);
        Assert.Equal("sub-2", results[1].ExternalSubscriptionId);
        Assert.Equal("workitem.updated", results[1].EventType);

        // Both should share the same webhook secret.
        Assert.NotNull(results[0].WebhookSecret);
        Assert.Equal(results[0].WebhookSecret, results[1].WebhookSecret);

        // Callback URL should include connector ID.
        Assert.Contains(connectorId.ToString(), results[0].CallbackUrl);
    }

    [Fact]
    public async Task RegisterAsync_HandlesPartialFailure()
    {
        var config = JsonSerializer.Serialize(new AzureDevOpsSourceConfig
        {
            OrganizationUrl = "https://dev.azure.com/testorg",
        }, JsonOptions);

        var responses = new Queue<HttpResponseMessage>();
        responses.Enqueue(CreateJsonResponse(new AdoServiceHookSubscriptionResponse
        {
            Id = "sub-ok",
            EventType = "workitem.created",
        }));
        responses.Enqueue(new HttpResponseMessage(HttpStatusCode.Forbidden)
        {
            Content = new StringContent("Access denied"),
        });

        var manager = CreateManager(new QueuedResponseHandler(responses));
        var ctx = new WebhookRegistrationContext(
            Guid.NewGuid(), "t1", config, "pat-123", "https://callback.example.com");

        var results = await manager.RegisterAsync(ctx);

        // Only one succeeded.
        Assert.Single(results);
        Assert.Equal("sub-ok", results[0].ExternalSubscriptionId);
    }

    [Fact]
    public async Task DeregisterAsync_DeletesAllSubscriptions()
    {
        var config = JsonSerializer.Serialize(new AzureDevOpsSourceConfig
        {
            OrganizationUrl = "https://dev.azure.com/testorg",
        }, JsonOptions);

        var deletedIds = new List<string>();
        var handler = new TrackingDeleteHandler(deletedIds);
        var manager = CreateManager(handler);

        var ctx = new WebhookDeregistrationContext(
            Guid.NewGuid(), "t1", config, "pat-123",
            ["sub-1", "sub-2"]);

        await manager.DeregisterAsync(ctx);

        Assert.Equal(2, deletedIds.Count);
        Assert.Contains("sub-1", deletedIds);
        Assert.Contains("sub-2", deletedIds);
    }

    [Fact]
    public async Task DeregisterAsync_HandlesDeleteFailureGracefully()
    {
        var config = JsonSerializer.Serialize(new AzureDevOpsSourceConfig
        {
            OrganizationUrl = "https://dev.azure.com/testorg",
        }, JsonOptions);

        var responses = new Queue<HttpResponseMessage>();
        responses.Enqueue(new HttpResponseMessage(HttpStatusCode.NotFound));
        responses.Enqueue(new HttpResponseMessage(HttpStatusCode.OK));

        var manager = CreateManager(new QueuedResponseHandler(responses));
        var ctx = new WebhookDeregistrationContext(
            Guid.NewGuid(), "t1", config, "pat-123",
            ["sub-missing", "sub-ok"]);

        // Should not throw.
        await manager.DeregisterAsync(ctx);
    }

    [Fact]
    public void GenerateWebhookSecret_Returns32ByteBase64()
    {
        var secret = AdoWebhookManager.GenerateWebhookSecret();
        Assert.NotNull(secret);

        var bytes = Convert.FromBase64String(secret);
        Assert.Equal(32, bytes.Length);
    }

    [Fact]
    public void GenerateWebhookSecret_IsUnique()
    {
        var s1 = AdoWebhookManager.GenerateWebhookSecret();
        var s2 = AdoWebhookManager.GenerateWebhookSecret();
        Assert.NotEqual(s1, s2);
    }

    // --- Helpers ---

    private static AdoWebhookManager CreateManager(HttpMessageHandler? handler = null)
    {
        var factory = new TestHttpClientFactory(handler ?? new QueuedResponseHandler(new Queue<HttpResponseMessage>()));
        return new AdoWebhookManager(factory, NullLogger<AdoWebhookManager>.Instance);
    }

    private static HttpResponseMessage CreateJsonResponse<T>(T body)
    {
        var json = JsonSerializer.Serialize(body, JsonOptions);
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json"),
        };
    }

    // --- Test doubles ---

    private sealed class TestHttpClientFactory : IHttpClientFactory
    {
        private readonly HttpMessageHandler _handler;
        public TestHttpClientFactory(HttpMessageHandler handler) => _handler = handler;
        public HttpClient CreateClient(string name) => new(_handler, false);
    }

    private sealed class QueuedResponseHandler : HttpMessageHandler
    {
        private readonly Queue<HttpResponseMessage> _responses;
        public QueuedResponseHandler(Queue<HttpResponseMessage> responses) => _responses = responses;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            return Task.FromResult(_responses.Count > 0
                ? _responses.Dequeue()
                : new HttpResponseMessage(HttpStatusCode.InternalServerError));
        }
    }

    private sealed class TrackingDeleteHandler : HttpMessageHandler
    {
        private readonly List<string> _deletedIds;
        public TrackingDeleteHandler(List<string> deletedIds) => _deletedIds = deletedIds;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            if (request.Method == HttpMethod.Delete && request.RequestUri?.AbsolutePath.Contains("/hooks/subscriptions/") == true)
            {
                var segments = request.RequestUri.AbsolutePath.Split('/');
                var id = segments.Last().Split('?')[0];
                _deletedIds.Add(id);
            }
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        }
    }
}
