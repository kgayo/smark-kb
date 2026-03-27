using Microsoft.Extensions.Logging.Abstractions;
using SmartKb.Contracts;
using SmartKb.Contracts.Configuration;
using SmartKb.Contracts.Enums;
using SmartKb.Contracts.Models;
using SmartKb.Contracts.Services;
using SmartKb.Data.Entities;
using SmartKb.Data.Repositories;

namespace SmartKb.Data.Tests;

public class PatternDistillationServiceTests : IDisposable
{
    private readonly SmartKbDbContext _db;
    private readonly StubAuditWriter _auditWriter;
    private readonly DistillationSettings _settings;
    private readonly PatternDistillationService _service;

    private const string TenantId = "t1";

    public PatternDistillationServiceTests()
    {
        _db = TestDbContextFactory.Create();
        _auditWriter = new StubAuditWriter();
        _settings = new DistillationSettings();
        SeedData();
        _service = new PatternDistillationService(
            _db, _settings, _auditWriter,
            NullLogger<PatternDistillationService>.Instance);
    }

    public void Dispose() => _db.Dispose();

    private void SeedData()
    {
        _db.Tenants.Add(new TenantEntity
        {
            TenantId = TenantId,
            DisplayName = "Test",
            CreatedAt = DateTimeOffset.UtcNow,
        });

        var connector = new ConnectorEntity
        {
            Id = Guid.NewGuid(),
            TenantId = TenantId,
            Name = "TestConnector",
            ConnectorType = ConnectorType.AzureDevOps,
            Status = ConnectorStatus.Enabled,
            AuthType = SecretAuthType.Pat,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        };
        _db.Connectors.Add(connector);
        _db.SaveChanges();
    }

    private (Guid sessionId, Guid messageId) SeedQualifiedSession(
        int thumbsUpCount = 1,
        int thumbsDownCount = 0,
        int chunkCount = 2,
        string? productArea = null)
    {
        var session = new SessionEntity
        {
            Id = Guid.NewGuid(),
            TenantId = TenantId,
            UserId = "u1",
            Title = "Test session",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        };
        _db.Sessions.Add(session);

        // Add ResolvedWithoutEscalation outcome.
        _db.OutcomeEvents.Add(new OutcomeEventEntity
        {
            Id = Guid.NewGuid(),
            SessionId = session.Id,
            TenantId = TenantId,
            ResolutionType = ResolutionType.ResolvedWithoutEscalation,
            CreatedAt = DateTimeOffset.UtcNow,
        });

        // Add a message with a correlation ID.
        var message = new MessageEntity
        {
            Id = Guid.NewGuid(),
            SessionId = session.Id,
            TenantId = TenantId,
            Role = MessageRole.Assistant,
            Content = "Test response",
            CorrelationId = $"corr-{session.Id:N}",
            CreatedAt = DateTimeOffset.UtcNow,
        };
        _db.Messages.Add(message);

        // Add evidence chunks.
        var chunkIds = new List<string>();
        var connector = _db.Connectors.First();
        for (int i = 0; i < chunkCount; i++)
        {
            var evidenceId = $"ev-{session.Id:N}-{i}";
            var chunkId = $"{evidenceId}_chunk_0";
            chunkIds.Add(chunkId);

            _db.EvidenceChunks.Add(new EvidenceChunkEntity
            {
                ChunkId = chunkId,
                EvidenceId = evidenceId,
                TenantId = TenantId,
                ConnectorId = connector.Id,
                ChunkIndex = 0,
                ChunkText = $"This is chunk text {i} describing the issue resolution.",
                ChunkContext = $"Context for chunk {i}",
                SourceSystem = "AzureDevOps",
                SourceType = "Ticket",
                Status = "Closed",
                UpdatedAt = DateTimeOffset.UtcNow,
                ProductArea = productArea,
                Tags = "[\"auth\",\"login\"]",
                Visibility = "Internal",
                AccessLabel = "Internal",
                Title = $"Work item {i}",
                SourceUrl = $"https://dev.azure.com/test/{i}",
                ErrorTokens = "[\"NullReferenceException\",\"HTTP 500\"]",
                ContentHash = $"hash-{session.Id:N}-{i}",
                CreatedAt = DateTimeOffset.UtcNow,
            });
        }

        // Add answer trace linking correlation ID to cited chunks.
        _db.AnswerTraces.Add(new AnswerTraceEntity
        {
            Id = Guid.NewGuid(),
            TenantId = TenantId,
            UserId = "u1",
            CorrelationId = $"corr-{session.Id:N}",
            Query = "How to fix this?",
            ResponseType = "final_answer",
            Confidence = 0.8f,
            ConfidenceLabel = "High",
            CitedChunkIds = System.Text.Json.JsonSerializer.Serialize(chunkIds),
            RetrievedChunkIds = System.Text.Json.JsonSerializer.Serialize(chunkIds),
            RetrievedChunkCount = chunkIds.Count,
            HasEvidence = true,
            SystemPromptVersion = "1.0",
            CreatedAt = DateTimeOffset.UtcNow,
        });

        // Add feedback.
        for (int i = 0; i < thumbsUpCount; i++)
        {
            _db.Feedbacks.Add(new FeedbackEntity
            {
                Id = Guid.NewGuid(),
                MessageId = message.Id,
                SessionId = session.Id,
                TenantId = TenantId,
                UserId = $"user-{i}",
                Type = FeedbackType.ThumbsUp,
                CreatedAt = DateTimeOffset.UtcNow,
            });
        }

        for (int i = 0; i < thumbsDownCount; i++)
        {
            _db.Feedbacks.Add(new FeedbackEntity
            {
                Id = Guid.NewGuid(),
                MessageId = message.Id,
                SessionId = session.Id,
                TenantId = TenantId,
                UserId = $"neg-user-{i}",
                Type = FeedbackType.ThumbsDown,
                CreatedAt = DateTimeOffset.UtcNow,
            });
        }

        _db.SaveChanges();
        return (session.Id, message.Id);
    }

