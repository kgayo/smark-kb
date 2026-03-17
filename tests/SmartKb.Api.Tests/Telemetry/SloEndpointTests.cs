using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using SmartKb.Api.Tests.Auth;

namespace SmartKb.Api.Tests.Telemetry;

public sealed class SloEndpointTests : IClassFixture<AuthTestFactory>
{
    private readonly AuthTestFactory _factory;

    public SloEndpointTests(AuthTestFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task SloStatus_ReturnsTargets_ForAdmin()
    {
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
        });

        client.DefaultRequestHeaders.Add("X-Test-Auth", "true");
        client.DefaultRequestHeaders.Add("X-Test-Roles", "Admin");
        client.DefaultRequestHeaders.Add("X-Test-Tenant", "tenant-slo");

        var response = await client.GetAsync("/api/admin/slo/status");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadAsStringAsync();
        var json = JsonDocument.Parse(body);
        var data = json.RootElement.GetProperty("data");

        // Verify targets block exists with expected SLO values.
        var targets = data.GetProperty("targets");
        Assert.Equal(8000, targets.GetProperty("answerLatencyP95TargetMs").GetInt32());
        Assert.Equal(99.5, targets.GetProperty("availabilityTargetPercent").GetDouble());
        Assert.Equal(15, targets.GetProperty("syncLagP95TargetMinutes").GetInt32());
        Assert.Equal(0.25, targets.GetProperty("noEvidenceRateThreshold").GetDouble());
        Assert.Equal(10, targets.GetProperty("deadLetterDepthThreshold").GetInt32());

        // Verify metrics block lists all custom metric names.
        var metrics = data.GetProperty("metrics");
        Assert.Equal("smartkb.chat.latency_ms", metrics.GetProperty("chatLatencyMetric").GetString());
        Assert.Equal("smartkb.chat.requests_total", metrics.GetProperty("chatRequestsMetric").GetString());
        Assert.Equal("smartkb.chat.no_evidence_total", metrics.GetProperty("chatNoEvidenceMetric").GetString());
        Assert.Equal("smartkb.ingestion.sync_duration_ms", metrics.GetProperty("syncDurationMetric").GetString());
        Assert.Equal("smartkb.ingestion.dead_letter_total", metrics.GetProperty("deadLetterMetric").GetString());
    }

    [Fact]
    public async Task SloStatus_RequiresConnectorManagePermission()
    {
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
        });

        client.DefaultRequestHeaders.Add("X-Test-Auth", "true");
        client.DefaultRequestHeaders.Add("X-Test-Roles", "SupportAgent");
        client.DefaultRequestHeaders.Add("X-Test-Tenant", "tenant-slo");

        var response = await client.GetAsync("/api/admin/slo/status");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task SloStatus_RequiresAuthentication()
    {
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
        });

        var response = await client.GetAsync("/api/admin/slo/status");
        // Unauthenticated requests should be rejected.
        Assert.NotEqual(HttpStatusCode.OK, response.StatusCode);
    }
}
