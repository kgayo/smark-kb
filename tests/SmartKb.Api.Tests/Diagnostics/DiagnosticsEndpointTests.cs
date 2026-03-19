using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using SmartKb.Api.Tests.Auth;
using SmartKb.Contracts.Enums;
using SmartKb.Data;
using SmartKb.Data.Entities;

namespace SmartKb.Api.Tests.Diagnostics;

public sealed class DiagnosticsEndpointTests : IClassFixture<AuthTestFactory>
{
    private readonly AuthTestFactory _factory;

    public DiagnosticsEndpointTests(AuthTestFactory factory)
    {
        _factory = factory;
        SeedData();
    }

    private Guid _connectorId;

    private void SeedData()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SmartKbDbContext>();

        if (db.Connectors.Any(c => c.TenantId == "tenant-1" && c.Name == "DiagTest"))
            return;

        var now = DateTimeOffset.UtcNow;
        var connector = new ConnectorEntity
        {
            Id = Guid.NewGuid(),
            TenantId = "tenant-1",
            Name = "DiagTest",
            ConnectorType = ConnectorType.AzureDevOps,
            Status = ConnectorStatus.Enabled,
            AuthType = SecretAuthType.Pat,
            CreatedAt = now,
            UpdatedAt = now,
        };
        db.Connectors.Add(connector);

        db.WebhookSubscriptions.Add(new WebhookSubscriptionEntity
        {
            Id = Guid.NewGuid(),
            ConnectorId = connector.Id,
            TenantId = "tenant-1",
            EventType = "workitem.created",
            CallbackUrl = "https://test/callback",
            IsActive = true,
            PollingFallbackActive = false,
            ConsecutiveFailures = 0,
            CreatedAt = now,
            UpdatedAt = now,
        });

