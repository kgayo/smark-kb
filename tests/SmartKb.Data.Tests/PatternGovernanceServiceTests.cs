using Microsoft.Extensions.Logging.Abstractions;
using SmartKb.Contracts;
using SmartKb.Contracts.Enums;
using SmartKb.Contracts.Models;
using SmartKb.Contracts.Services;
using SmartKb.Data.Entities;
using SmartKb.Data.Repositories;

namespace SmartKb.Data.Tests;

public class PatternGovernanceServiceTests : IDisposable
{
    private readonly SmartKbDbContext _db;
    private readonly StubAuditWriter _auditWriter;
    private readonly PatternGovernanceService _service;

    private const string TenantId = "t1";
    private const string ActorId = "lead-1";
    private const string CorrelationId = "corr-1";

    public PatternGovernanceServiceTests()
    {
        _db = TestDbContextFactory.Create();
        _auditWriter = new StubAuditWriter();
        SeedTenant();
        _service = new PatternGovernanceService(
            _db, _auditWriter,
            NullLogger<PatternGovernanceService>.Instance);
    }

    public void Dispose() => _db.Dispose();

    private void SeedTenant()
    {
        _db.Tenants.Add(new TenantEntity
        {
            TenantId = TenantId,
            DisplayName = "Test",
            CreatedAt = DateTimeOffset.UtcNow,
        });
        _db.SaveChanges();
    }

    private CasePatternEntity SeedPattern(
        TrustLevel trustLevel = TrustLevel.Draft,
        string? productArea = null,
        string tenantId = TenantId)
    {
        var entity = new CasePatternEntity
        {
            Id = Guid.NewGuid(),
            PatternId = $"pattern-{Guid.NewGuid():N}",
            TenantId = tenantId,
            Title = "Test Pattern",
            ProblemStatement = "Test problem statement",
            SymptomsJson = "[\"symptom1\"]",
            DiagnosisStepsJson = "[\"diag1\"]",
            ResolutionStepsJson = "[\"step1\",\"step2\"]",
            VerificationStepsJson = "[\"verify1\"]",
            EscalationCriteriaJson = "[]",
            RelatedEvidenceIdsJson = "[\"ev1\",\"ev2\"]",
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
            CreatedAt = DateTimeOffset.UtcNow.AddDays(-3),
            UpdatedAt = DateTimeOffset.UtcNow.AddDays(-3),
        };
        _db.CasePatterns.Add(entity);
        _db.SaveChanges();
        return entity;
    }

    // ── Governance Queue Tests ──

    [Fact]
    public async Task GetGovernanceQueue_ReturnsAllPatterns()
    {
        SeedPattern();
        SeedPattern(TrustLevel.Approved);
        SeedPattern(TrustLevel.Reviewed);

        var result = await _service.GetGovernanceQueueAsync(TenantId);

        Assert.Equal(3, result.TotalCount);
        Assert.Equal(3, result.Patterns.Count);
        Assert.Equal(1, result.Page);
        Assert.False(result.HasMore);
    }

    [Fact]
    public async Task GetGovernanceQueue_FiltersByTrustLevel()
    {
        SeedPattern(TrustLevel.Draft);
        SeedPattern(TrustLevel.Approved);

        var result = await _service.GetGovernanceQueueAsync(TenantId, trustLevel: "Draft");

        Assert.Equal(1, result.TotalCount);
        Assert.All(result.Patterns, p => Assert.Equal("Draft", p.TrustLevel));
    }

    [Fact]
    public async Task GetGovernanceQueue_FiltersByProductArea()
    {
        SeedPattern(productArea: "Auth");
        SeedPattern(productArea: "Billing");

        var result = await _service.GetGovernanceQueueAsync(TenantId, productArea: "Auth");

        Assert.Equal(1, result.TotalCount);
        Assert.All(result.Patterns, p => Assert.Equal("Auth", p.ProductArea));
    }

    [Fact]
    public async Task GetGovernanceQueue_Paginates()
    {
        for (var i = 0; i < 5; i++) SeedPattern();

        var page1 = await _service.GetGovernanceQueueAsync(TenantId, pageSize: 2);
        Assert.Equal(2, page1.Patterns.Count);
        Assert.Equal(5, page1.TotalCount);
        Assert.True(page1.HasMore);

        var page3 = await _service.GetGovernanceQueueAsync(TenantId, page: 3, pageSize: 2);
        Assert.Single(page3.Patterns);
        Assert.False(page3.HasMore);
    }

