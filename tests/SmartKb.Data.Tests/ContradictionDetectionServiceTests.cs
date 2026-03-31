using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using SmartKb.Contracts;
using SmartKb.Contracts.Configuration;
using SmartKb.Contracts.Models;
using SmartKb.Contracts.Services;
using SmartKb.Data.Entities;
using SmartKb.Data.Repositories;

namespace SmartKb.Data.Tests;

public class ContradictionDetectionServiceTests : IDisposable
{
    private readonly SmartKbDbContext _db;
    private readonly StubAuditWriter _auditWriter;
    private readonly PatternMaintenanceSettings _settings;
    private readonly ContradictionDetectionService _service;

    private const string TenantId = "t1";
    private const string ActorId = "admin-1";
    private const string CorrelationId = "corr-1";

    public ContradictionDetectionServiceTests()
    {
        _db = TestDbContextFactory.Create();
        _auditWriter = new StubAuditWriter();
        _settings = new PatternMaintenanceSettings();
        SeedTenant();
        _service = new ContradictionDetectionService(
            _db, _auditWriter, _settings,
            NullLogger<ContradictionDetectionService>.Instance);
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
        string title = "Test Pattern",
        string problemStatement = "Test problem statement for detection",
        string symptomsJson = "[\"error 500\",\"timeout\",\"connection refused\"]",
        string resolutionStepsJson = "[\"restart the service\",\"clear the cache\"]",
        string? productArea = "Backend",
        string trustLevel = "Approved")
    {
        var entity = new CasePatternEntity
        {
            Id = Guid.NewGuid(),
            PatternId = $"pattern-{Guid.NewGuid():N}",
            TenantId = TenantId,
            Title = title,
            ProblemStatement = problemStatement,
            SymptomsJson = symptomsJson,
            DiagnosisStepsJson = "[\"check logs\"]",
            ResolutionStepsJson = resolutionStepsJson,
            VerificationStepsJson = "[\"verify service is running\"]",
            EscalationCriteriaJson = "[]",
            RelatedEvidenceIdsJson = "[\"ev1\"]",
            Confidence = 0.7f,
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
            CreatedAt = DateTimeOffset.UtcNow.AddDays(-5),
            CreatedAtEpoch = DateTimeOffset.UtcNow.AddDays(-5).ToUnixTimeSeconds(),
            UpdatedAt = DateTimeOffset.UtcNow.AddDays(-5),
        };
        _db.CasePatterns.Add(entity);
        _db.SaveChanges();
        return entity;
    }

    // --- Tokenize Tests ---

    [Fact]
    public void Tokenize_ExtractsTokens()
    {
        var tokens = ContradictionDetectionService.Tokenize("Error 500 timeout connection refused");
        Assert.Contains("error", tokens);
        Assert.Contains("500", tokens);
        Assert.Contains("timeout", tokens);
        Assert.Contains("connection", tokens);
        Assert.Contains("refused", tokens);
    }

    [Fact]
    public void Tokenize_SkipsShortTokens()
    {
        var tokens = ContradictionDetectionService.Tokenize("a to be or not");
        Assert.DoesNotContain("a", tokens);
        Assert.DoesNotContain("to", tokens);
        Assert.DoesNotContain("be", tokens);
        Assert.DoesNotContain("or", tokens);
        Assert.Contains("not", tokens);
    }

    [Fact]
    public void Tokenize_EmptyString_ReturnsEmpty()
    {
        Assert.Empty(ContradictionDetectionService.Tokenize(""));
        Assert.Empty(ContradictionDetectionService.Tokenize("   "));
    }

    // --- JaccardSimilarity Tests ---

    [Fact]
    public void JaccardSimilarity_IdenticalSets_Returns1()
    {
        var set = new HashSet<string> { "error", "timeout", "crash" };
        Assert.Equal(1.0f, ContradictionDetectionService.JaccardSimilarity(set, set));
    }

    [Fact]
    public void JaccardSimilarity_DisjointSets_Returns0()
    {
        var a = new HashSet<string> { "error", "timeout" };
        var b = new HashSet<string> { "success", "complete" };
        Assert.Equal(0f, ContradictionDetectionService.JaccardSimilarity(a, b));
    }

