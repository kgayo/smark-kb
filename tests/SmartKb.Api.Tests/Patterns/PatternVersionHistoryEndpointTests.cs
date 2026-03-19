using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using SmartKb.Contracts.Models;
using SmartKb.Data;
using SmartKb.Data.Entities;

namespace SmartKb.Api.Tests.Patterns;

public class PatternVersionHistoryEndpointTests : IAsyncLifetime
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

    private async Task<string> SeedPatternAndReviewAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SmartKbDbContext>();
        var entity = new CasePatternEntity
        {
            Id = Guid.NewGuid(),
            PatternId = $"pattern-hist-{Guid.NewGuid():N}",
            TenantId = "tenant-1",
            Title = "History Test Pattern",
            ProblemStatement = "Test problem",
            SymptomsJson = "[]",
            DiagnosisStepsJson = "[]",
            ResolutionStepsJson = "[]",
            VerificationStepsJson = "[]",
            EscalationCriteriaJson = "[]",
            RelatedEvidenceIdsJson = "[]",
            ApplicabilityConstraintsJson = "[]",
            ExclusionsJson = "[]",
            TagsJson = "[]",
            AllowedGroupsJson = "[]",
            Confidence = 0.7f,
            TrustLevel = "Draft",
            Version = 1,
            Visibility = "Internal",
            AccessLabel = "Internal",
            SourceUrl = "session://test",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        };
        db.CasePatterns.Add(entity);
        await db.SaveChangesAsync();

        // Perform a review to create a history entry.
        var reviewResp = await _adminClient.PostAsJsonAsync(
            $"/api/patterns/{entity.PatternId}/review",
            new ReviewPatternRequest { Notes = "Looks good" });
        reviewResp.EnsureSuccessStatusCode();

        return entity.PatternId;
    }

    [Fact]
    public async Task GetHistory_ReturnsHistoryEntries()
    {
        var patternId = await SeedPatternAndReviewAsync();

        var resp = await _adminClient.GetAsync($"/api/patterns/{patternId}/history");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

        var body = await resp.Content.ReadFromJsonAsync<ApiResponse<PatternVersionHistoryResponse>>();
        Assert.NotNull(body?.Data);
        Assert.Equal(patternId, body.Data.PatternId);
        Assert.Single(body.Data.Entries);
        Assert.Equal("trust_transition", body.Data.Entries[0].ChangeType);
        Assert.Equal("Draft → Reviewed", body.Data.Entries[0].Summary);
    }

    [Fact]
    public async Task GetHistory_Returns404_WhenPatternNotFound()
    {
        var resp = await _adminClient.GetAsync("/api/patterns/nonexistent-pattern/history");
        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
    }

    [Fact]
    public async Task GetHistory_RequiresAuth()
    {
        var resp = await _unauthClient.GetAsync("/api/patterns/pattern-1/history");
        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
    }

    [Fact]
    public async Task GetHistory_RequiresPatternApprovePermission()
    {
        var resp = await _agentClient.GetAsync("/api/patterns/pattern-1/history");
        Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
    }

    [Fact]
    public async Task GetHistory_TenantIsolation()
    {
        var patternId = await SeedPatternAndReviewAsync();

        // Seed tenant-2.
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

        var tenant2Client = _factory.CreateAuthenticatedClient(tenantId: "tenant-2", roles: "Admin");
        try
        {
            var resp = await tenant2Client.GetAsync($"/api/patterns/{patternId}/history");
            Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
        }
        finally
        {
            tenant2Client.Dispose();
        }
    }
}