    [Fact]
    public async Task GetGovernanceQueue_TenantIsolation()
    {
        _db.Tenants.Add(new TenantEntity
        {
            TenantId = "t2",
            DisplayName = "Other",
            CreatedAt = DateTimeOffset.UtcNow,
        });
        _db.SaveChanges();

        SeedPattern(tenantId: TenantId);
        SeedPattern(tenantId: "t2");

        var result = await _service.GetGovernanceQueueAsync(TenantId);
        Assert.Equal(1, result.TotalCount);
    }

    [Fact]
    public async Task GetGovernanceQueue_TruncatesProblemStatement()
    {
        var entity = SeedPattern();
        entity.ProblemStatement = new string('x', 300);
        _db.SaveChanges();

        var result = await _service.GetGovernanceQueueAsync(TenantId);
        Assert.True(result.Patterns[0].ProblemStatement.Length <= 203); // 200 + "..."
        Assert.EndsWith("...", result.Patterns[0].ProblemStatement);
    }

    // ── Pattern Detail Tests ──

    [Fact]
    public async Task GetPatternDetail_ReturnsFullPattern()
    {
        var entity = SeedPattern();

        var result = await _service.GetPatternDetailAsync(TenantId, entity.PatternId);

        Assert.NotNull(result);
        Assert.Equal(entity.PatternId, result.PatternId);
        Assert.Equal(entity.Title, result.Title);
        Assert.Equal(entity.ProblemStatement, result.ProblemStatement);
        Assert.Single(result.Symptoms);
        Assert.Equal(2, result.ResolutionSteps.Count);
        Assert.Equal(2, result.RelatedEvidenceIds.Count);
    }

    [Fact]
    public async Task GetPatternDetail_ReturnsNull_WhenNotFound()
    {
        var result = await _service.GetPatternDetailAsync(TenantId, "nonexistent");
        Assert.Null(result);
    }

    [Fact]
    public async Task GetPatternDetail_ReturnsNull_WhenWrongTenant()
    {
        var entity = SeedPattern();
        var result = await _service.GetPatternDetailAsync("other-tenant", entity.PatternId);
        Assert.Null(result);
    }

    // ── Review Transition Tests ──

    [Fact]
    public async Task ReviewPattern_TransitionsDraftToReviewed()
    {
        var entity = SeedPattern(TrustLevel.Draft);

        var result = await _service.ReviewPatternAsync(
            TenantId, entity.PatternId, ActorId, CorrelationId,
            new ReviewPatternRequest { Notes = "LGTM" });

        Assert.NotNull(result);
        Assert.Equal("Draft", result.PreviousTrustLevel);
        Assert.Equal("Reviewed", result.NewTrustLevel);
        Assert.Equal(ActorId, result.TransitionedBy);

        var updated = await _service.GetPatternDetailAsync(TenantId, entity.PatternId);
        Assert.NotNull(updated);
        Assert.Equal("Reviewed", updated.TrustLevel);
        Assert.Equal(ActorId, updated.ReviewedBy);
        Assert.NotNull(updated.ReviewedAt);
        Assert.Equal("LGTM", updated.ReviewNotes);
    }

    [Fact]
    public async Task ReviewPattern_WritesAuditEvent()
    {
        var entity = SeedPattern(TrustLevel.Draft);

        await _service.ReviewPatternAsync(
            TenantId, entity.PatternId, ActorId, CorrelationId,
            new ReviewPatternRequest());

        Assert.Contains(_auditWriter.Events,
            e => e.EventType == AuditEventTypes.PatternReviewed
                 && e.TenantId == TenantId
                 && e.ActorId == ActorId);
    }

    [Fact]
    public async Task ReviewPattern_RejectsAlreadyReviewed()
    {
        var entity = SeedPattern(TrustLevel.Reviewed);

        var result = await _service.ReviewPatternAsync(
            TenantId, entity.PatternId, ActorId, CorrelationId,
            new ReviewPatternRequest());

        Assert.Null(result); // Invalid transition Reviewed → Reviewed.
    }

    // ── Approve Transition Tests ──

    [Fact]
    public async Task ApprovePattern_TransitionsDraftToApproved()
    {
        var entity = SeedPattern(TrustLevel.Draft);

        var result = await _service.ApprovePatternAsync(
            TenantId, entity.PatternId, ActorId, CorrelationId,
            new ApprovePatternRequest { Notes = "Ship it" });

        Assert.NotNull(result);
        Assert.Equal("Draft", result.PreviousTrustLevel);
        Assert.Equal("Approved", result.NewTrustLevel);

        var updated = await _service.GetPatternDetailAsync(TenantId, entity.PatternId);
        Assert.NotNull(updated);
        Assert.Equal("Approved", updated.TrustLevel);
        Assert.Equal(ActorId, updated.ApprovedBy);
        Assert.NotNull(updated.ApprovedAt);
        Assert.Equal("Ship it", updated.ApprovalNotes);
    }