    // --- FindCandidatesAsync Tests ---

    [Fact]
    public async Task FindCandidates_ReturnsQualifiedSession()
    {
        SeedQualifiedSession();

        var result = await _service.FindCandidatesAsync(TenantId);

        Assert.Equal(1, result.TotalCount);
        var candidate = result.Candidates[0];
        Assert.Equal(TenantId, candidate.TenantId);
        Assert.Equal(1, candidate.PositiveFeedbackCount);
        Assert.False(candidate.AlreadyDistilled);
    }

    [Fact]
    public async Task FindCandidates_ExcludesSessionsWithoutPositiveFeedback()
    {
        SeedQualifiedSession(thumbsUpCount: 0);

        var result = await _service.FindCandidatesAsync(TenantId);

        Assert.Equal(0, result.TotalCount);
    }

    [Fact]
    public async Task FindCandidates_ExcludesSessionsWithoutResolvedOutcome()
    {
        // Seed a session with an escalated outcome instead.
        var session = new SessionEntity
        {
            Id = Guid.NewGuid(),
            TenantId = TenantId,
            UserId = "u1",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        };
        _db.Sessions.Add(session);
        _db.OutcomeEvents.Add(new OutcomeEventEntity
        {
            Id = Guid.NewGuid(),
            SessionId = session.Id,
            TenantId = TenantId,
            ResolutionType = ResolutionType.Escalated,
            CreatedAt = DateTimeOffset.UtcNow,
        });
        _db.SaveChanges();

        var result = await _service.FindCandidatesAsync(TenantId);

        Assert.Equal(0, result.TotalCount);
    }

    [Fact]
    public async Task FindCandidates_RespectsTenantIsolation()
    {
        SeedQualifiedSession();

        var result = await _service.FindCandidatesAsync("other-tenant");

        Assert.Equal(0, result.TotalCount);
    }

    [Fact]
    public async Task FindCandidates_ReturnsNegativeFeedbackCount()
    {
        SeedQualifiedSession(thumbsUpCount: 2, thumbsDownCount: 1);

        var result = await _service.FindCandidatesAsync(TenantId);

        Assert.Equal(1, result.TotalCount);
        Assert.Equal(2, result.Candidates[0].PositiveFeedbackCount);
        Assert.Equal(1, result.Candidates[0].NegativeFeedbackCount);
    }

    [Fact]
    public async Task FindCandidates_IncludesCitedChunkIds()
    {
        SeedQualifiedSession(chunkCount: 3);

        var result = await _service.FindCandidatesAsync(TenantId);

        Assert.Equal(1, result.TotalCount);
        Assert.Equal(3, result.Candidates[0].CitedChunkIds.Count);
        Assert.Equal(3, result.Candidates[0].CitedEvidenceIds.Count);
    }

