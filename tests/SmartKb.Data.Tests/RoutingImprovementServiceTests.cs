using Microsoft.Extensions.Logging.Abstractions;
using SmartKb.Contracts;
using SmartKb.Contracts.Configuration;
using SmartKb.Contracts.Enums;
using SmartKb.Contracts.Models;
using SmartKb.Contracts.Services;
using SmartKb.Data.Entities;
using SmartKb.Data.Repositories;

namespace SmartKb.Data.Tests;

public class RoutingImprovementServiceTests : IDisposable
{
    private readonly SmartKbDbContext _db;
    private readonly RoutingImprovementService _service;
    private readonly RoutingAnalyticsService _analyticsService;
    private readonly StubAuditWriter _auditWriter;
    private readonly RoutingAnalyticsSettings _settings;

    public RoutingImprovementServiceTests()
    {
        _db = TestDbContextFactory.Create();
        _auditWriter = new StubAuditWriter();
        _settings = new RoutingAnalyticsSettings
        {
            MinOutcomesForRecommendation = 3,
            RerouteRateThreshold = 0.3f,
            LowAcceptanceRateThreshold = 0.5f,
            MinRecommendationConfidence = 0.0f, // Lower for testing.
        };
        SeedTenant();
        _analyticsService = new RoutingAnalyticsService(
            _db, _settings, NullLogger<RoutingAnalyticsService>.Instance);
        _service = new RoutingImprovementService(
            _db, _analyticsService, _auditWriter, _settings,
            NullLogger<RoutingImprovementService>.Instance);
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

    private SessionEntity SeedSession()
    {
        var session = new SessionEntity
        {
            Id = Guid.NewGuid(),
            TenantId = "t1",
            UserId = "u1",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        };
        _db.Sessions.Add(session);
        _db.SaveChanges();
        return session;
    }

    private Guid SeedDraftWithOutcome(Guid sessionId, string productArea, string targetTeam,
        ResolutionType resolutionType, bool? acceptance = null)
    {
        var draftId = Guid.NewGuid();
        _db.EscalationDrafts.Add(new EscalationDraftEntity
        {
            Id = draftId,
            TenantId = "t1",
            UserId = "u1",
            SessionId = sessionId,
            Title = "Test",
            CustomerSummary = "Summary",
            StepsToReproduce = "Steps",
            LogsIdsRequested = "Logs",
            SuspectedComponent = productArea,
            Severity = "P2",
            EvidenceLinksJson = "[]",
            TargetTeam = targetTeam,
            Reason = "Test",
            CreatedAt = DateTimeOffset.UtcNow,
        });
        _db.OutcomeEvents.Add(new OutcomeEventEntity
        {
            Id = Guid.NewGuid(),
            SessionId = sessionId,
            TenantId = "t1",
            ResolutionType = resolutionType,
            TargetTeam = targetTeam,
            Acceptance = acceptance,
            EscalationTraceId = draftId.ToString(),
            CreatedAt = DateTimeOffset.UtcNow,
        });
        _db.SaveChanges();
        return draftId;
    }

    [Fact]
    public async Task GenerateRecommendations_HighRerouteRate_CreatesTeamChangeRecommendation()
    {
        var session = SeedSession();

        // Seed a routing rule.
        _db.EscalationRoutingRules.Add(new EscalationRoutingRuleEntity
        {
            Id = Guid.NewGuid(),
            TenantId = "t1",
            ProductArea = "Auth",
            TargetTeam = "Engineering",
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        });
        _db.SaveChanges();

        // 2 rerouted out of 4 = 50% reroute rate (above 30% threshold).
        SeedDraftWithOutcome(session.Id, "Auth", "Engineering", ResolutionType.Escalated, true);
        SeedDraftWithOutcome(session.Id, "Auth", "Engineering", ResolutionType.Rerouted);
        SeedDraftWithOutcome(session.Id, "Auth", "Engineering", ResolutionType.Rerouted);
        SeedDraftWithOutcome(session.Id, "Auth", "Engineering", ResolutionType.Escalated, true);

        var result = await _service.GenerateRecommendationsAsync("t1", "u1", "corr-1");

        Assert.Single(result.Recommendations);
        var rec = result.Recommendations[0];
        Assert.Equal("TeamChange", rec.RecommendationType);
        Assert.Equal("Auth", rec.ProductArea);
        Assert.Equal("Engineering", rec.CurrentTargetTeam);
        Assert.Equal("Pending", rec.Status);
        Assert.Equal(4, rec.SupportingOutcomeCount);

        var auditEvents = _auditWriter.Events
            .Where(e => e.EventType == AuditEventTypes.RoutingRecommendationGenerated).ToList();
        Assert.Single(auditEvents);
    }

    [Fact]
    public async Task GenerateRecommendations_LowAcceptanceRate_CreatesThresholdAdjustRecommendation()
    {
        var session = SeedSession();

        _db.EscalationRoutingRules.Add(new EscalationRoutingRuleEntity
        {
            Id = Guid.NewGuid(),
            TenantId = "t1",
            ProductArea = "Billing",
            TargetTeam = "Finance",
            EscalationThreshold = 0.4f,
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        });
        _db.SaveChanges();

        // 1 accepted, 2 rejected (acceptance false) = 33% acceptance rate (below 50% threshold).
        // 0 rerouted, so reroute rate is 0 — below reroute threshold.
        SeedDraftWithOutcome(session.Id, "Billing", "Finance", ResolutionType.Escalated, true);
        SeedDraftWithOutcome(session.Id, "Billing", "Finance", ResolutionType.Escalated, false);
        SeedDraftWithOutcome(session.Id, "Billing", "Finance", ResolutionType.Escalated, false);

        var result = await _service.GenerateRecommendationsAsync("t1", "u1", "corr-1");

        Assert.Single(result.Recommendations);
        var rec = result.Recommendations[0];
        Assert.Equal("ThresholdAdjust", rec.RecommendationType);
        Assert.Equal("Billing", rec.ProductArea);
        Assert.Equal(0.4f, rec.CurrentThreshold);
        Assert.NotNull(rec.SuggestedThreshold);
        Assert.True(rec.SuggestedThreshold < 0.4f);
    }

    [Fact]
    public async Task GenerateRecommendations_InsufficientData_NoRecommendations()
    {
        var session = SeedSession();

        // Only 2 outcomes, below MinOutcomesForRecommendation=3.
        SeedDraftWithOutcome(session.Id, "Auth", "Engineering", ResolutionType.Rerouted);
        SeedDraftWithOutcome(session.Id, "Auth", "Engineering", ResolutionType.Rerouted);

        var result = await _service.GenerateRecommendationsAsync("t1", "u1", "corr-1");

        Assert.Empty(result.Recommendations);
    }

    [Fact]
    public async Task GenerateRecommendations_SkipsDuplicatePending()
    {
        var session = SeedSession();

        _db.EscalationRoutingRules.Add(new EscalationRoutingRuleEntity
        {
            Id = Guid.NewGuid(),
            TenantId = "t1",
            ProductArea = "Auth",
            TargetTeam = "Engineering",
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        });
        _db.SaveChanges();

        SeedDraftWithOutcome(session.Id, "Auth", "Engineering", ResolutionType.Rerouted);
        SeedDraftWithOutcome(session.Id, "Auth", "Engineering", ResolutionType.Rerouted);
        SeedDraftWithOutcome(session.Id, "Auth", "Engineering", ResolutionType.Rerouted);

        // First call generates a recommendation.
        var first = await _service.GenerateRecommendationsAsync("t1", "u1", "c1");
        Assert.Single(first.Recommendations);

        // Second call should not duplicate.
        var second = await _service.GenerateRecommendationsAsync("t1", "u1", "c2");
        Assert.Empty(second.Recommendations);
    }

    [Fact]
    public async Task ApplyRecommendation_TeamChange_UpdatesRoutingRule()
    {
        var session = SeedSession();

        _db.EscalationRoutingRules.Add(new EscalationRoutingRuleEntity
        {
            Id = Guid.NewGuid(),
            TenantId = "t1",
            ProductArea = "Auth",
            TargetTeam = "Engineering",
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        });
        _db.SaveChanges();

        SeedDraftWithOutcome(session.Id, "Auth", "Engineering", ResolutionType.Rerouted);
        SeedDraftWithOutcome(session.Id, "Auth", "Engineering", ResolutionType.Rerouted);
        SeedDraftWithOutcome(session.Id, "Auth", "Engineering", ResolutionType.Rerouted);

        var recs = await _service.GenerateRecommendationsAsync("t1", "u1", "c1");
        var rec = recs.Recommendations[0];

        var applied = await _service.ApplyRecommendationAsync(
            "t1", "admin1", "c2", rec.RecommendationId,
            new ApplyRecommendationRequest { OverrideTargetTeam = "Security" });

        Assert.NotNull(applied);
        Assert.Equal("Applied", applied!.Status);

        // Check the routing rule was updated.
        var rule = _db.EscalationRoutingRules.First(r => r.ProductArea == "Auth" && r.TenantId == "t1");
        Assert.Equal("Security", rule.TargetTeam);

        Assert.Contains(_auditWriter.Events, e => e.EventType == AuditEventTypes.RoutingRecommendationApplied);
    }

    [Fact]
    public async Task ApplyRecommendation_ThresholdAdjust_UpdatesThreshold()
    {
        var session = SeedSession();

        _db.EscalationRoutingRules.Add(new EscalationRoutingRuleEntity
        {
            Id = Guid.NewGuid(),
            TenantId = "t1",
            ProductArea = "Billing",
            TargetTeam = "Finance",
            EscalationThreshold = 0.4f,
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        });
        _db.SaveChanges();

        SeedDraftWithOutcome(session.Id, "Billing", "Finance", ResolutionType.Escalated, true);
        SeedDraftWithOutcome(session.Id, "Billing", "Finance", ResolutionType.Escalated, false);
        SeedDraftWithOutcome(session.Id, "Billing", "Finance", ResolutionType.Escalated, false);

        var recs = await _service.GenerateRecommendationsAsync("t1", "u1", "c1");
        var rec = recs.Recommendations[0];

        var applied = await _service.ApplyRecommendationAsync(
            "t1", "admin1", "c2", rec.RecommendationId);

        Assert.NotNull(applied);
        Assert.Equal("Applied", applied!.Status);

        var rule = _db.EscalationRoutingRules.First(r => r.ProductArea == "Billing" && r.TenantId == "t1");
        Assert.True(rule.EscalationThreshold < 0.4f);
    }

    [Fact]
    public async Task ApplyRecommendation_NotPending_ReturnsNull()
    {
        var session = SeedSession();

        _db.EscalationRoutingRules.Add(new EscalationRoutingRuleEntity
        {
            Id = Guid.NewGuid(),
            TenantId = "t1",
            ProductArea = "Auth",
            TargetTeam = "Engineering",
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        });
        _db.SaveChanges();

        SeedDraftWithOutcome(session.Id, "Auth", "Engineering", ResolutionType.Rerouted);
        SeedDraftWithOutcome(session.Id, "Auth", "Engineering", ResolutionType.Rerouted);
        SeedDraftWithOutcome(session.Id, "Auth", "Engineering", ResolutionType.Rerouted);

        var recs = await _service.GenerateRecommendationsAsync("t1", "u1", "c1");
        var rec = recs.Recommendations[0];

        // Apply once.
        await _service.ApplyRecommendationAsync("t1", "admin1", "c2", rec.RecommendationId);

        // Try to apply again → null.
        var result = await _service.ApplyRecommendationAsync("t1", "admin1", "c3", rec.RecommendationId);
        Assert.Null(result);
    }

    [Fact]
    public async Task DismissRecommendation_MarksAsDismissed()
    {
        var session = SeedSession();

        _db.EscalationRoutingRules.Add(new EscalationRoutingRuleEntity
        {
            Id = Guid.NewGuid(),
            TenantId = "t1",
            ProductArea = "Auth",
            TargetTeam = "Engineering",
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        });
        _db.SaveChanges();

        SeedDraftWithOutcome(session.Id, "Auth", "Engineering", ResolutionType.Rerouted);
        SeedDraftWithOutcome(session.Id, "Auth", "Engineering", ResolutionType.Rerouted);
        SeedDraftWithOutcome(session.Id, "Auth", "Engineering", ResolutionType.Rerouted);

        var recs = await _service.GenerateRecommendationsAsync("t1", "u1", "c1");
        var rec = recs.Recommendations[0];

        var dismissed = await _service.DismissRecommendationAsync("t1", "admin1", "c2", rec.RecommendationId);
        Assert.True(dismissed);

        var all = await _service.GetRecommendationsAsync("t1", "Dismissed");
        Assert.Single(all.Recommendations);
        Assert.Equal("Dismissed", all.Recommendations[0].Status);

        Assert.Contains(_auditWriter.Events, e => e.EventType == AuditEventTypes.RoutingRecommendationDismissed);
    }

    [Fact]
    public async Task DismissRecommendation_NotFound_ReturnsFalse()
    {
        var result = await _service.DismissRecommendationAsync("t1", "u1", "c1", Guid.NewGuid());
        Assert.False(result);
    }

    [Fact]
    public async Task GetRecommendations_FiltersByStatus()
    {
        var session = SeedSession();

        _db.EscalationRoutingRules.Add(new EscalationRoutingRuleEntity
        {
            Id = Guid.NewGuid(),
            TenantId = "t1",
            ProductArea = "Auth",
            TargetTeam = "Engineering",
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        });
        _db.SaveChanges();

        SeedDraftWithOutcome(session.Id, "Auth", "Engineering", ResolutionType.Rerouted);
        SeedDraftWithOutcome(session.Id, "Auth", "Engineering", ResolutionType.Rerouted);
        SeedDraftWithOutcome(session.Id, "Auth", "Engineering", ResolutionType.Rerouted);

        await _service.GenerateRecommendationsAsync("t1", "u1", "c1");

        var pending = await _service.GetRecommendationsAsync("t1", "Pending");
        Assert.Single(pending.Recommendations);

        var applied = await _service.GetRecommendationsAsync("t1", "Applied");
        Assert.Empty(applied.Recommendations);
    }

    [Fact]
    public void ComputeConfidence_HighVolumeHighSignal_ReturnsHighConfidence()
    {
        var confidence = RoutingImprovementService.ComputeConfidence(50, 0.8f);
        Assert.True(confidence > 0.8f);
    }

    [Fact]
    public void ComputeConfidence_LowVolume_ReturnsLowerConfidence()
    {
        var high = RoutingImprovementService.ComputeConfidence(50, 0.5f);
        var low = RoutingImprovementService.ComputeConfidence(3, 0.5f);
        Assert.True(high > low);
    }

    [Fact]
    public void ComputeConfidence_ClampedTo01()
    {
        var result = RoutingImprovementService.ComputeConfidence(1000, 1.0f);
        Assert.True(result <= 1.0f);
        Assert.True(result >= 0f);
    }

    private sealed class StubAuditWriter : IAuditEventWriter
    {
        public List<AuditEvent> Events { get; } = [];

        public Task WriteAsync(AuditEvent auditEvent, CancellationToken cancellationToken = default)
        {
            Events.Add(auditEvent);
            return Task.CompletedTask;
        }
    }
}