    [Fact]
    public async Task ApprovePattern_TransitionsReviewedToApproved()
    {
        var entity = SeedPattern(TrustLevel.Reviewed);

        var result = await _service.ApprovePatternAsync(
            TenantId, entity.PatternId, ActorId, CorrelationId,
            new ApprovePatternRequest());

        Assert.NotNull(result);
        Assert.Equal("Reviewed", result.PreviousTrustLevel);
        Assert.Equal("Approved", result.NewTrustLevel);
    }

    [Fact]
    public async Task ApprovePattern_RejectsAlreadyApproved()
    {
        var entity = SeedPattern(TrustLevel.Approved);

        var result = await _service.ApprovePatternAsync(
            TenantId, entity.PatternId, ActorId, CorrelationId,
            new ApprovePatternRequest());

        Assert.Null(result);
    }

    [Fact]
    public async Task ApprovePattern_WritesAuditEvent()
    {
        var entity = SeedPattern(TrustLevel.Draft);

        await _service.ApprovePatternAsync(
            TenantId, entity.PatternId, ActorId, CorrelationId,
            new ApprovePatternRequest());

        Assert.Contains(_auditWriter.Events,
            e => e.EventType == AuditEventTypes.PatternApproved);
    }

    // ── Deprecate Transition Tests ──

    [Fact]
    public async Task DeprecatePattern_TransitionsDraftToDeprecated()
    {
        var entity = SeedPattern(TrustLevel.Draft);

        var result = await _service.DeprecatePatternAsync(
            TenantId, entity.PatternId, ActorId, CorrelationId,
            new DeprecatePatternRequest { Reason = "Outdated" });

        Assert.NotNull(result);
        Assert.Equal("Draft", result.PreviousTrustLevel);
        Assert.Equal("Deprecated", result.NewTrustLevel);

        var updated = await _service.GetPatternDetailAsync(TenantId, entity.PatternId);
        Assert.NotNull(updated);
        Assert.Equal("Deprecated", updated.TrustLevel);
        Assert.Equal(ActorId, updated.DeprecatedBy);
        Assert.NotNull(updated.DeprecatedAt);
        Assert.Equal("Outdated", updated.DeprecationReason);
    }

    [Fact]
    public async Task DeprecatePattern_TransitionsApprovedToDeprecated()
    {
        var entity = SeedPattern(TrustLevel.Approved);

        var result = await _service.DeprecatePatternAsync(
            TenantId, entity.PatternId, ActorId, CorrelationId,
            new DeprecatePatternRequest());

        Assert.NotNull(result);
        Assert.Equal("Approved", result.PreviousTrustLevel);
        Assert.Equal("Deprecated", result.NewTrustLevel);
    }

    [Fact]
    public async Task DeprecatePattern_SetsSupersedingPatternId()
    {
        var entity = SeedPattern(TrustLevel.Approved);
        var newPattern = SeedPattern(TrustLevel.Draft);

        await _service.DeprecatePatternAsync(
            TenantId, entity.PatternId, ActorId, CorrelationId,
            new DeprecatePatternRequest
            {
                Reason = "Replaced",
                SupersedingPatternId = newPattern.PatternId,
            });

        var updated = await _service.GetPatternDetailAsync(TenantId, entity.PatternId);
        Assert.NotNull(updated);
        Assert.Equal(newPattern.PatternId, updated.SupersedesPatternId);
    }

    [Fact]
    public async Task DeprecatePattern_RejectsAlreadyDeprecated()
    {
        var entity = SeedPattern(TrustLevel.Deprecated);

        var result = await _service.DeprecatePatternAsync(
            TenantId, entity.PatternId, ActorId, CorrelationId,
            new DeprecatePatternRequest());

        Assert.Null(result);
    }

    [Fact]
    public async Task DeprecatePattern_WritesAuditEvent()
    {
        var entity = SeedPattern(TrustLevel.Draft);

        await _service.DeprecatePatternAsync(
            TenantId, entity.PatternId, ActorId, CorrelationId,
            new DeprecatePatternRequest { Reason = "Stale" });

        Assert.Contains(_auditWriter.Events,
            e => e.EventType == AuditEventTypes.PatternDeprecated
                 && e.Detail!.Contains("Stale"));
    }