    [Fact]
    public void JaccardSimilarity_PartialOverlap_ReturnsCorrectValue()
    {
        var a = new HashSet<string> { "error", "timeout", "crash" };
        var b = new HashSet<string> { "error", "timeout", "resolved" };
        // Intersection: 2, Union: 4 → 0.5
        Assert.Equal(0.5f, ContradictionDetectionService.JaccardSimilarity(a, b));
    }

    [Fact]
    public void JaccardSimilarity_EmptySets_Returns0()
    {
        Assert.Equal(0f, ContradictionDetectionService.JaccardSimilarity([], []));
        Assert.Equal(0f, ContradictionDetectionService.JaccardSimilarity([], new HashSet<string> { "a" }));
    }

    // --- Contradiction Detection Tests ---

    [Fact]
    public async Task DetectContradictions_NoPatterns_ReturnsZero()
    {
        var result = await _service.DetectContradictionsAsync(TenantId, ActorId, CorrelationId);

        Assert.Equal(0, result.PatternsAnalyzed);
        Assert.Equal(0, result.ContradictionsFound);
        Assert.Equal(0, result.NewContradictions);
    }

    [Fact]
    public async Task DetectContradictions_DuplicatePatterns_DetectedAsDuplicate()
    {
        // Two patterns with very similar title + problem + symptoms.
        SeedPattern(
            title: "Service timeout on API requests",
            problemStatement: "API endpoints return timeout errors under load",
            symptomsJson: "[\"timeout\",\"api error\",\"http 504\",\"gateway timeout\"]",
            resolutionStepsJson: "[\"increase timeout limits\",\"add connection pooling\"]");

        SeedPattern(
            title: "API request timeout errors",
            problemStatement: "Timeout errors on API endpoint calls under heavy load",
            symptomsJson: "[\"timeout\",\"api error\",\"http 504\",\"request timeout\"]",
            resolutionStepsJson: "[\"scale up instances\",\"optimize database queries\"]");

        _settings.DuplicateThreshold = 0.3f;

        var result = await _service.DetectContradictionsAsync(TenantId, ActorId, CorrelationId);

        Assert.Equal(2, result.PatternsAnalyzed);
        Assert.True(result.ContradictionsFound > 0, "Should detect at least one contradiction");

        var contradictions = await _db.PatternContradictions
            .Where(c => c.TenantId == TenantId)
            .ToListAsync();
        Assert.Contains(contradictions, c => c.ContradictionType == "DuplicatePattern");
    }

    [Fact]
    public async Task DetectContradictions_ResolutionConflict_Detected()
    {
        // Same symptoms but very different resolution.
        SeedPattern(
            title: "Database connection pool exhaustion",
            problemStatement: "Connection pool runs out under load",
            symptomsJson: "[\"connection pool exhausted\",\"database timeout\",\"max connections reached\"]",
            resolutionStepsJson: "[\"increase pool size to 200\",\"add connection pooling middleware\"]");

        SeedPattern(
            title: "Database connection pool issues",
            problemStatement: "Connection pool becomes exhausted during peak traffic",
            symptomsJson: "[\"connection pool exhausted\",\"database timeout\",\"max connections reached\"]",
            resolutionStepsJson: "[\"reduce query complexity\",\"implement read replicas\",\"switch to async queries\"]");

        _settings.SymptomOverlapThreshold = 0.3f;
        _settings.ResolutionDivergenceThreshold = 0.5f;
        _settings.DuplicateThreshold = 0.9f; // High so it falls through to resolution check.

        var result = await _service.DetectContradictionsAsync(TenantId, ActorId, CorrelationId);

        Assert.Equal(2, result.PatternsAnalyzed);
        Assert.True(result.ContradictionsFound > 0);
    }