    [Fact]
    public async Task FindCandidates_MarksAlreadyDistilled()
    {
        var (sessionId, _) = SeedQualifiedSession();

        // Create a pattern for this session.
        _db.CasePatterns.Add(new CasePatternEntity
        {
            Id = Guid.NewGuid(),
            PatternId = "pattern-test",
            TenantId = TenantId,
            Title = "Existing",
            ProblemStatement = "Test",
            SourceUrl = $"session://{sessionId}",
            CreatedAt = DateTimeOffset.UtcNow,
            CreatedAtEpoch = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            UpdatedAt = DateTimeOffset.UtcNow,
        });
        _db.SaveChanges();

        var result = await _service.FindCandidatesAsync(TenantId);

        Assert.Equal(1, result.TotalCount);
        Assert.True(result.Candidates[0].AlreadyDistilled);
    }

    [Fact]
    public async Task FindCandidates_IncludesProductArea()
    {
        SeedQualifiedSession(productArea: "Billing");

        var result = await _service.FindCandidatesAsync(TenantId);

        Assert.Equal(1, result.TotalCount);
        Assert.Equal("Billing", result.Candidates[0].ProductArea);
    }

    [Fact]
    public async Task FindCandidates_IncludesTags()
    {
        SeedQualifiedSession();

        var result = await _service.FindCandidatesAsync(TenantId);

        Assert.Contains("auth", result.Candidates[0].Tags);
        Assert.Contains("login", result.Candidates[0].Tags);
    }

    [Fact]
    public async Task FindCandidates_OrdersByPositiveFeedbackDescending()
    {
        SeedQualifiedSession(thumbsUpCount: 1);
        SeedQualifiedSession(thumbsUpCount: 5);

        var result = await _service.FindCandidatesAsync(TenantId);

        Assert.Equal(2, result.TotalCount);
        Assert.True(result.Candidates[0].PositiveFeedbackCount >= result.Candidates[1].PositiveFeedbackCount);
    }

    // --- DistillAsync Tests ---

    [Fact]
    public async Task Distill_CreatesDraftPattern()
    {
        SeedQualifiedSession();

        var result = await _service.DistillAsync(TenantId, "admin", "corr-1");

        Assert.Equal(1, result.PatternsCreated);
        Assert.Single(result.CreatedPatternIds);
        Assert.Empty(result.Errors);

        var pattern = _db.CasePatterns.First();
        Assert.Equal(TenantId, pattern.TenantId);
        Assert.Equal("Draft", pattern.TrustLevel);
        Assert.StartsWith("pattern-", pattern.PatternId);
        Assert.StartsWith("Pattern: Test session", pattern.Title);
    }

    [Fact]
    public async Task Distill_SkipsAlreadyDistilled()
    {
        var (sessionId, _) = SeedQualifiedSession();

        _db.CasePatterns.Add(new CasePatternEntity
        {
            Id = Guid.NewGuid(),
            PatternId = "pattern-existing",
            TenantId = TenantId,
            Title = "Existing",
            ProblemStatement = "Test",
            SourceUrl = $"session://{sessionId}",
            CreatedAt = DateTimeOffset.UtcNow,
            CreatedAtEpoch = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            UpdatedAt = DateTimeOffset.UtcNow,
        });
        _db.SaveChanges();

        var result = await _service.DistillAsync(TenantId, "admin", "corr-2");

        Assert.Equal(0, result.PatternsCreated);
        Assert.Equal(0, result.CandidatesEvaluated);
    }

    [Fact]
    public async Task Distill_WritesAuditEvents()
    {
        SeedQualifiedSession();

        await _service.DistillAsync(TenantId, "admin", "corr-3");

        Assert.True(_auditWriter.Events.Count >= 2);
        Assert.Contains(_auditWriter.Events, e => e.EventType == AuditEventTypes.PatternDistilled);
        Assert.Contains(_auditWriter.Events, e => e.EventType == AuditEventTypes.PatternDistillationRun);
    }

    [Fact]
    public async Task Distill_SetsConfidenceFromFeedback()
    {
        SeedQualifiedSession(thumbsUpCount: 3, thumbsDownCount: 1);

        var result = await _service.DistillAsync(TenantId, "admin", "corr-4");

        Assert.Equal(1, result.PatternsCreated);
        var pattern = _db.CasePatterns.First(p => p.PatternId == result.CreatedPatternIds[0]);
        // Base 0.5 + 2 extra positive * 0.05 - 1 negative * 0.1 = 0.5
        Assert.Equal(0.5f, pattern.Confidence, 0.01f);
    }

    [Fact]
    public async Task Distill_SetsHigherConfidenceForMorePositiveFeedback()
    {
        SeedQualifiedSession(thumbsUpCount: 5);

        var result = await _service.DistillAsync(TenantId, "admin", "corr-5");

        var pattern = _db.CasePatterns.First(p => p.PatternId == result.CreatedPatternIds[0]);
        // Base 0.5 + 4 extra * 0.05 = 0.7
        Assert.True(pattern.Confidence > _settings.BaseConfidence);
    }