    // ── Cross-Cutting Tests ──

    [Fact]
    public async Task AllTransitions_ReturnNull_WhenPatternNotFound()
    {
        var r1 = await _service.ReviewPatternAsync(TenantId, "nope", ActorId, CorrelationId, new());
        var r2 = await _service.ApprovePatternAsync(TenantId, "nope", ActorId, CorrelationId, new());
        var r3 = await _service.DeprecatePatternAsync(TenantId, "nope", ActorId, CorrelationId, new());

        Assert.Null(r1);
        Assert.Null(r2);
        Assert.Null(r3);
    }

    [Fact]
    public async Task AllTransitions_EnforceTenantIsolation()
    {
        var entity = SeedPattern(TrustLevel.Draft);

        var r1 = await _service.ReviewPatternAsync("wrong-tenant", entity.PatternId, ActorId, CorrelationId, new());
        var r2 = await _service.ApprovePatternAsync("wrong-tenant", entity.PatternId, ActorId, CorrelationId, new());
        var r3 = await _service.DeprecatePatternAsync("wrong-tenant", entity.PatternId, ActorId, CorrelationId, new());

        Assert.Null(r1);
        Assert.Null(r2);
        Assert.Null(r3);
    }

    [Fact]
    public async Task Transition_UpdatesUpdatedAtTimestamp()
    {
        var entity = SeedPattern(TrustLevel.Draft);
        var originalUpdatedAt = entity.UpdatedAt;

        await _service.ApprovePatternAsync(
            TenantId, entity.PatternId, ActorId, CorrelationId, new());

        var updated = await _service.GetPatternDetailAsync(TenantId, entity.PatternId);
        Assert.NotNull(updated);
        Assert.True(updated.UpdatedAt > originalUpdatedAt);
    }

    [Fact]
    public async Task DeprecateReviewed_IsValid()
    {
        var entity = SeedPattern(TrustLevel.Reviewed);

        var result = await _service.DeprecatePatternAsync(
            TenantId, entity.PatternId, ActorId, CorrelationId, new());

        Assert.NotNull(result);
        Assert.Equal("Deprecated", result.NewTrustLevel);
    }

    // ── GovernanceQueue sorts by CreatedAt desc ──

    [Fact]
    public async Task GetGovernanceQueue_OrdersByCreatedAtDescending()
    {
        var older = SeedPattern();
        var newer = SeedPattern();
        // newer has a later CreatedAt by default (seed order).

        var result = await _service.GetGovernanceQueueAsync(TenantId);

        Assert.Equal(2, result.Patterns.Count);
        Assert.True(result.Patterns[0].CreatedAt >= result.Patterns[1].CreatedAt);
    }

    // ── Search Index Integration Tests (P3-034) ──

    [Fact]
    public async Task DeprecatePattern_DeletesFromSearchIndex()
    {
        var stubIndexing = new StubPatternIndexingService();
        var service = new PatternGovernanceService(
            _db, _auditWriter,
            NullLogger<PatternGovernanceService>.Instance,
            stubIndexing);
        var entity = SeedPattern(TrustLevel.Approved);

        await service.DeprecatePatternAsync(
            TenantId, entity.PatternId, ActorId, CorrelationId,
            new DeprecatePatternRequest { Reason = "Obsolete" });

        Assert.Empty(stubIndexing.IndexedPatterns);
        Assert.Single(stubIndexing.DeletedPatternIds);
        Assert.Equal(entity.PatternId, stubIndexing.DeletedPatternIds[0]);
    }

    [Fact]
    public async Task ReviewPattern_ReindexesInSearchIndex()
    {
        var stubIndexing = new StubPatternIndexingService();
        var service = new PatternGovernanceService(
            _db, _auditWriter,
            NullLogger<PatternGovernanceService>.Instance,
            stubIndexing);
        var entity = SeedPattern(TrustLevel.Draft);

        await service.ReviewPatternAsync(
            TenantId, entity.PatternId, ActorId, CorrelationId,
            new ReviewPatternRequest { Notes = "OK" });

        Assert.Single(stubIndexing.IndexedPatterns);
        Assert.Equal(TrustLevel.Reviewed, stubIndexing.IndexedPatterns[0].TrustLevel);
        Assert.Empty(stubIndexing.DeletedPatternIds);
    }