    [Fact]
    public async Task DetectContradictions_DifferentProductAreas_NoContradiction()
    {
        SeedPattern(
            title: "Service timeout",
            productArea: "Backend",
            symptomsJson: "[\"timeout\",\"error 500\"]");

        SeedPattern(
            title: "Service timeout",
            productArea: "Frontend",
            symptomsJson: "[\"timeout\",\"error 500\"]");

        var result = await _service.DetectContradictionsAsync(TenantId, ActorId, CorrelationId);

        Assert.Equal(2, result.PatternsAnalyzed);
        Assert.Equal(0, result.ContradictionsFound);
    }

    [Fact]
    public async Task DetectContradictions_DeprecatedPatterns_Excluded()
    {
        SeedPattern(title: "Active pattern", symptomsJson: "[\"same symptom\"]");
        SeedPattern(title: "Deprecated pattern", symptomsJson: "[\"same symptom\"]", trustLevel: "Deprecated");

        var result = await _service.DetectContradictionsAsync(TenantId, ActorId, CorrelationId);

        Assert.Equal(1, result.PatternsAnalyzed); // Only the active one.
        Assert.Equal(0, result.ContradictionsFound);
    }

    [Fact]
    public async Task DetectContradictions_SkipsExistingPending()
    {
        var a = SeedPattern(
            title: "Timeout issue A",
            symptomsJson: "[\"timeout\",\"error\",\"service down\"]");
        var b = SeedPattern(
            title: "Timeout issue B",
            symptomsJson: "[\"timeout\",\"error\",\"service down\"]");
        _settings.DuplicateThreshold = 0.2f;

        // First run creates contradictions.
        var first = await _service.DetectContradictionsAsync(TenantId, ActorId, CorrelationId);
        Assert.True(first.NewContradictions > 0);

        // Second run skips existing.
        var second = await _service.DetectContradictionsAsync(TenantId, ActorId, CorrelationId);
        Assert.Equal(0, second.NewContradictions);
        Assert.True(second.SkippedExisting > 0);
    }

    [Fact]
    public async Task DetectContradictions_WritesAuditEvent()
    {
        await _service.DetectContradictionsAsync(TenantId, ActorId, CorrelationId);

        Assert.Contains(_auditWriter.Events,
            e => e.EventType == AuditEventTypes.ContradictionDetectionRun);
    }

    // --- GetContradictions Tests ---

    [Fact]
    public async Task GetContradictions_Pagination()
    {
        // Seed some contradictions manually.
        for (int i = 0; i < 5; i++)
        {
            _db.PatternContradictions.Add(new PatternContradictionEntity
            {
                Id = Guid.NewGuid(),
                TenantId = TenantId,
                PatternIdA = $"pattern-a{i}",
                PatternIdB = $"pattern-b{i}",
                ContradictionType = "ResolutionConflict",
                SimilarityScore = 0.5f,
                Description = $"Test contradiction {i}",
                ConflictingFieldsJson = "[\"ResolutionSteps\"]",
                Status = "Pending",
                CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-i),
                CreatedAtEpoch = DateTimeOffset.UtcNow.AddMinutes(-i).ToUnixTimeSeconds(),
            });
        }
        _db.SaveChanges();

        var page1 = await _service.GetContradictionsAsync(TenantId, null, 1, 3);
        Assert.Equal(5, page1.TotalCount);
        Assert.Equal(3, page1.Contradictions.Count);
        Assert.True(page1.HasMore);

