using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace SmartKb.Api.Tests.Auth;

public sealed class EntraIdStartupValidationTests
{
    [Theory]
    [InlineData("", "")]
    [InlineData(null, null)]
    [InlineData("<placeholder>", "<placeholder>")]
    public void Production_WithoutEntraIdConfig_ThrowsOnStartup(string? clientId, string? tenantId)
    {
        var ex = Assert.Throws<InvalidOperationException>(() =>
        {
            using var factory = new WebApplicationFactory<Program>()
                .WithWebHostBuilder(builder =>
                {
                    builder.UseEnvironment(Environments.Production);
                    builder.ConfigureAppConfiguration((_, config) =>
                    {
                        config.AddInMemoryCollection(new Dictionary<string, string?>
                        {
                            ["AzureAd:ClientId"] = clientId,
                            ["AzureAd:TenantId"] = tenantId,
                        });
                    });
                });
            // Force host build
            _ = factory.Server;
        });

        Assert.Contains("Entra ID authentication must be configured in Production", ex.Message);
    }

    [Fact]
    public void Development_WithoutEntraIdConfig_StartsSuccessfully()
    {
        using var factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment(Environments.Development);
                builder.ConfigureAppConfiguration((_, config) =>
                {
                    config.AddInMemoryCollection(new Dictionary<string, string?>
                    {
                        ["AzureAd:ClientId"] = "<placeholder>",
                        ["AzureAd:TenantId"] = "<placeholder>",
                    });
                });
            });

        // Should not throw — development mode allows fallback auth
        var server = factory.Server;
        Assert.NotNull(server);
    }
}