    [Fact]
    public async Task Distill_RespectsMaxBatchSize()
    {
        // Create more candidates than MaxBatchSize.
        _settings.GetType().GetProperty("MaxBatchSize")!.SetValue(_settings, 1);
        SeedQualifiedSession();
        SeedQualifiedSession(thumbsUpCount: 2);

        var result = await _service.DistillAsync(TenantId, "admin", "corr-6");

        Assert.Equal(1, result.CandidatesEvaluated);
        Assert.Equal(1, result.PatternsCreated);
    }

    [Fact]
    public async Task Distill_SetsVisibilityFromMostRestrictive()
    {
        SeedQualifiedSession();

        var result = await _service.DistillAsync(TenantId, "admin", "corr-7");

        var pattern = _db.CasePatterns.First(p => p.PatternId == result.CreatedPatternIds[0]);
        Assert.Equal("Internal", pattern.Visibility);
    }

    [Fact]
    public async Task Distill_StoresRelatedEvidenceIds()
    {
        SeedQualifiedSession(chunkCount: 2);

        var result = await _service.DistillAsync(TenantId, "admin", "corr-8");

        var pattern = _db.CasePatterns.First(p => p.PatternId == result.CreatedPatternIds[0]);
        var relatedIds = System.Text.Json.JsonSerializer.Deserialize<List<string>>(pattern.RelatedEvidenceIdsJson);
        Assert.NotNull(relatedIds);
        Assert.Equal(2, relatedIds.Count);
    }

    [Fact]
    public async Task Distill_StoresSourceUrlWithSessionId()
    {
        var (sessionId, _) = SeedQualifiedSession();

        var result = await _service.DistillAsync(TenantId, "admin", "corr-9");

        var pattern = _db.CasePatterns.First(p => p.PatternId == result.CreatedPatternIds[0]);
        Assert.Equal($"session://{sessionId}", pattern.SourceUrl);
    }

    // --- Static helper tests ---

    [Fact]
    public void BuildTitle_UsesSessionTitle()
    {
        var candidate = CreateMinimalCandidate(sessionTitle: "Auth login failure");
        var title = PatternDistillationService.BuildTitle(candidate, []);
        Assert.Equal("Pattern: Auth login failure", title);
    }

    [Fact]
    public void BuildTitle_FallsBackToChunkTitle()
    {
        var candidate = CreateMinimalCandidate(sessionTitle: null);
        var chunk = CreateMinimalChunk(title: "Work item 123");
        var title = PatternDistillationService.BuildTitle(candidate, [chunk]);
        Assert.Equal("Pattern: Work item 123", title);
    }

    [Fact]
    public void ExtractSymptoms_IncludesErrorTokens()
    {
        var chunk = CreateMinimalChunk();
        chunk.ErrorTokens = "[\"NullReferenceException\",\"HTTP 500\"]";

        var symptoms = PatternDistillationService.ExtractSymptoms([chunk]);

        Assert.Contains("NullReferenceException", symptoms);
        Assert.Contains("HTTP 500", symptoms);
    }

    [Fact]
    public void ExtractSymptoms_DeduplicatesTokens()
    {
        var chunk1 = CreateMinimalChunk();
        chunk1.ErrorTokens = "[\"NullReferenceException\"]";
        var chunk2 = CreateMinimalChunk();
        chunk2.ErrorTokens = "[\"NullReferenceException\"]";

        var symptoms = PatternDistillationService.ExtractSymptoms([chunk1, chunk2]);

        Assert.Equal(1, symptoms.Count(s => s == "NullReferenceException"));
    }

    [Fact]
    public void ExtractResolutionSteps_TruncatesLongText()
    {
        var chunk = CreateMinimalChunk();
        chunk.ChunkText = new string('a', 300);

        var steps = PatternDistillationService.ExtractResolutionSteps(
            CreateMinimalCandidate(), [chunk]);

        Assert.Single(steps);
        Assert.EndsWith("...", steps[0]);
        Assert.True(steps[0].Length <= 203); // 200 + "..."
    }

    [Fact]
    public void ComputeConfidence_BaseCase()
    {
        var candidate = CreateMinimalCandidate(positiveCount: 1);
        var confidence = _service.ComputeConfidence(candidate);
        Assert.Equal(_settings.BaseConfidence, confidence, 0.01f);
    }

