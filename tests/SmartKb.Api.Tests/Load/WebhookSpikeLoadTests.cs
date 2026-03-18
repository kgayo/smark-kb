using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

namespace SmartKb.Api.Tests.Load;

/// <summary>
/// Load tests for webhook spikes: concurrent ADO webhook hits,
/// SharePoint Graph notification bursts, and mixed webhook traffic.
/// Webhook endpoints are anonymous — tests verify the system handles
/// high-volume unsigned/invalid payloads gracefully (no crashes, no 500s).
/// </summary>
[Collection("LoadTests")]
public class WebhookSpikeLoadTests : IAsyncLifetime
{
    private readonly LoadTestFactory _factory = new();
    private HttpClient _client = null!;

    public async Task InitializeAsync()
    {
        await _factory.InitializeAsync();
        _client = _factory.CreateClient(); // Anonymous — webhook endpoints are AllowAnonymous.
        await _factory.EnsureDatabaseAsync();
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
    }

    [Fact]
    public async Task AdoWebhookSpike_50Concurrent_NoServerErrors()
    {
        var connectorId = Guid.NewGuid();
        const int count = 50;

        var tasks = Enumerable.Range(0, count).Select(async i =>
        {
            using var client = _factory.CreateClient();
            var payload = JsonSerializer.Serialize(new
            {
                eventType = "workitem.updated",
                resource = new { id = i, workItemId = 1000 + i, rev = 1 },
                resourceVersion = "1.0",
            });
            var content = new StringContent(payload, Encoding.UTF8, "application/json");
            var response = await client.PostAsync($"/api/webhooks/ado/{connectorId}", content);
            return response;
        });

        var responses = await Task.WhenAll(tasks);

        // Expect 401 (no auth header) or 404 (connector not found) — never 500.
        Assert.All(responses, r =>
            Assert.True(r.StatusCode != HttpStatusCode.InternalServerError,
                $"Got 500 Internal Server Error on ADO webhook spike"));
    }

    [Fact]
    public async Task SharePointWebhookSpike_50Concurrent_NoServerErrors()
    {
        var connectorId = Guid.NewGuid();
        const int count = 50;

        var tasks = Enumerable.Range(0, count).Select(async i =>
        {
            using var client = _factory.CreateClient();
            var payload = JsonSerializer.Serialize(new
            {
                value = new[]
                {
                    new
                    {
                        subscriptionId = Guid.NewGuid().ToString(),
                        clientState = "invalid-state",
                        resource = $"drives/test-drive/items/item-{i}",
                        changeType = "updated",
                    },
                },
            });
            var content = new StringContent(payload, Encoding.UTF8, "application/json");
            var response = await client.PostAsync($"/api/webhooks/msgraph/{connectorId}", content);
            return response;
        });

        var responses = await Task.WhenAll(tasks);

        // Expect 401 (clientState mismatch) or 404 — never 500.
        Assert.All(responses, r =>
            Assert.True(r.StatusCode != HttpStatusCode.InternalServerError,
                $"Got 500 Internal Server Error on SharePoint webhook spike"));
    }

    [Fact]
    public async Task GraphValidationHandshake_30Concurrent_AllReturnValidationToken()
    {
        var connectorId = Guid.NewGuid();
        const int count = 30;

        var tasks = Enumerable.Range(0, count).Select(async i =>
        {
            using var client = _factory.CreateClient();
            var token = $"validation-token-{i}";
            var content = new StringContent("{}", Encoding.UTF8, "application/json");
            var response = await client.PostAsync(
                $"/api/webhooks/msgraph/{connectorId}?validationToken={token}", content);
            var body = await response.Content.ReadAsStringAsync();
            return (response.StatusCode, body, token);
        });

        var results = await Task.WhenAll(tasks);

        // Graph validation handshake should echo back the token with 200.
        Assert.All(results, r =>
        {
            Assert.Equal(HttpStatusCode.OK, r.StatusCode);
            Assert.Equal(r.token, r.body);
        });
    }

    [Fact]
    public async Task HubSpotWebhookSpike_30Concurrent_NoServerErrors()
    {
        var connectorId = Guid.NewGuid();
        const int count = 30;

        var tasks = Enumerable.Range(0, count).Select(async i =>
        {
            using var client = _factory.CreateClient();
            var payload = JsonSerializer.Serialize(new[]
            {
                new
                {
                    eventId = 100 + i,
                    subscriptionType = "deal.propertyChange",
                    objectId = 5000 + i,
                },
            });
            var content = new StringContent(payload, Encoding.UTF8, "application/json");
            client.DefaultRequestHeaders.Add("X-HubSpot-Signature-v3", "invalid-sig");
            client.DefaultRequestHeaders.Add("X-HubSpot-Request-Timestamp", DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString());
            var response = await client.PostAsync($"/api/webhooks/hubspot/{connectorId}", content);
            return response;
        });

        var responses = await Task.WhenAll(tasks);

        Assert.All(responses, r =>
            Assert.True(r.StatusCode != HttpStatusCode.InternalServerError,
                $"Got 500 Internal Server Error on HubSpot webhook spike"));
    }