    [Fact]
    public async Task ApprovePattern_ReindexesInSearchIndex()
    {
        var stubIndexing = new StubPatternIndexingService();
        var service = new PatternGovernanceService(
            _db, _auditWriter,
            NullLogger<PatternGovernanceService>.Instance,
            stubIndexing);
        var entity = SeedPattern(TrustLevel.Reviewed);

        await service.ApprovePatternAsync(
            TenantId, entity.PatternId, ActorId, CorrelationId,
            new ApprovePatternRequest { Notes = "Approved" });

        Assert.Single(stubIndexing.IndexedPatterns);
        Assert.Equal(TrustLevel.Approved, stubIndexing.IndexedPatterns[0].TrustLevel);
        Assert.Empty(stubIndexing.DeletedPatternIds);
    }

    [Fact]
    public async Task DeprecatePattern_GracefullyHandlesIndexingFailure()
    {
        var stubIndexing = new StubPatternIndexingService { ShouldThrow = true };
        var service = new PatternGovernanceService(
            _db, _auditWriter,
            NullLogger<PatternGovernanceService>.Instance,
            stubIndexing);
        var entity = SeedPattern(TrustLevel.Approved);

        var result = await service.DeprecatePatternAsync(
            TenantId, entity.PatternId, ActorId, CorrelationId,
            new DeprecatePatternRequest { Reason = "Obsolete" });

        // Transition should succeed even if index deletion fails.
        Assert.NotNull(result);
        Assert.Equal("Deprecated", result.NewTrustLevel);
    }

    [Fact]
    public async Task ApprovePattern_GracefullyHandlesIndexingFailure()
    {
        var stubIndexing = new StubPatternIndexingService { ShouldThrow = true };
        var service = new PatternGovernanceService(
            _db, _auditWriter,
            NullLogger<PatternGovernanceService>.Instance,
            stubIndexing);
        var entity = SeedPattern(TrustLevel.Draft);

        var result = await service.ApprovePatternAsync(
            TenantId, entity.PatternId, ActorId, CorrelationId,
            new ApprovePatternRequest());

        // Transition should succeed even if indexing fails.
        Assert.NotNull(result);
        Assert.Equal("Approved", result.NewTrustLevel);
    }

    [Fact]
    public async Task DeprecateFromDraft_DeletesFromSearchIndex()
    {
        var stubIndexing = new StubPatternIndexingService();
        var service = new PatternGovernanceService(
            _db, _auditWriter,
            NullLogger<PatternGovernanceService>.Instance,
            stubIndexing);
        var entity = SeedPattern(TrustLevel.Draft);

        await service.DeprecatePatternAsync(
            TenantId, entity.PatternId, ActorId, CorrelationId,
            new DeprecatePatternRequest { Reason = "Bad pattern" });

        Assert.Empty(stubIndexing.IndexedPatterns);
        Assert.Single(stubIndexing.DeletedPatternIds);
    }

    [Fact]
    public async Task DeprecateFromReviewed_DeletesFromSearchIndex()
    {
        var stubIndexing = new StubPatternIndexingService();
        var service = new PatternGovernanceService(
            _db, _auditWriter,
            NullLogger<PatternGovernanceService>.Instance,
            stubIndexing);
        var entity = SeedPattern(TrustLevel.Reviewed);

        await service.DeprecatePatternAsync(
            TenantId, entity.PatternId, ActorId, CorrelationId,
            new DeprecatePatternRequest { Reason = "Superseded" });

        Assert.Empty(stubIndexing.IndexedPatterns);
        Assert.Single(stubIndexing.DeletedPatternIds);
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

    private sealed class StubPatternIndexingService : IPatternIndexingService
    {
        public List<CasePattern> IndexedPatterns { get; } = [];
        public List<string> DeletedPatternIds { get; } = [];
        public bool ShouldThrow { get; set; }

        public Task EnsureIndexAsync(CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task<IndexingResult> IndexPatternsAsync(
            IReadOnlyList<CasePattern> patterns,
            CancellationToken cancellationToken = default)
        {
            if (ShouldThrow) throw new InvalidOperationException("Index service unavailable");
            IndexedPatterns.AddRange(patterns);
            return Task.FromResult(new IndexingResult(patterns.Count, 0, []));
        }

        public Task<int> DeletePatternsAsync(
            IReadOnlyList<string> patternIds,
            CancellationToken cancellationToken = default)
        {
            if (ShouldThrow) throw new InvalidOperationException("Index service unavailable");
            DeletedPatternIds.AddRange(patternIds);
            return Task.FromResult(patternIds.Count);
        }
    }
}