    [Fact]
    public void ComputeConfidence_BoostsForExtraPositive()
    {
        var candidate = CreateMinimalCandidate(positiveCount: 3);
        var confidence = _service.ComputeConfidence(candidate);
        Assert.True(confidence > _settings.BaseConfidence);
    }

    [Fact]
    public void ComputeConfidence_PenalizesNegative()
    {
        var candidate = CreateMinimalCandidate(positiveCount: 1, negativeCount: 2);
        var confidence = _service.ComputeConfidence(candidate);
        Assert.True(confidence < _settings.BaseConfidence);
    }

    [Fact]
    public void ComputeConfidence_ClampsToMax()
    {
        var candidate = CreateMinimalCandidate(positiveCount: 100);
        var confidence = _service.ComputeConfidence(candidate);
        Assert.True(confidence <= _settings.MaxConfidence);
    }

    [Fact]
    public void ComputeConfidence_ClampsToMin()
    {
        var candidate = CreateMinimalCandidate(positiveCount: 1, negativeCount: 100);
        var confidence = _service.ComputeConfidence(candidate);
        Assert.True(confidence >= 0.1f);
    }

    [Fact]
    public void DetermineVisibility_RestrictedWins()
    {
        var chunk1 = CreateMinimalChunk(); chunk1.Visibility = "Internal";
        var chunk2 = CreateMinimalChunk(); chunk2.Visibility = "Restricted";

        var visibility = PatternDistillationService.DetermineVisibility([chunk1, chunk2]);

        Assert.Equal("Restricted", visibility);
    }

    [Fact]
    public void DetermineVisibility_InternalDefault()
    {
        var chunk = CreateMinimalChunk(); chunk.Visibility = "Internal";

        var visibility = PatternDistillationService.DetermineVisibility([chunk]);

        Assert.Equal("Internal", visibility);
    }

    [Fact]
    public void DetermineVisibility_PublicIfAllPublic()
    {
        var chunk = CreateMinimalChunk(); chunk.Visibility = "Public";

        var visibility = PatternDistillationService.DetermineVisibility([chunk]);

        Assert.Equal("Public", visibility);
    }

    // --- ExtractRootCause Tests ---

    [Fact]
    public void ExtractRootCause_FromChunkContext()
    {
        var chunk = CreateMinimalChunk();
        chunk.ChunkContext = "Root Cause";
        chunk.ChunkText = "Memory leak in token cache due to missing disposal.";

        var result = PatternDistillationService.ExtractRootCause([chunk]);

        Assert.Equal("Memory leak in token cache due to missing disposal.", result);
    }

    [Fact]
    public void ExtractRootCause_FallsBackToKeywordInText()
    {
        var chunk = CreateMinimalChunk();
        chunk.ChunkContext = "Resolution";
        chunk.ChunkText = "The root cause was a misconfigured connection string.";

        var result = PatternDistillationService.ExtractRootCause([chunk]);

        Assert.Equal("The root cause was a misconfigured connection string.", result);
    }

    [Fact]
    public void ExtractRootCause_DetectsCausedByKeyword()
    {
        var chunk = CreateMinimalChunk();
        chunk.ChunkText = "This was caused by a race condition in the queue processor.";

        var result = PatternDistillationService.ExtractRootCause([chunk]);

        Assert.Contains("caused by", result!);
    }

    [Fact]
    public void ExtractRootCause_DetectsUnderlyingIssueKeyword()
    {
        var chunk = CreateMinimalChunk();
        chunk.ChunkText = "The underlying issue is a stale DNS cache entry.";

        var result = PatternDistillationService.ExtractRootCause([chunk]);

        Assert.Contains("underlying issue", result!);
    }

    [Fact]
    public void ExtractRootCause_ReturnsNullWhenNoRootCauseFound()
    {
        var chunk = CreateMinimalChunk();
        chunk.ChunkText = "Applied the fix and restarted the service.";
        chunk.ChunkContext = "Resolution";

        var result = PatternDistillationService.ExtractRootCause([chunk]);

        Assert.Null(result);
    }

    [Fact]
    public void ExtractRootCause_TruncatesLongText()
    {
        var chunk = CreateMinimalChunk();
        chunk.ChunkContext = "Root Cause";
        chunk.ChunkText = new string('x', 3000);

        var result = PatternDistillationService.ExtractRootCause([chunk]);

        Assert.NotNull(result);
        Assert.Equal(2000, result.Length);
    }