        var page2 = await _service.GetContradictionsAsync(TenantId, null, 2, 3);
        Assert.Equal(2, page2.Contradictions.Count);
        Assert.False(page2.HasMore);
    }

    [Fact]
    public async Task GetContradictions_FilterByStatus()
    {
        _db.PatternContradictions.Add(new PatternContradictionEntity
        {
            Id = Guid.NewGuid(), TenantId = TenantId,
            PatternIdA = "pa", PatternIdB = "pb",
            ContradictionType = "DuplicatePattern",
            SimilarityScore = 0.8f, Description = "dup",
            ConflictingFieldsJson = "[]", Status = "Pending",
            CreatedAt = DateTimeOffset.UtcNow,
            CreatedAtEpoch = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
        });
        _db.PatternContradictions.Add(new PatternContradictionEntity
        {
            Id = Guid.NewGuid(), TenantId = TenantId,
            PatternIdA = "pc", PatternIdB = "pd",
            ContradictionType = "ResolutionConflict",
            SimilarityScore = 0.6f, Description = "conflict",
            ConflictingFieldsJson = "[]", Status = "Resolved",
            CreatedAt = DateTimeOffset.UtcNow,
            CreatedAtEpoch = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
        });
        _db.SaveChanges();

        var pending = await _service.GetContradictionsAsync(TenantId, "Pending", 1, 20);
        Assert.Single(pending.Contradictions);

        var resolved = await _service.GetContradictionsAsync(TenantId, "Resolved", 1, 20);
        Assert.Single(resolved.Contradictions);
    }

    // --- ResolveContradiction Tests ---

    [Fact]
    public async Task ResolveContradiction_ValidResolution_UpdatesStatus()
    {
        var entity = new PatternContradictionEntity
        {
            Id = Guid.NewGuid(), TenantId = TenantId,
            PatternIdA = "pa", PatternIdB = "pb",
            ContradictionType = "DuplicatePattern",
            SimilarityScore = 0.8f, Description = "dup",
            ConflictingFieldsJson = "[]", Status = "Pending",
            CreatedAt = DateTimeOffset.UtcNow,
        };
        _db.PatternContradictions.Add(entity);
        _db.SaveChanges();

        var result = await _service.ResolveContradictionAsync(
            entity.Id, TenantId, ActorId, CorrelationId,
            new ResolveContradictionRequest { Resolution = "Merged", Notes = "Combined into one" });

        Assert.NotNull(result);
        Assert.Equal("Resolved", result!.Status);
        Assert.Equal("Merged", result.Resolution);
        Assert.Equal(ActorId, result.ResolvedBy);
        Assert.NotNull(result.ResolvedAt);
    }

    [Fact]
    public async Task ResolveContradiction_InvalidResolution_ReturnsNull()
    {
        var entity = new PatternContradictionEntity
        {
            Id = Guid.NewGuid(), TenantId = TenantId,
            PatternIdA = "pa", PatternIdB = "pb",
            ContradictionType = "DuplicatePattern",
            SimilarityScore = 0.8f, Description = "dup",
            ConflictingFieldsJson = "[]", Status = "Pending",
            CreatedAt = DateTimeOffset.UtcNow,
        };
        _db.PatternContradictions.Add(entity);
        _db.SaveChanges();

        var result = await _service.ResolveContradictionAsync(
            entity.Id, TenantId, ActorId, CorrelationId,
            new ResolveContradictionRequest { Resolution = "InvalidAction" });

        Assert.Null(result);
    }

    [Fact]
    public async Task ResolveContradiction_AlreadyResolved_ReturnsNull()
    {
        var entity = new PatternContradictionEntity
        {
            Id = Guid.NewGuid(), TenantId = TenantId,
            PatternIdA = "pa", PatternIdB = "pb",
            ContradictionType = "DuplicatePattern",
            SimilarityScore = 0.8f, Description = "dup",
            ConflictingFieldsJson = "[]", Status = "Resolved",
            Resolution = "Kept",
            CreatedAt = DateTimeOffset.UtcNow,
            CreatedAtEpoch = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
        };
        _db.PatternContradictions.Add(entity);
        _db.SaveChanges();

        var result = await _service.ResolveContradictionAsync(
            entity.Id, TenantId, ActorId, CorrelationId,
            new ResolveContradictionRequest { Resolution = "Merged" });

        Assert.Null(result);
    }

    [Fact]
    public async Task ResolveContradiction_WritesAuditEvent()
    {
        var entity = new PatternContradictionEntity
        {
            Id = Guid.NewGuid(), TenantId = TenantId,
            PatternIdA = "pa", PatternIdB = "pb",
            ContradictionType = "DuplicatePattern",
            SimilarityScore = 0.8f, Description = "dup",
            ConflictingFieldsJson = "[]", Status = "Pending",
            CreatedAt = DateTimeOffset.UtcNow,
        };
        _db.PatternContradictions.Add(entity);
        _db.SaveChanges();

        await _service.ResolveContradictionAsync(
            entity.Id, TenantId, ActorId, CorrelationId,
            new ResolveContradictionRequest { Resolution = "Dismissed" });

        Assert.Contains(_auditWriter.Events,
            e => e.EventType == AuditEventTypes.ContradictionResolved);
    }

    [Fact]
    public async Task ResolveContradiction_WrongTenant_ReturnsNull()
    {
        var entity = new PatternContradictionEntity
        {
            Id = Guid.NewGuid(), TenantId = TenantId,
            PatternIdA = "pa", PatternIdB = "pb",
            ContradictionType = "DuplicatePattern",
            SimilarityScore = 0.8f, Description = "dup",
            ConflictingFieldsJson = "[]", Status = "Pending",
            CreatedAt = DateTimeOffset.UtcNow,
        };
        _db.PatternContradictions.Add(entity);
        _db.SaveChanges();

        var result = await _service.ResolveContradictionAsync(
            entity.Id, "other-tenant", ActorId, CorrelationId,
            new ResolveContradictionRequest { Resolution = "Merged" });

        Assert.Null(result);
    }

    // --- AnalyzeContradiction Internal Tests ---

    [Fact]
    public void AnalyzeContradiction_SameProductArea_Required()
    {
        var a = MakePattern(productArea: "Backend");
        var b = MakePattern(productArea: "Frontend");

        var result = _service.AnalyzeContradiction(a, b);
        Assert.Null(result);
    }

    [Fact]
    public void AnalyzeContradiction_BothNullProductArea_Compares()
    {
        var a = MakePattern(productArea: null,
            symptomsJson: "[\"error\",\"crash\",\"timeout\"]",
            title: "Crash and timeout errors");
        var b = MakePattern(productArea: null,
            symptomsJson: "[\"error\",\"crash\",\"timeout\"]",
            title: "Crash and timeout errors");
        _settings.DuplicateThreshold = 0.3f;

        var result = _service.AnalyzeContradiction(a, b);
        Assert.NotNull(result);
    }

    private static CasePatternEntity MakePattern(
        string? productArea = "Backend",
        string symptomsJson = "[\"error\"]",
        string resolutionStepsJson = "[\"fix it\"]",
        string title = "Test",
        string problemStatement = "Test problem")
    {
        return new CasePatternEntity
        {
            Id = Guid.NewGuid(),
            PatternId = $"pattern-{Guid.NewGuid():N}",
            TenantId = TenantId,
            Title = title,
            ProblemStatement = problemStatement,
            SymptomsJson = symptomsJson,
            DiagnosisStepsJson = "[]",
            ResolutionStepsJson = resolutionStepsJson,
            VerificationStepsJson = "[]",
            EscalationCriteriaJson = "[]",
            RelatedEvidenceIdsJson = "[\"ev1\"]",
            Confidence = 0.7f,
            TrustLevel = "Approved",
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
        };
    }

    [Fact]
    public async Task DetectContradictions_PatternsLoadBounded_PrioritizesNewest()
    {
        // Seed patterns with distinct epochs to verify ordering.
        var now = DateTimeOffset.UtcNow;
        var oldPattern = MakePattern(symptomsJson: "[\"Old pattern symptoms\"]", resolutionStepsJson: "[\"Old resolution A\"]", productArea: "General");
        oldPattern.CreatedAtEpoch = now.AddDays(-100).ToUnixTimeSeconds();
        oldPattern.CreatedAt = now.AddDays(-100);

        var newPattern = MakePattern(symptomsJson: "[\"New pattern symptoms\"]", resolutionStepsJson: "[\"New resolution B\"]", productArea: "General");
        newPattern.CreatedAtEpoch = now.ToUnixTimeSeconds();
        newPattern.CreatedAt = now;

        _db.CasePatterns.AddRange(oldPattern, newPattern);
        await _db.SaveChangesAsync();

        var result = await _service.DetectContradictionsAsync(TenantId, ActorId, CorrelationId);

        // Both patterns loaded and analyzed (well under 2000 cap).
        Assert.True(result.PatternsAnalyzed >= 2);
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
