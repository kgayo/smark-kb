using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SmartKb.Contracts.Models;
using SmartKb.Data;
using SmartKb.Data.Entities;

namespace SmartKb.Api.Tests.Patterns;

public class MaintenanceEndpointTests : IAsyncLifetime
{
    private readonly DistillationTestFactory _factory = new();
    private HttpClient _adminClient = null!;
    private HttpClient _agentClient = null!;

    public async Task InitializeAsync()
    {
        await _factory.InitializeAsync();
        _adminClient = _factory.CreateAuthenticatedClient(roles: "Admin");
        _agentClient = _factory.CreateAuthenticatedClient(roles: "SupportAgent", userId: "agent-1");
        await _factory.EnsureDatabaseAsync();
    }

    public async Task DisposeAsync()
    {
        _adminClient.Dispose();
        _agentClient.Dispose();
        await _factory.DisposeAsync();
    }

    private async Task<string> SeedPatternAsync(
        string title = "Test Pattern",
        string symptomsJson = "[\"error\"]",
        string resolutionStepsJson = "[\"fix it\"]",
        string trustLevel = "Approved",
        string? productArea = "Backend",
        int daysOld = 5,
        float? qualityScore = 0.8f)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SmartKbDbContext>();

        var patternId = $"pattern-{Guid.NewGuid():N}";
        db.CasePatterns.Add(new CasePatternEntity
        {
            Id = Guid.NewGuid(),
            PatternId = patternId,
            TenantId = "tenant-1",
            Title = title,
            ProblemStatement = "A test problem statement for detection",
            SymptomsJson = symptomsJson,
            DiagnosisStepsJson = "[]",
            ResolutionStepsJson = resolutionStepsJson,
            VerificationStepsJson = "[]",
            EscalationCriteriaJson = "[]",
            RelatedEvidenceIdsJson = "[\"ev1\"]",
            Confidence = 0.6f,
            TrustLevel = trustLevel,
            Version = 1,
            ProductArea = productArea,
            TagsJson = "[]",
            Visibility = "Internal",
            AllowedGroupsJson = "[]",
            AccessLabel = "Internal",
            SourceUrl = "session://test",
            ApplicabilityConstraintsJson = "[]",
            ExclusionsJson = "[]",
            QualityScore = qualityScore,
            CreatedAt = DateTimeOffset.UtcNow.AddDays(-daysOld),
            CreatedAtEpoch = DateTimeOffset.UtcNow.AddDays(-daysOld).ToUnixTimeSeconds(),
            UpdatedAt = DateTimeOffset.UtcNow.AddDays(-daysOld),
        });
        await db.SaveChangesAsync();
        return patternId;
    }

    private async Task<Guid> SeedContradictionAsync(string status = "Pending")
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SmartKbDbContext>();

        var id = Guid.NewGuid();
        db.PatternContradictions.Add(new PatternContradictionEntity
        {
            Id = id,
            TenantId = "tenant-1",
            PatternIdA = "pattern-a",
            PatternIdB = "pattern-b",
            ContradictionType = "ResolutionConflict",
            SimilarityScore = 0.6f,
            Description = "Test contradiction",
            ConflictingFieldsJson = "[\"ResolutionSteps\"]",
            Status = status,
            CreatedAt = DateTimeOffset.UtcNow,
            CreatedAtEpoch = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
        });
        await db.SaveChangesAsync();
        return id;
    }

    private async Task<Guid> SeedMaintenanceTaskAsync(string status = "Pending")
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SmartKbDbContext>();

        var id = Guid.NewGuid();
        db.PatternMaintenanceTasks.Add(new PatternMaintenanceTaskEntity
        {
            Id = id,
            TenantId = "tenant-1",
            PatternId = "pattern-test",
            TaskType = "Stale",
            Severity = "Warning",
            Description = "Test task",
            RecommendedAction = "Review",
            MetricsJson = "{}",
            Status = status,
            CreatedAt = DateTimeOffset.UtcNow,
            CreatedAtEpoch = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
        });
        await db.SaveChangesAsync();
        return id;
    }

    // --- Contradiction Detection Endpoint ---

    [Fact]
    public async Task DetectContradictions_AdminRole_ReturnsOk()
    {
        await SeedPatternAsync();

        var response = await _adminClient.PostAsync("/api/admin/patterns/detect-contradictions", null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<ApiResponse<ContradictionDetectionResult>>();
        Assert.NotNull(body?.Data);
        Assert.True(body!.Data!.PatternsAnalyzed >= 0);
    }

    [Fact]
    public async Task DetectContradictions_AgentRole_Forbidden()
    {
        var response = await _agentClient.PostAsync("/api/admin/patterns/detect-contradictions", null);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    // --- Get Contradictions Endpoint ---

    [Fact]
    public async Task GetContradictions_ReturnsOk()
    {
        await SeedContradictionAsync();

        var response = await _adminClient.GetAsync("/api/admin/patterns/contradictions");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<ApiResponse<ContradictionListResponse>>();
        Assert.NotNull(body?.Data);
        Assert.True(body!.Data!.TotalCount >= 1);
    }

    [Fact]
    public async Task GetContradictions_FilterByStatus()
    {
        await SeedContradictionAsync("Pending");
        await SeedContradictionAsync("Resolved");

        var response = await _adminClient.GetAsync("/api/admin/patterns/contradictions?status=Pending");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<ApiResponse<ContradictionListResponse>>();
        Assert.NotNull(body?.Data);
        Assert.All(body!.Data!.Contradictions, c => Assert.Equal("Pending", c.Status));
    }

    // --- Resolve Contradiction Endpoint ---

    [Fact]
    public async Task ResolveContradiction_ValidRequest_ReturnsOk()
    {
        var id = await SeedContradictionAsync();

        var response = await _adminClient.PostAsJsonAsync(
            $"/api/admin/patterns/contradictions/{id}/resolve",
            new ResolveContradictionRequest { Resolution = "Merged", Notes = "Combined patterns" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<ApiResponse<ContradictionSummary>>();
        Assert.NotNull(body?.Data);
        Assert.Equal("Resolved", body!.Data!.Status);
        Assert.Equal("Merged", body.Data.Resolution);
    }

    [Fact]
    public async Task ResolveContradiction_NotFound_Returns404()
    {
        var response = await _adminClient.PostAsJsonAsync(
            $"/api/admin/patterns/contradictions/{Guid.NewGuid()}/resolve",
            new ResolveContradictionRequest { Resolution = "Merged" });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // --- Detect Maintenance Endpoint ---

    [Fact]
    public async Task DetectMaintenance_AdminRole_ReturnsOk()
    {
        var response = await _adminClient.PostAsync("/api/admin/patterns/detect-maintenance", null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<ApiResponse<MaintenanceDetectionResult>>();
        Assert.NotNull(body?.Data);
    }

    [Fact]
    public async Task DetectMaintenance_AgentRole_Forbidden()
    {
        var response = await _agentClient.PostAsync("/api/admin/patterns/detect-maintenance", null);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    // --- Get Maintenance Tasks Endpoint ---

    [Fact]
    public async Task GetMaintenanceTasks_ReturnsOk()
    {
        await SeedMaintenanceTaskAsync();

        var response = await _adminClient.GetAsync("/api/admin/patterns/maintenance-tasks");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<ApiResponse<MaintenanceTaskListResponse>>();
        Assert.NotNull(body?.Data);
        Assert.True(body!.Data!.TotalCount >= 1);
    }

    // --- Resolve Maintenance Task Endpoint ---

    [Fact]
    public async Task ResolveMaintenanceTask_ReturnsOk()
    {
        var id = await SeedMaintenanceTaskAsync();

        var response = await _adminClient.PostAsJsonAsync(
            $"/api/admin/patterns/maintenance-tasks/{id}/resolve",
            new ResolveMaintenanceTaskRequest { Notes = "Confirmed and updated" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<ApiResponse<MaintenanceTaskSummary>>();
        Assert.NotNull(body?.Data);
        Assert.Equal("Resolved", body!.Data!.Status);
    }

    // --- Dismiss Maintenance Task Endpoint ---

    [Fact]
    public async Task DismissMaintenanceTask_ReturnsOk()
    {
        var id = await SeedMaintenanceTaskAsync();

        var response = await _adminClient.PostAsJsonAsync(
            $"/api/admin/patterns/maintenance-tasks/{id}/dismiss",
            new ResolveMaintenanceTaskRequest { Notes = "Not an issue" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<ApiResponse<MaintenanceTaskSummary>>();
        Assert.NotNull(body?.Data);
        Assert.Equal("Dismissed", body!.Data!.Status);
    }

    [Fact]
    public async Task DismissMaintenanceTask_NotFound_Returns404()
    {
        var response = await _adminClient.PostAsJsonAsync(
            $"/api/admin/patterns/maintenance-tasks/{Guid.NewGuid()}/dismiss",
            new ResolveMaintenanceTaskRequest());

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // --- Tenant Isolation ---

    [Fact]
    public async Task Contradictions_TenantIsolation()
    {
        await SeedContradictionAsync();

        var otherTenantClient = _factory.CreateAuthenticatedClient(tenantId: "tenant-2", roles: "Admin");
        // Create tenant-2.
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SmartKbDbContext>();
            if (!await db.Tenants.AnyAsync(t => t.TenantId == "tenant-2"))
            {
                db.Tenants.Add(new TenantEntity
                {
                    TenantId = "tenant-2",
                    DisplayName = "Other Tenant",
                    CreatedAt = DateTimeOffset.UtcNow,
                });
                await db.SaveChangesAsync();
            }
        }

        var response = await otherTenantClient.GetAsync("/api/admin/patterns/contradictions");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<ApiResponse<ContradictionListResponse>>();
        Assert.NotNull(body?.Data);
        Assert.Equal(0, body!.Data!.TotalCount);
        otherTenantClient.Dispose();
    }

    private record ApiResponse<T>(T? Data, string? CorrelationId, string? Error);
}