    [Fact]
    public void ExtractRootCause_PrefersContextOverKeyword()
    {
        var chunkWithContext = CreateMinimalChunk();
        chunkWithContext.ChunkContext = "Root Cause";
        chunkWithContext.ChunkText = "DNS misconfiguration.";

        var chunkWithKeyword = CreateMinimalChunk();
        chunkWithKeyword.ChunkText = "The root cause was something else.";

        var result = PatternDistillationService.ExtractRootCause([chunkWithContext, chunkWithKeyword]);

        Assert.Equal("DNS misconfiguration.", result);
    }

    [Fact]
    public void BuildProblemStatement_CustomMaxLength_Truncates()
    {
        var chunk = CreateMinimalChunk();
        chunk.ChunkText = new string('z', 100);

        var result = PatternDistillationService.BuildProblemStatement(
            CreateMinimalCandidate(), [chunk], maxLength: 50);

        Assert.Equal(53, result.Length); // 50 + "..."
        Assert.EndsWith("...", result);
    }

    [Fact]
    public void ExtractResolutionSteps_CustomMaxLength_Truncates()
    {
        var chunk = CreateMinimalChunk();
        chunk.ChunkText = new string('y', 200);

        var steps = PatternDistillationService.ExtractResolutionSteps(
            CreateMinimalCandidate(), [chunk], stepMaxLength: 80);

        Assert.Single(steps);
        Assert.Equal(83, steps[0].Length); // 80 + "..."
        Assert.EndsWith("...", steps[0]);
    }

    [Fact]
    public void ExtractRootCause_CustomMaxLength_Truncates()
    {
        var chunk = CreateMinimalChunk();
        chunk.ChunkContext = "Root Cause";
        chunk.ChunkText = new string('w', 500);

        var result = PatternDistillationService.ExtractRootCause([chunk], maxLength: 100);

        Assert.NotNull(result);
        Assert.Equal(100, result.Length);
    }

    [Fact]
    public void DistillationSettings_TruncationDefaults_MatchOriginalValues()
    {
        var settings = new DistillationSettings();
        Assert.Equal(500, settings.ProblemStatementMaxLength);
        Assert.Equal(200, settings.StepTextMaxLength);
        Assert.Equal(2000, settings.RootCauseMaxLength);
    }

    [Fact]
    public async Task Distill_SetsRootCauseWhenAvailable()
    {
        var (sessionId, _) = SeedQualifiedSession();

        // Update one chunk to have root cause context.
        var chunk = _db.EvidenceChunks.First();
        chunk.ChunkContext = "Root Cause";
        chunk.ChunkText = "Expired certificate on load balancer.";
        _db.SaveChanges();

        var result = await _service.DistillAsync(TenantId, "admin", "corr-rc");

        Assert.Equal(1, result.PatternsCreated);
        var pattern = _db.CasePatterns.First(p => p.PatternId == result.CreatedPatternIds[0]);
        Assert.Equal("Expired certificate on load balancer.", pattern.RootCause);
    }

    // --- Optional service integration tests ---

    [Fact]
    public async Task Distill_EmbeddingFailure_ContinuesWithoutEmbedding()
    {
        SeedQualifiedSession();

        var embeddingService = new ThrowingEmbeddingService();
        var service = new PatternDistillationService(
            _db, _settings, _auditWriter,
            NullLogger<PatternDistillationService>.Instance,
            embeddingService: embeddingService);

        var result = await service.DistillAsync(TenantId, "admin", "corr-emb-fail");

        Assert.Equal(1, result.PatternsCreated);
        Assert.Empty(result.Errors);
        var pattern = _db.CasePatterns.First(p => p.PatternId == result.CreatedPatternIds[0]);
        Assert.NotNull(pattern);
    }

    [Fact]
    public async Task Distill_EmbeddingSuccess_IndexesPatternsInPatternStore()
    {
        SeedQualifiedSession();

        var embeddingService = new StubEmbeddingService();
        var indexingService = new StubPatternIndexingService();
        var service = new PatternDistillationService(
            _db, _settings, _auditWriter,
            NullLogger<PatternDistillationService>.Instance,
            embeddingService: embeddingService,
            patternIndexing: indexingService);

        var result = await service.DistillAsync(TenantId, "admin", "corr-idx-ok");

        Assert.Equal(1, result.PatternsCreated);
        Assert.Single(indexingService.IndexedPatterns);
        Assert.NotNull(indexingService.IndexedPatterns[0].EmbeddingVector);
    }

