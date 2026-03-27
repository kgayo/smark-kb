using Microsoft.Extensions.Logging.Abstractions;
using SmartKb.Contracts.Configuration;
using SmartKb.Contracts.Enums;
using SmartKb.Data.Entities;
using SmartKb.Data.Repositories;

namespace SmartKb.Data.Tests;

public class RoutingAnalyticsServiceTests : IDisposable
{
    private readonly SmartKbDbContext _db;
    private readonly RoutingAnalyticsService _service;

    public RoutingAnalyticsServiceTests()
    {
        _db = TestDbContextFactory.Create();
        SeedTenant();
        _service = new RoutingAnalyticsService(
            _db,
            new RoutingAnalyticsSettings(),
            NullLogger<RoutingAnalyticsService>.Instance);
    }

    public void Dispose() => _db.Dispose();

    private void SeedTenant()
    {
        _db.Tenants.Add(new TenantEntity
        {
            TenantId = "t1",
            DisplayName = "Test",
            CreatedAt = DateTimeOffset.UtcNow,
        });
        _db.SaveChanges();
    }

    private SessionEntity SeedSession(string tenantId = "t1", string userId = "u1")
    {
        var now = DateTimeOffset.UtcNow;
        var session = new SessionEntity
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            UserId = userId,
            CreatedAt = now,
            CreatedAtEpoch = now.ToUnixTimeSeconds(),
            UpdatedAt = now,
        };
        _db.Sessions.Add(session);
        _db.SaveChanges();
        return session;
    }

    private void SeedOutcome(Guid sessionId, ResolutionType type,
        string? targetTeam = null, bool? acceptance = null,
        string? escalationTraceId = null,
        TimeSpan? timeToAssign = null, TimeSpan? timeToResolve = null)
    {
        _db.OutcomeEvents.Add(new OutcomeEventEntity
        {
            Id = Guid.NewGuid(),
            SessionId = sessionId,
            TenantId = "t1",
            ResolutionType = type,
            TargetTeam = targetTeam,
            Acceptance = acceptance,
            EscalationTraceId = escalationTraceId,
            TimeToAssign = timeToAssign,
            TimeToResolve = timeToResolve,
            CreatedAt = DateTimeOffset.UtcNow,
            CreatedAtEpoch = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
        });
        _db.SaveChanges();
    }

    [Fact]
    public async Task EmptyOutcomes_ReturnsZeroMetrics()
    {
        var result = await _service.GetAnalyticsAsync("t1");

        Assert.Equal(0, result.TotalOutcomes);
        Assert.Equal(0, result.TotalEscalations);
        Assert.Equal(0f, result.OverallAcceptanceRate);
        Assert.Equal(0f, result.OverallRerouteRate);
        Assert.Empty(result.TeamMetrics);
        Assert.Empty(result.ProductAreaMetrics);
    }

    [Fact]
    public async Task MixedOutcomes_ComputesCorrectSummary()
    {
        var session = SeedSession();

        SeedOutcome(session.Id, ResolutionType.ResolvedWithoutEscalation);
        SeedOutcome(session.Id, ResolutionType.ResolvedWithoutEscalation);
        SeedOutcome(session.Id, ResolutionType.Escalated, "Engineering", true);
        SeedOutcome(session.Id, ResolutionType.Rerouted, "Billing");

        var result = await _service.GetAnalyticsAsync("t1");

        Assert.Equal(4, result.TotalOutcomes);
        Assert.Equal(2, result.TotalEscalations);
        Assert.Equal(1, result.TotalReroutes);
        Assert.Equal(2, result.TotalResolvedWithoutEscalation);
        Assert.Equal(0.5f, result.SelfResolutionRate);
    }

    [Fact]
    public async Task TeamMetrics_GroupsByTargetTeam()
    {
        var session = SeedSession();

        SeedOutcome(session.Id, ResolutionType.Escalated, "Engineering", true);
        SeedOutcome(session.Id, ResolutionType.Escalated, "Engineering", false);
        SeedOutcome(session.Id, ResolutionType.Rerouted, "Billing");

        var result = await _service.GetAnalyticsAsync("t1");

        Assert.True(result.TeamMetrics.Count == 2);

        var engTeam = result.TeamMetrics.First(t => t.TargetTeam == "Engineering");
        Assert.Equal(2, engTeam.TotalEscalations);
        Assert.Equal(1, engTeam.AcceptedCount);
        Assert.Equal(0.5f, engTeam.AcceptanceRate);

        var billingTeam = result.TeamMetrics.First(t => t.TargetTeam == "Billing");
        Assert.Equal(1, billingTeam.TotalEscalations);
        Assert.Equal(1, billingTeam.ReroutedCount);
        Assert.Equal(1f, billingTeam.RerouteRate);
    }

    [Fact]
    public async Task TeamMetrics_ComputesAvgTimeToAssign()
    {
        var session = SeedSession();

        SeedOutcome(session.Id, ResolutionType.Escalated, "Engineering", true,
            timeToAssign: TimeSpan.FromMinutes(10));
        SeedOutcome(session.Id, ResolutionType.Escalated, "Engineering", true,
            timeToAssign: TimeSpan.FromMinutes(20));

        var result = await _service.GetAnalyticsAsync("t1");

        var engTeam = result.TeamMetrics.First(t => t.TargetTeam == "Engineering");
        Assert.NotNull(engTeam.AvgTimeToAssign);
        Assert.Equal(TimeSpan.FromMinutes(15), engTeam.AvgTimeToAssign.Value);
    }

    [Fact]
    public async Task TenantIsolation_DoesNotLeakOutcomes()
    {
        _db.Tenants.Add(new TenantEntity
        {
            TenantId = "t2",
            DisplayName = "Other",
            CreatedAt = DateTimeOffset.UtcNow,
        });
        _db.SaveChanges();

        var s1 = SeedSession("t1");
        var s2 = SeedSession("t2", "u2");

        SeedOutcome(s1.Id, ResolutionType.Escalated, "Engineering", true);
        _db.OutcomeEvents.Add(new OutcomeEventEntity
        {
            Id = Guid.NewGuid(),
            SessionId = s2.Id,
            TenantId = "t2",
            ResolutionType = ResolutionType.Escalated,
            TargetTeam = "Sales",
            Acceptance = true,
            CreatedAt = DateTimeOffset.UtcNow,
            CreatedAtEpoch = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
        });
        _db.SaveChanges();

        var result = await _service.GetAnalyticsAsync("t1");

        Assert.Equal(1, result.TotalOutcomes);
        var teamMetric = Assert.Single(result.TeamMetrics);
        Assert.Equal("Engineering", teamMetric.TargetTeam);
    }

    [Fact]
    public async Task WindowDays_FiltersOldOutcomes()
    {
        var session = SeedSession();

        // Add an old outcome (60 days ago).
        var oldDate = DateTimeOffset.UtcNow.AddDays(-60);
        _db.OutcomeEvents.Add(new OutcomeEventEntity
        {
            Id = Guid.NewGuid(),
            SessionId = session.Id,
            TenantId = "t1",
            ResolutionType = ResolutionType.Escalated,
            TargetTeam = "Old",
            CreatedAt = oldDate,
            CreatedAtEpoch = oldDate.ToUnixTimeSeconds(),
        });
        SeedOutcome(session.Id, ResolutionType.Escalated, "New", true);

        var result = await _service.GetAnalyticsAsync("t1", windowDays: 30);

        Assert.Equal(1, result.TotalOutcomes);
        Assert.Equal("New", result.TeamMetrics[0].TargetTeam);
    }

    [Fact]
    public async Task ProductAreaMetrics_JoinsWithEscalationDrafts()
    {
        var session = SeedSession();

        var draftId = Guid.NewGuid();
        _db.EscalationDrafts.Add(new EscalationDraftEntity
        {
            Id = draftId,
            TenantId = "t1",
            UserId = "u1",
            SessionId = session.Id,
            Title = "Test",
            CustomerSummary = "Summary",
            StepsToReproduce = "Steps",
            LogsIdsRequested = "Logs",
            SuspectedComponent = "Authentication",
            Severity = "P2",
            EvidenceLinksJson = "[]",
            TargetTeam = "Security",
            Reason = "Test",
            CreatedAt = DateTimeOffset.UtcNow,
        });
        _db.SaveChanges();

        SeedOutcome(session.Id, ResolutionType.Escalated, "Security", true,
            escalationTraceId: draftId.ToString());
        SeedOutcome(session.Id, ResolutionType.Rerouted, "Security", null,
            escalationTraceId: draftId.ToString());

        // Seed a routing rule so currentTargetTeam resolves.
        _db.EscalationRoutingRules.Add(new EscalationRoutingRuleEntity
        {
            Id = Guid.NewGuid(),
            TenantId = "t1",
            ProductArea = "Authentication",
            TargetTeam = "Security",
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        });
        _db.SaveChanges();

        var result = await _service.GetAnalyticsAsync("t1");

        Assert.Single(result.ProductAreaMetrics);
        var area = result.ProductAreaMetrics[0];
        Assert.Equal("Authentication", area.ProductArea);
        Assert.Equal("Security", area.CurrentTargetTeam);
        Assert.Equal(2, area.TotalEscalations);
        Assert.Equal(1, area.AcceptedCount);
        Assert.Equal(1, area.ReroutedCount);
        Assert.Equal(0.5f, area.AcceptanceRate);
        Assert.Equal(0.5f, area.RerouteRate);
    }

    [Fact]
    public async Task GetAnalytics_FiltersServerSideViaEpochColumn()
    {
        var session = SeedSession();
        var now = DateTimeOffset.UtcNow;

        // In-window outcome (5 days ago) — should be included
        var recent = now.AddDays(-5);
        _db.OutcomeEvents.Add(new OutcomeEventEntity
        {
            Id = Guid.NewGuid(),
            SessionId = session.Id,
            TenantId = "t1",
            ResolutionType = ResolutionType.Escalated,
            TargetTeam = "InWindow",
            CreatedAt = recent,
            CreatedAtEpoch = recent.ToUnixTimeSeconds(),
        });

        // Out-of-window outcome (45 days ago) — should be excluded by epoch filter
        var old = now.AddDays(-45);
        _db.OutcomeEvents.Add(new OutcomeEventEntity
        {
            Id = Guid.NewGuid(),
            SessionId = session.Id,
            TenantId = "t1",
            ResolutionType = ResolutionType.Escalated,
            TargetTeam = "OutOfWindow",
            CreatedAt = old,
            CreatedAtEpoch = old.ToUnixTimeSeconds(),
        });

        _db.SaveChanges();

        var result = await _service.GetAnalyticsAsync("t1", windowDays: 30);

        Assert.Equal(1, result.TotalOutcomes);
        Assert.Single(result.TeamMetrics);
        Assert.Equal("InWindow", result.TeamMetrics[0].TargetTeam);
    }
}
