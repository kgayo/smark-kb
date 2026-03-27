using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using SmartKb.Contracts.Enums;
using SmartKb.Contracts.Models;
using SmartKb.Data;
using SmartKb.Data.Entities;

namespace SmartKb.Api.Tests.Patterns;

public class GovernanceEndpointTests : IAsyncLifetime
{
    private readonly DistillationTestFactory _factory = new();
    private HttpClient _adminClient = null!;
    private HttpClient _leadClient = null!;

    public async Task InitializeAsync()
    {
        await _factory.InitializeAsync();
        _adminClient = _factory.CreateAuthenticatedClient(roles: "Admin");
        _leadClient = _factory.CreateAuthenticatedClient(roles: "SupportLead", userId: "lead-1");
        await _factory.EnsureDatabaseAsync();
    }

    public async Task DisposeAsync()
    {
        _adminClient.Dispose();
        _leadClient.Dispose();
        await _factory.DisposeAsync();
    }

    private async Task<string> SeedPatternAsync(
        TrustLevel trustLevel = TrustLevel.Draft,
        string? productArea = null)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SmartKbDbContext>();

        var patternId = $"pattern-{Guid.NewGuid():N}";
        db.CasePatterns.Add(new CasePatternEntity
        {
            Id = Guid.NewGuid(),
            PatternId = patternId,
            TenantId = "tenant-1",
            Title = "Test Pattern",
            ProblemStatement = "A test problem",
            SymptomsJson = "[\"symptom1\"]",
            DiagnosisStepsJson = "[]",
            ResolutionStepsJson = "[\"step1\"]",
            VerificationStepsJson = "[]",
            EscalationCriteriaJson = "[]",
            RelatedEvidenceIdsJson = "[\"ev1\"]",
            Confidence = 0.6f,
            TrustLevel = trustLevel.ToString(),
            Version = 1,
            ProductArea = productArea,
            TagsJson = "[]",
            Visibility = "Internal",
            AllowedGroupsJson = "[]",
            AccessLabel = "Internal",
            SourceUrl = "session://test",
            ApplicabilityConstraintsJson = "[]",
            ExclusionsJson = "[]",
            CreatedAt = DateTimeOffset.UtcNow,
            CreatedAtEpoch = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            UpdatedAt = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync();
        return patternId;
    }

    // ── Queue endpoint ──

    [Fact]
    public async Task GovernanceQueue_ReturnsPatterns()
    {
        await SeedPatternAsync();

        var response = await _adminClient.GetAsync("/api/patterns/governance-queue");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<ApiResponse<PatternGovernanceQueueResponse>>();
        Assert.True(body!.IsSuccess);
        Assert.Equal(1, body.Data!.TotalCount);
    }

    [Fact]
    public async Task GovernanceQueue_FiltersByTrustLevel()
    {
        await SeedPatternAsync(TrustLevel.Draft);
        await SeedPatternAsync(TrustLevel.Approved);

        var response = await _adminClient.GetAsync("/api/patterns/governance-queue?trustLevel=Draft");

        var body = await response.Content.ReadFromJsonAsync<ApiResponse<PatternGovernanceQueueResponse>>();
        Assert.Equal(1, body!.Data!.TotalCount);
        Assert.All(body.Data.Patterns, p => Assert.Equal("Draft", p.TrustLevel));
    }

    // ── Detail endpoint ──

    [Fact]
    public async Task GetPatternDetail_ReturnsPattern()
    {
        var patternId = await SeedPatternAsync();

        var response = await _adminClient.GetAsync($"/api/patterns/{patternId}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<ApiResponse<PatternDetail>>();
        Assert.True(body!.IsSuccess);
        Assert.Equal(patternId, body.Data!.PatternId);
    }

    [Fact]
    public async Task GetPatternDetail_Returns404_WhenNotFound()
    {
        var response = await _adminClient.GetAsync("/api/patterns/nonexistent");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // ── Approve endpoint ──

    [Fact]
    public async Task ApprovePattern_TransitionsTrustLevel()
    {
        var patternId = await SeedPatternAsync(TrustLevel.Draft);

        var response = await _adminClient.PostAsJsonAsync(
            $"/api/patterns/{patternId}/approve",
            new ApprovePatternRequest { Notes = "Good pattern" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<ApiResponse<PatternGovernanceResult>>();
        Assert.True(body!.IsSuccess);
        Assert.Equal("Draft", body.Data!.PreviousTrustLevel);
        Assert.Equal("Approved", body.Data.NewTrustLevel);
    }

    [Fact]
    public async Task ApprovePattern_Returns404_WhenNotFound()
    {
        var response = await _adminClient.PostAsJsonAsync(
            "/api/patterns/nonexistent/approve",
            new ApprovePatternRequest());
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // ── Review endpoint ──

    [Fact]
    public async Task ReviewPattern_TransitionsDraftToReviewed()
    {
        var patternId = await SeedPatternAsync(TrustLevel.Draft);

        var response = await _leadClient.PostAsJsonAsync(
            $"/api/patterns/{patternId}/review",
            new ReviewPatternRequest { Notes = "Needs minor edits" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<ApiResponse<PatternGovernanceResult>>();
        Assert.True(body!.IsSuccess);
        Assert.Equal("Reviewed", body.Data!.NewTrustLevel);
    }

    // ── Deprecate endpoint ──

    [Fact]
    public async Task DeprecatePattern_TransitionsTrustLevel()
    {
        var patternId = await SeedPatternAsync(TrustLevel.Approved);

        var response = await _adminClient.PostAsJsonAsync(
            $"/api/patterns/{patternId}/deprecate",
            new DeprecatePatternRequest { Reason = "Outdated" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<ApiResponse<PatternGovernanceResult>>();
        Assert.True(body!.IsSuccess);
        Assert.Equal("Deprecated", body.Data!.NewTrustLevel);

        // Verify detail reflects deprecation.
        var detail = await _adminClient.GetAsync($"/api/patterns/{patternId}");
        var detailBody = await detail.Content.ReadFromJsonAsync<ApiResponse<PatternDetail>>();
        Assert.Equal("Deprecated", detailBody!.Data!.TrustLevel);
        Assert.Equal("Outdated", detailBody.Data.DeprecationReason);
    }

    // ── RBAC ──

    [Fact]
    public async Task Endpoints_RequireAuthentication()
    {
        var unauthClient = _factory.CreateClient();

        Assert.Equal(HttpStatusCode.Unauthorized,
            (await unauthClient.GetAsync("/api/patterns/governance-queue")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await unauthClient.GetAsync("/api/patterns/test-id")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await unauthClient.PostAsJsonAsync("/api/patterns/test-id/approve", new ApprovePatternRequest())).StatusCode);
    }

    [Fact]
    public async Task ApproveEndpoints_RequireApprovePermission()
    {
        var agentClient = _factory.CreateAuthenticatedClient(roles: "SupportAgent");

        Assert.Equal(HttpStatusCode.Forbidden,
            (await agentClient.GetAsync("/api/patterns/governance-queue")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await agentClient.PostAsJsonAsync("/api/patterns/x/approve", new ApprovePatternRequest())).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await agentClient.PostAsJsonAsync("/api/patterns/x/review", new ReviewPatternRequest())).StatusCode);
    }

    [Fact]
    public async Task DeprecateEndpoint_RequiresDeprecatePermission()
    {
        var agentClient = _factory.CreateAuthenticatedClient(roles: "SupportAgent");

        Assert.Equal(HttpStatusCode.Forbidden,
            (await agentClient.PostAsJsonAsync("/api/patterns/x/deprecate", new DeprecatePatternRequest())).StatusCode);
    }

    [Fact]
    public async Task SupportLead_CanAccessGovernanceEndpoints()
    {
        await SeedPatternAsync();

        var response = await _leadClient.GetAsync("/api/patterns/governance-queue");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    // ── Invalid transitions ──

    [Fact]
    public async Task ApproveAlreadyApproved_Returns404()
    {
        var patternId = await SeedPatternAsync(TrustLevel.Approved);

        var response = await _adminClient.PostAsJsonAsync(
            $"/api/patterns/{patternId}/approve",
            new ApprovePatternRequest());

        // Invalid transition returns null → 404.
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
