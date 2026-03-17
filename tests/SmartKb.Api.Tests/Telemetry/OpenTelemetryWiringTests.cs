using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using OpenTelemetry.Trace;
using SmartKb.Api.Tests.Auth;

namespace SmartKb.Api.Tests.Telemetry;

public sealed class OpenTelemetryWiringTests : IClassFixture<AuthTestFactory>
{
    private readonly AuthTestFactory _factory;

    public OpenTelemetryWiringTests(AuthTestFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public void TracerProvider_IsRegistered()
    {
        var provider = _factory.Services.GetService<TracerProvider>();
        Assert.NotNull(provider);
    }

    [Fact]
    public async Task Endpoint_ReturnsCorrelationIdHeader_WhenAuthenticatedWithCorrelationId()
    {
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
        });

        // Set test auth headers (TestAuthHandler requires X-Test-Auth).
        client.DefaultRequestHeaders.Add("X-Test-Auth", "true");
        client.DefaultRequestHeaders.Add("X-Test-Roles", "SupportAgent");
        client.DefaultRequestHeaders.Add("X-Test-Tenant", "tenant-1");
        client.DefaultRequestHeaders.Add("X-Correlation-Id", "test-correlation-999");

        var response = await client.GetAsync("/api/sessions");

        // The middleware echoes X-Correlation-Id in response headers for authenticated requests.
        Assert.True(response.Headers.Contains("X-Correlation-Id"),
            "Response should contain X-Correlation-Id header.");
        Assert.Equal("test-correlation-999",
            response.Headers.GetValues("X-Correlation-Id").First());
    }
}
