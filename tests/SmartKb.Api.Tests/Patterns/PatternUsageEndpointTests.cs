using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using SmartKb.Contracts.Models;
using SmartKb.Data;
using SmartKb.Data.Entities;

namespace SmartKb.Api.Tests.Patterns;

public class PatternUsageEndpointTests : IAsyncLifetime
{
    private readonly DistillationTestFactory _factory = new();
    private HttpClient _adminClient = null!;
    private HttpClient _agentClient = null!;
    private HttpClient _unauthClient = null!;

    public async Task InitializeAsync()
    {
        await _factory.InitializeAsync();
        _adminClient = _factory.CreateAuthenticatedClient(roles: "Admin");
        _agentClient = _factory.CreateAuthenticatedClient(roles: "SupportAgent", userId: "agent-1");
        _unauthClient = _factory.CreateClient();
        await _factory.EnsureDatabaseAsync();
    }

    public async Task DisposeAsync()
    {
        _adminClient.Dispose();
        _agentClient.Dispose();
        _unauthClient.Dispose();
        await _factory.DisposeAsync();
    }

    private async Task SeedTraceAsync(string patternId, string userId = "user-1", float confidence = 0.8f, int daysAgo = 0)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SmartKbDbContext>();
        var createdAt = DateTimeOffset.UtcNow.AddDays(-daysAgo);
        db.Set<AnswerTraceEntity>().Add(new AnswerTraceEntity
        {
            Id = Guid.NewGuid(),
            TenantId = "tenant-1",
            UserId = userId,
            CorrelationId = Guid.NewGuid().ToString(),
            Query = "test query",
            ResponseType = "answer",
            Confidence = confidence,
            ConfidenceLabel = "High",
            CitedChunkIds = $"""["{patternId}", "evidence-chunk-1"]""",
            RetrievedChunkIds = "[]",
            HasEvidence = true,
            SystemPromptVersion = "v1",
            CreatedAt = createdAt,
            CreatedAtEpoch = createdAt.ToUnixTimeSeconds(),
        });
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task GetUsage_ReturnsMetrics()
    {
        var patternId = "pattern-usage-test-1";
        await SeedTraceAsync(patternId, "user-1", 0.85f, daysAgo: 1);
        await SeedTraceAsync(patternId, "user-2", 0.75f, daysAgo: 3);

        var resp = await _adminClient.GetAsync($"/api/admin/patterns/{patternId}/usage");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

        var body = await resp.Content.ReadFromJsonAsync<ApiResponse<PatternUsageMetrics>>();
        Assert.NotNull(body);
        Assert.True(body.IsSuccess);
        Assert.NotNull(body.Data);
        Assert.Equal(patternId, body.Data.PatternId);
        Assert.Equal(2, body.Data.TotalCitations);
        Assert.Equal(2, body.Data.UniqueUsers);
        Assert.Equal(30, body.Data.DailyBreakdown.Count);
    }

    [Fact]
    public async Task GetUsage_NoCitations_ReturnsZeros()
    {
        var resp = await _adminClient.GetAsync("/api/admin/patterns/pattern-nonexistent/usage");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

        var body = await resp.Content.ReadFromJsonAsync<ApiResponse<PatternUsageMetrics>>();
        Assert.NotNull(body?.Data);
        Assert.Equal(0, body.Data.TotalCitations);
        Assert.Equal(0, body.Data.UniqueUsers);
        Assert.Null(body.Data.LastCitedAt);
    }

    [Fact]
    public async Task GetUsage_RequiresAuth()
    {
        var resp = await _unauthClient.GetAsync("/api/admin/patterns/pattern-1/usage");
        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
    }

    [Fact]
    public async Task GetUsage_RequiresAdminRole()
    {
        var resp = await _agentClient.GetAsync("/api/admin/patterns/pattern-1/usage");
        Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
    }

    [Fact]
    public async Task GetUsage_TenantIsolation()
    {
        var patternId = "pattern-tenant-isolated";
        await SeedTraceAsync(patternId, "user-1", 0.8f);

        // Query from tenant-2 should not see tenant-1 traces
        var tenant2Client = _factory.CreateAuthenticatedClient(tenantId: "tenant-2", roles: "Admin");
        try
        {
            // Seed tenant-2
            using var scope = _factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<SmartKbDbContext>();
            if (!await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.AnyAsync(
                db.Tenants, t => t.TenantId == "tenant-2"))
            {
                db.Tenants.Add(new TenantEntity
                {
                    TenantId = "tenant-2",
                    DisplayName = "Other",
                    IsActive = true,
                    CreatedAt = DateTimeOffset.UtcNow,
                });
                await db.SaveChangesAsync();
            }

            var resp = await tenant2Client.GetAsync($"/api/admin/patterns/{patternId}/usage");
            Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

            var body = await resp.Content.ReadFromJsonAsync<ApiResponse<PatternUsageMetrics>>();
            Assert.NotNull(body?.Data);
            Assert.Equal(0, body.Data.TotalCitations);
        }
        finally
        {
            tenant2Client.Dispose();
        }
    }
}