    [Fact]
    public async Task ClickUpWebhookSpike_30Concurrent_NoServerErrors()
    {
        var connectorId = Guid.NewGuid();
        const int count = 30;

        var tasks = Enumerable.Range(0, count).Select(async i =>
        {
            using var client = _factory.CreateClient();
            var payload = JsonSerializer.Serialize(new
            {
                @event = "taskUpdated",
                task_id = $"task-{i}",
                webhook_id = Guid.NewGuid().ToString(),
            });
            var content = new StringContent(payload, Encoding.UTF8, "application/json");
            client.DefaultRequestHeaders.Add("X-Signature", "invalid-signature");
            var response = await client.PostAsync($"/api/webhooks/clickup/{connectorId}", content);
            return response;
        });

        var responses = await Task.WhenAll(tasks);

        Assert.All(responses, r =>
            Assert.True(r.StatusCode != HttpStatusCode.InternalServerError,
                $"Got 500 Internal Server Error on ClickUp webhook spike"));
    }

    [Fact]
    public async Task MixedWebhookTraffic_AllProviders_60Concurrent_NoServerErrors()
    {
        var connectorId = Guid.NewGuid();

        var adoTasks = Enumerable.Range(0, 15).Select(async i =>
        {
            using var client = _factory.CreateClient();
            var payload = $@"{{""eventType"":""workitem.created"",""resource"":{{""id"":{i}}}}}";
            var content = new StringContent(payload, Encoding.UTF8, "application/json");
            return await client.PostAsync($"/api/webhooks/ado/{connectorId}", content);
        });

        var spTasks = Enumerable.Range(0, 15).Select(async i =>
        {
            using var client = _factory.CreateClient();
            var payload = $@"{{""value"":[{{""subscriptionId"":""{Guid.NewGuid()}"",""clientState"":""bad"",""resource"":""drives/d/items/{i}"",""changeType"":""updated""}}]}}";
            var content = new StringContent(payload, Encoding.UTF8, "application/json");
            return await client.PostAsync($"/api/webhooks/msgraph/{connectorId}", content);
        });

        var hsTasks = Enumerable.Range(0, 15).Select(async i =>
        {
            using var client = _factory.CreateClient();
            var payload = $@"[{{""eventId"":{i},""subscriptionType"":""deal.creation"",""objectId"":{i}}}]";
            var content = new StringContent(payload, Encoding.UTF8, "application/json");
            client.DefaultRequestHeaders.Add("X-HubSpot-Signature-v3", "bad");
            client.DefaultRequestHeaders.Add("X-HubSpot-Request-Timestamp", "0");
            return await client.PostAsync($"/api/webhooks/hubspot/{connectorId}", content);
        });

        var cuTasks = Enumerable.Range(0, 15).Select(async i =>
        {
            using var client = _factory.CreateClient();
            var payload = $@"{{""event"":""taskCreated"",""task_id"":""t-{i}""}}";
            var content = new StringContent(payload, Encoding.UTF8, "application/json");
            client.DefaultRequestHeaders.Add("X-Signature", "bad");
            return await client.PostAsync($"/api/webhooks/clickup/{connectorId}", content);
        });

        var allResponses = await Task.WhenAll(
            Task.WhenAll(adoTasks),
            Task.WhenAll(spTasks),
            Task.WhenAll(hsTasks),
            Task.WhenAll(cuTasks));

        var flattened = allResponses.SelectMany(r => r).ToList();
        Assert.Equal(60, flattened.Count);
        Assert.All(flattened, r =>
            Assert.True(r.StatusCode != HttpStatusCode.InternalServerError,
                $"Got 500 on mixed webhook spike"));
    }

    [Fact]
    public async Task WebhookThroughput_100Sequential_CompletesWithinBudget()
    {
        var connectorId = Guid.NewGuid();
        const int count = 100;
        var sw = Stopwatch.StartNew();

        for (var i = 0; i < count; i++)
        {
            var payload = $@"{{""eventType"":""workitem.updated"",""resource"":{{""id"":{i}}}}}";
            var content = new StringContent(payload, Encoding.UTF8, "application/json");
            var response = await _client.PostAsync($"/api/webhooks/ado/{connectorId}", content);
            Assert.True(response.StatusCode != HttpStatusCode.InternalServerError);
        }

        sw.Stop();
        Assert.True(sw.Elapsed.TotalSeconds < 30,
            $"100 sequential webhook calls took {sw.Elapsed.TotalSeconds:F1}s (budget: 30s)");
    }
}