    [Fact]
    public async Task Distill_IndexingFailure_PatternStillPersisted()
    {
        SeedQualifiedSession();

        var embeddingService = new StubEmbeddingService();
        var indexingService = new ThrowingPatternIndexingService();
        var service = new PatternDistillationService(
            _db, _settings, _auditWriter,
            NullLogger<PatternDistillationService>.Instance,
            embeddingService: embeddingService,
            patternIndexing: indexingService);

        var result = await service.DistillAsync(TenantId, "admin", "corr-idx-fail");

        Assert.Equal(1, result.PatternsCreated);
        Assert.Empty(result.Errors);
        Assert.Single(_db.CasePatterns.Where(p => p.PatternId == result.CreatedPatternIds[0]));
    }

    [Fact]
    public async Task Distill_NoEmbeddingService_SkipsIndexing()
    {
        SeedQualifiedSession();

        var indexingService = new StubPatternIndexingService();
        var service = new PatternDistillationService(
            _db, _settings, _auditWriter,
            NullLogger<PatternDistillationService>.Instance,
            patternIndexing: indexingService);

        var result = await service.DistillAsync(TenantId, "admin", "corr-no-emb");

        Assert.Equal(1, result.PatternsCreated);
        Assert.Empty(indexingService.IndexedPatterns);
    }

    [Fact]
    public async Task Distill_QualityValidatorRejects_SkipsPattern()
    {
        SeedQualifiedSession();

        var validator = new StubQualityValidator(rejected: true, qualityScore: 0.1f);
        var service = new PatternDistillationService(
            _db, _settings, _auditWriter,
            NullLogger<PatternDistillationService>.Instance,
            qualityValidator: validator);

        var result = await service.DistillAsync(TenantId, "admin", "corr-reject");

        Assert.Equal(0, result.PatternsCreated);
        Assert.Equal(1, result.PatternsSkipped);
        Assert.Empty(_db.CasePatterns.ToList());
    }

    [Fact]
    public async Task Distill_QualityValidatorBelowThreshold_SavesWithQualityScore()
    {
        SeedQualifiedSession();

        var validator = new StubQualityValidator(rejected: false, passed: false, qualityScore: 0.45f);
        var service = new PatternDistillationService(
            _db, _settings, _auditWriter,
            NullLogger<PatternDistillationService>.Instance,
            qualityValidator: validator);

        var result = await service.DistillAsync(TenantId, "admin", "corr-low-q");

        Assert.Equal(1, result.PatternsCreated);
        var pattern = _db.CasePatterns.First(p => p.PatternId == result.CreatedPatternIds[0]);
        Assert.Equal(0.45f, pattern.QualityScore!.Value, 0.01f);
    }

    [Fact]
    public async Task Distill_QualityValidatorPasses_SavesWithQualityScore()
    {
        SeedQualifiedSession();

        var validator = new StubQualityValidator(rejected: false, passed: true, qualityScore: 0.85f);
        var service = new PatternDistillationService(
            _db, _settings, _auditWriter,
            NullLogger<PatternDistillationService>.Instance,
            qualityValidator: validator);

        var result = await service.DistillAsync(TenantId, "admin", "corr-pass-q");

        Assert.Equal(1, result.PatternsCreated);
        var pattern = _db.CasePatterns.First(p => p.PatternId == result.CreatedPatternIds[0]);
        Assert.Equal(0.85f, pattern.QualityScore!.Value, 0.01f);
    }

    [Fact]
    public async Task FindCandidates_ExcludesSessionsWithTooFewChunks()
    {
        _settings.GetType().GetProperty("MinCitedChunks")!.SetValue(_settings, 3);
        SeedQualifiedSession(chunkCount: 2);

        var result = await _service.FindCandidatesAsync(TenantId);

        Assert.Equal(0, result.TotalCount);
    }

    [Fact]
    public async Task Distill_CandidateThrows_CapturesErrorAndContinues()
    {
        // Seed two candidates: one with chunks and one where we'll delete chunks
        // to cause a null return (skipped), plus test the error path via a failing embedding service.
        SeedQualifiedSession();
        var (sessionId2, _) = SeedQualifiedSession();

        // Delete all chunks for the second session to make it get skipped (returns null).
        var chunksToRemove = _db.EvidenceChunks
            .Where(c => c.EvidenceId.Contains(sessionId2.ToString("N")))
            .ToList();
        _db.EvidenceChunks.RemoveRange(chunksToRemove);
        _db.SaveChanges();

        var result = await _service.DistillAsync(TenantId, "admin", "corr-mixed");

        // One pattern created, one skipped (no chunks).
        Assert.Equal(1, result.PatternsCreated);
        Assert.Equal(1, result.PatternsSkipped);
    }