        db.SaveChanges();
        _connectorId = connector.Id;
    }

    private HttpClient CreateAdminClient(string tenant = "tenant-1")
    {
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        client.DefaultRequestHeaders.Add("X-Test-Auth", "true");
        client.DefaultRequestHeaders.Add("X-Test-Roles", "Admin");
        client.DefaultRequestHeaders.Add("X-Test-Tenant", tenant);
        return client;
    }

    [Fact]
    public async Task GetWebhooksByConnector_ReturnsOk_ForAdmin()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SmartKbDbContext>();
        var conn = db.Connectors.First(c => c.Name == "DiagTest");

        var client = CreateAdminClient();
        var response = await client.GetAsync($"/api/admin/connectors/{conn.Id}/webhooks");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        var json = JsonDocument.Parse(body);
        var data = json.RootElement.GetProperty("data");
        Assert.True(data.GetProperty("totalCount").GetInt32() >= 1);
    }

    [Fact]
    public async Task GetAllWebhooks_ReturnsOk_ForAdmin()
    {
        var client = CreateAdminClient();
        var response = await client.GetAsync("/api/admin/webhooks");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        var json = JsonDocument.Parse(body);
        var data = json.RootElement.GetProperty("data");
        Assert.True(data.GetProperty("totalCount").GetInt32() >= 0);
        Assert.True(data.TryGetProperty("subscriptions", out _));
        Assert.True(data.TryGetProperty("activeCount", out _));
        Assert.True(data.TryGetProperty("fallbackCount", out _));
    }

    [Fact]
    public async Task GetDiagnosticsSummary_ReturnsOk_ForAdmin()
    {
        var client = CreateAdminClient();
        var response = await client.GetAsync("/api/admin/diagnostics/summary");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        var json = JsonDocument.Parse(body);
        var data = json.RootElement.GetProperty("data");

        Assert.True(data.TryGetProperty("totalConnectors", out _));
        Assert.True(data.TryGetProperty("enabledConnectors", out _));
        Assert.True(data.TryGetProperty("totalWebhooks", out _));
        Assert.True(data.TryGetProperty("activeWebhooks", out _));
        Assert.True(data.TryGetProperty("fallbackWebhooks", out _));
        Assert.True(data.TryGetProperty("connectorHealth", out _));
        Assert.True(data.TryGetProperty("serviceBusConfigured", out _));
        Assert.True(data.TryGetProperty("keyVaultConfigured", out _));
        Assert.True(data.TryGetProperty("openAiConfigured", out _));
        Assert.True(data.TryGetProperty("searchServiceConfigured", out _));
    }

    [Fact]
    public async Task GetDiagnosticsSummary_RequiresConnectorManage()
    {
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        client.DefaultRequestHeaders.Add("X-Test-Auth", "true");
        client.DefaultRequestHeaders.Add("X-Test-Roles", "SupportAgent");
        client.DefaultRequestHeaders.Add("X-Test-Tenant", "tenant-1");

        var response = await client.GetAsync("/api/admin/diagnostics/summary");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task GetAllWebhooks_RequiresConnectorManage()
    {
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        client.DefaultRequestHeaders.Add("X-Test-Auth", "true");
        client.DefaultRequestHeaders.Add("X-Test-Roles", "SupportAgent");
        client.DefaultRequestHeaders.Add("X-Test-Tenant", "tenant-1");

        var response = await client.GetAsync("/api/admin/webhooks");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task GetWebhooksByConnector_RequiresAuth()
    {
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var response = await client.GetAsync($"/api/admin/connectors/{Guid.NewGuid()}/webhooks");
        Assert.NotEqual(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task SloStatus_IncludesRateLimitMetric()
    {
        var client = CreateAdminClient();
        var response = await client.GetAsync("/api/admin/slo/status");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        var json = JsonDocument.Parse(body);
        var metrics = json.RootElement.GetProperty("data").GetProperty("metrics");
        Assert.Equal("smartkb.ingestion.source_rate_limit_total",
            metrics.GetProperty("sourceRateLimitMetric").GetString());
    }

    [Fact]
    public async Task SloStatus_IncludesRateLimitAlertThreshold()
    {
        var client = CreateAdminClient();
        var response = await client.GetAsync("/api/admin/slo/status");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        var json = JsonDocument.Parse(body);
        var targets = json.RootElement.GetProperty("data").GetProperty("targets");
        Assert.True(targets.TryGetProperty("rateLimitAlertThreshold", out var threshold));
        Assert.True(threshold.GetInt32() > 0);
        Assert.True(targets.TryGetProperty("rateLimitAlertWindowMinutes", out var window));
        Assert.True(window.GetInt32() > 0);
    }

    [Fact]
    public async Task GetRateLimitAlerts_ReturnsOk_ForAdmin()
    {
        var client = CreateAdminClient();
        var response = await client.GetAsync("/api/admin/diagnostics/rate-limit-alerts");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        var json = JsonDocument.Parse(body);
        var data = json.RootElement.GetProperty("data");
        Assert.True(data.TryGetProperty("totalAlertingConnectors", out _));
        Assert.True(data.TryGetProperty("alerts", out _));
    }

    [Fact]
    public async Task GetRateLimitAlerts_RequiresConnectorManage()
    {
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        client.DefaultRequestHeaders.Add("X-Test-Auth", "true");
        client.DefaultRequestHeaders.Add("X-Test-Roles", "SupportAgent");
        client.DefaultRequestHeaders.Add("X-Test-Tenant", "tenant-1");

        var response = await client.GetAsync("/api/admin/diagnostics/rate-limit-alerts");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task GetRateLimitAlerts_RequiresAuth()
    {
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var response = await client.GetAsync("/api/admin/diagnostics/rate-limit-alerts");
        Assert.NotEqual(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task GetDiagnosticsSummary_IncludesRateLimitAlertingConnectors()
    {
        var client = CreateAdminClient();
        var response = await client.GetAsync("/api/admin/diagnostics/summary");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        var json = JsonDocument.Parse(body);
        var data = json.RootElement.GetProperty("data");
        Assert.True(data.TryGetProperty("rateLimitAlertingConnectors", out _));

        // Connector health should include rate-limit fields.
        var health = data.GetProperty("connectorHealth");
        if (health.GetArrayLength() > 0)
        {
            var first = health[0];
            Assert.True(first.TryGetProperty("rateLimitHits", out _));
            Assert.True(first.TryGetProperty("rateLimitAlerting", out _));
        }
    }
}
