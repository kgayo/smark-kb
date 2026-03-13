using System.Net;
using System.Net.Http.Json;
using SmartKb.Api.Tests.Auth;
using SmartKb.Contracts.Models;

namespace SmartKb.Api.Tests;

public class HealthEndpointTests : IClassFixture<AuthTestFactory>
{
    private readonly HttpClient _client;

    public HealthEndpointTests(AuthTestFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task HealthCheck_ReturnsHealthy()
    {
        var response = await _client.GetAsync("/healthz");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task ApiHealth_ReturnsHealthStatus()
    {
        var response = await _client.GetAsync("/api/health");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var health = await response.Content.ReadFromJsonAsync<HealthStatus>();
        Assert.NotNull(health);
        Assert.Equal("SmartKb.Api", health.Service);
        Assert.Equal("Healthy", health.Status);
        Assert.False(string.IsNullOrEmpty(health.Version));
    }

    [Fact]
    public async Task Root_ReturnsRunningStatus()
    {
        var response = await _client.GetAsync("/");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
