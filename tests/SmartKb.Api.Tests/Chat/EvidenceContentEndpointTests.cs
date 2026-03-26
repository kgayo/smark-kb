using System.Net;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using SmartKb.Api.Tests.Auth;
using SmartKb.Contracts.Enums;
using SmartKb.Data;
using SmartKb.Data.Entities;

namespace SmartKb.Api.Tests.Chat;

public sealed class EvidenceContentEndpointTests : IClassFixture<AuthTestFactory>
{
    private readonly AuthTestFactory _factory;

    public EvidenceContentEndpointTests(AuthTestFactory factory)
    {
        _factory = factory;
    }

    private HttpClient CreateClient(string role = "SupportAgent", string tenantId = "tenant-1")
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.AuthenticatedHeader, "true");
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, tenantId);
        client.DefaultRequestHeaders.Add(TestAuthHandler.RolesHeader, role);
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserIdHeader, "test-user");
        return client;
    }

    private async Task SeedChunkAsync(
        string chunkId,
        string tenantId,
        string visibility = "Internal",
        string? allowedGroups = null,
        string sourceType = "Ticket")
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SmartKbDbContext>();

        var connectorId = Guid.NewGuid();
        if (!db.Connectors.Any(c => c.Id == connectorId))
        {
            db.Connectors.Add(new ConnectorEntity
            {
                Id = connectorId,
                TenantId = tenantId,
                Name = $"test-{chunkId}",
                ConnectorType = ConnectorType.AzureDevOps,
                Status = ConnectorStatus.Enabled,
                AuthType = SecretAuthType.Pat,
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow,
            });
        }

        db.EvidenceChunks.Add(new EvidenceChunkEntity
        {
            ChunkId = chunkId,
            EvidenceId = $"ev-{chunkId}",
            TenantId = tenantId,
            ConnectorId = connectorId,
            ChunkIndex = 0,
            ChunkText = "This is the full chunk text content for testing.",
            ChunkContext = "Root > Section > Subsection",
            SourceSystem = "AzureDevOps",
            SourceType = sourceType,
            Status = "Active",
            UpdatedAt = DateTimeOffset.UtcNow,
            ProductArea = "Authentication",
            Tags = "[\"auth\",\"login\"]",
            Visibility = visibility,
            AllowedGroups = allowedGroups,
            AccessLabel = visibility,
            Title = "Test Evidence Title",
            SourceUrl = "https://dev.azure.com/test/item/1",
            ContentHash = "abc123",
            CreatedAt = DateTimeOffset.UtcNow,
        });

        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task GetEvidenceContent_ReturnsContent_ForInternalChunk()
    {
        var chunkId = $"chunk-int-{Guid.NewGuid():N}";
        await SeedChunkAsync(chunkId, "tenant-1");

        var client = CreateClient();
        var response = await client.GetAsync($"/api/evidence/{chunkId}/content");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadAsStringAsync();
        var json = JsonDocument.Parse(body);
        var data = json.RootElement.GetProperty("data");

        Assert.Equal(chunkId, data.GetProperty("chunkId").GetString());
        Assert.Equal("Test Evidence Title", data.GetProperty("title").GetString());
        Assert.Equal("This is the full chunk text content for testing.", data.GetProperty("chunkText").GetString());
        Assert.Equal("Root > Section > Subsection", data.GetProperty("chunkContext").GetString());
        Assert.Equal("Ticket", data.GetProperty("sourceType").GetString());
        Assert.Equal("AzureDevOps", data.GetProperty("sourceSystem").GetString());
        Assert.Equal("Authentication", data.GetProperty("productArea").GetString());

        var tags = data.GetProperty("tags");
        Assert.Equal(2, tags.GetArrayLength());
    }

    [Fact]
    public async Task GetEvidenceContent_Returns404_WhenChunkNotFound()
    {
        var client = CreateClient();
        var response = await client.GetAsync("/api/evidence/nonexistent-chunk/content");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetEvidenceContent_Returns404_CrossTenantIsolation()
    {
        var chunkId = $"chunk-iso-{Guid.NewGuid():N}";
        await SeedChunkAsync(chunkId, "tenant-1");

        // Request from tenant-other should not see tenant-1's chunk
        var client = CreateClient(tenantId: "tenant-other");
        var response = await client.GetAsync($"/api/evidence/{chunkId}/content");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetEvidenceContent_Returns404_ForRestrictedChunk_WhenNoGroupAccess()
    {
        var chunkId = $"chunk-restr-{Guid.NewGuid():N}";
        await SeedChunkAsync(chunkId, "tenant-1", visibility: "Restricted", allowedGroups: "[\"admin-group\"]");

        // Default test user has no groups
        var client = CreateClient();
        var response = await client.GetAsync($"/api/evidence/{chunkId}/content");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetEvidenceContent_ReturnsContent_ForPublicChunk()
    {
        var chunkId = $"chunk-pub-{Guid.NewGuid():N}";
        await SeedChunkAsync(chunkId, "tenant-1", visibility: "Public");

        var client = CreateClient();
        var response = await client.GetAsync($"/api/evidence/{chunkId}/content");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadAsStringAsync();
        var json = JsonDocument.Parse(body);
        Assert.Equal(chunkId, json.RootElement.GetProperty("data").GetProperty("chunkId").GetString());
    }

    [Fact]
    public async Task GetEvidenceContent_Returns401_WhenUnauthenticated()
    {
        var client = _factory.CreateClient();
        var response = await client.GetAsync("/api/evidence/any-chunk/content");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetEvidenceContent_RawContentNull_WhenNoBlobSnapshot()
    {
        var chunkId = $"chunk-noraw-{Guid.NewGuid():N}";
        await SeedChunkAsync(chunkId, "tenant-1");

        var client = CreateClient();
        var response = await client.GetAsync($"/api/evidence/{chunkId}/content");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadAsStringAsync();
        var json = JsonDocument.Parse(body);
        var data = json.RootElement.GetProperty("data");

        Assert.Equal(JsonValueKind.Null, data.GetProperty("rawContent").ValueKind);
        Assert.Equal(JsonValueKind.Null, data.GetProperty("contentType").ValueKind);
    }

    [Fact]
    public async Task GetEvidenceContent_ReturnsSourceType_WikiPage()
    {
        var chunkId = $"chunk-wiki-{Guid.NewGuid():N}";
        await SeedChunkAsync(chunkId, "tenant-1", sourceType: "WikiPage");

        var client = CreateClient();
        var response = await client.GetAsync($"/api/evidence/{chunkId}/content");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadAsStringAsync();
        var json = JsonDocument.Parse(body);
        Assert.Equal("WikiPage", json.RootElement.GetProperty("data").GetProperty("sourceType").GetString());
    }
}