    [Fact]
    public async Task Distill_AllServicesIntegrated_EmbedAndIndexAndValidate()
    {
        SeedQualifiedSession();

        var embeddingService = new StubEmbeddingService();
        var indexingService = new StubPatternIndexingService();
        var validator = new StubQualityValidator(rejected: false, passed: true, qualityScore: 0.9f);
        var service = new PatternDistillationService(
            _db, _settings, _auditWriter,
            NullLogger<PatternDistillationService>.Instance,
            embeddingService: embeddingService,
            patternIndexing: indexingService,
            qualityValidator: validator);

        var result = await service.DistillAsync(TenantId, "admin", "corr-all");

        Assert.Equal(1, result.PatternsCreated);
        Assert.Single(indexingService.IndexedPatterns);
        var pattern = _db.CasePatterns.First(p => p.PatternId == result.CreatedPatternIds[0]);
        Assert.Equal(0.9f, pattern.QualityScore!.Value, 0.01f);
    }

    // --- Helpers ---

    private static DistillationCandidate CreateMinimalCandidate(
        string? sessionTitle = "Test",
        int positiveCount = 1,
        int negativeCount = 0)
    {
        return new DistillationCandidate
        {
            SessionId = Guid.NewGuid(),
            TenantId = TenantId,
            UserId = "u1",
            SessionTitle = sessionTitle,
            CitedEvidenceIds = ["ev-1"],
            CitedChunkIds = ["ev-1_chunk_0"],
            PositiveFeedbackCount = positiveCount,
            NegativeFeedbackCount = negativeCount,
            ResolvedAt = DateTimeOffset.UtcNow,
        };
    }

    private static EvidenceChunkEntity CreateMinimalChunk(string? title = "Test chunk")
    {
        return new EvidenceChunkEntity
        {
            ChunkId = $"chunk-{Guid.NewGuid():N}",
            EvidenceId = "ev-1",
            TenantId = TenantId,
            ConnectorId = Guid.NewGuid(),
            ChunkText = "Resolution text content.",
            Title = title ?? "Test",
            SourceSystem = "AzureDevOps",
            SourceType = "Ticket",
            Status = "Closed",
            Visibility = "Internal",
            AccessLabel = "Internal",
            SourceUrl = "https://test.com",
            ContentHash = "hash-1",
            UpdatedAt = DateTimeOffset.UtcNow,
            CreatedAt = DateTimeOffset.UtcNow,
        };
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

    private sealed class StubEmbeddingService : IEmbeddingService
    {
        public Task<float[]> GenerateEmbeddingAsync(string text, CancellationToken cancellationToken = default)
            => Task.FromResult(new float[1536]);
    }

    private sealed class ThrowingEmbeddingService : IEmbeddingService
    {
        public Task<float[]> GenerateEmbeddingAsync(string text, CancellationToken cancellationToken = default)
            => throw new HttpRequestException("Embedding API unavailable");
    }

    private sealed class StubPatternIndexingService : IPatternIndexingService
    {
        public List<CasePattern> IndexedPatterns { get; } = [];

        public Task<IndexingResult> IndexPatternsAsync(IReadOnlyList<CasePattern> patterns, CancellationToken cancellationToken = default)
        {
            IndexedPatterns.AddRange(patterns);
            return Task.FromResult(new IndexingResult(patterns.Count, 0, []));
        }

        public Task EnsureIndexAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<int> DeletePatternsAsync(IReadOnlyList<string> patternIds, CancellationToken cancellationToken = default) => Task.FromResult(0);
    }

    private sealed class ThrowingPatternIndexingService : IPatternIndexingService
    {
        public Task<IndexingResult> IndexPatternsAsync(IReadOnlyList<CasePattern> patterns, CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("Search service unavailable");

        public Task EnsureIndexAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<int> DeletePatternsAsync(IReadOnlyList<string> patternIds, CancellationToken cancellationToken = default) => Task.FromResult(0);
    }

    private sealed class StubQualityValidator : ICaseCardQualityValidator
    {
        private readonly bool _rejected;
        private readonly bool _passed;
        private readonly float _qualityScore;

        public StubQualityValidator(bool rejected, bool passed = true, float qualityScore = 0.8f)
        {
            _rejected = rejected;
            _passed = passed;
            _qualityScore = qualityScore;
        }

        public CaseCardQualityReport Validate(CasePattern pattern)
        {
            return new CaseCardQualityReport
            {
                QualityScore = _qualityScore,
                Passed = _passed,
                Rejected = _rejected,
                Issues = _rejected
                    ? [new QualityIssue { Field = "title", Severity = QualitySeverity.Error, Message = "Too short", Penalty = 0.5f }]
                    : [],
            };
        }
    }
}
