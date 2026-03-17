using Microsoft.Extensions.Logging.Abstractions;
using SmartKb.Contracts;
using SmartKb.Contracts.Configuration;
using SmartKb.Contracts.Enums;
using SmartKb.Contracts.Models;
using SmartKb.Contracts.Services;
using SmartKb.Data.Entities;
using SmartKb.Data.Repositories;

namespace SmartKb.Data.Tests;

public class DistillationQualityGateTests : IDisposable
{
    private readonly SmartKbDbContext _db;
    private readonly StubAuditWriter _auditWriter;
    private readonly DistillationSettings _distillSettings;
    private readonly CaseCardQualitySettings _qualitySettings;

    private const string TenantId = "t1";

    public DistillationQualityGateTests()
    {
        _db = TestDbContextFactory.Create();
        _auditWriter = new StubAuditWriter();
        _distillSettings = new DistillationSettings();
        _qualitySettings = new CaseCardQualitySettings();
        SeedData();
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
        _db.Connectors.Add(new ConnectorEntity
        {
            Id = Guid.NewGuid(),
            TenantId = TenantId,
            Name = "TestConnector",
            ConnectorType = ConnectorType.AzureDevOps,
            Status = ConnectorStatus.Enabled,
            AuthType = SecretAuthType.Pat,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        });
        _db.SaveChanges();
    }

    private PatternDistillationService CreateService(CaseCardQualitySettings? qualityOverride = null)
    {
        var qualityValidator = new CaseCardQualityValidator(qualityOverride ?? _qualitySettings);
        return new PatternDistillationService(
            _db, _distillSettings, _auditWriter,
            NullLogger<PatternDistillationService>.Instance,
            qualityValidator: qualityValidator);
    }

    private Guid SeedQualifiedSession(
        string chunkText = "This is a detailed resolution step describing how to fix the authentication token cache issue.",
        string? chunkContext = "Step 1: Clear the token cache on the auth service host",
        string sessionTitle = "Auth token validation failure on SSO login",
        int chunkCount = 2)
    {
        var session = new SessionEntity
        {
            Id = Guid.NewGuid(),
            TenantId = TenantId,
            UserId = "u1",
            Title = sessionTitle,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        };
        _db.Sessions.Add(session);

        _db.OutcomeEvents.Add(new OutcomeEventEntity
        {
            Id = Guid.NewGuid(),
            SessionId = session.Id,
            TenantId = TenantId,
            ResolutionType = ResolutionType.ResolvedWithoutEscalation,
            CreatedAt = DateTimeOffset.UtcNow,
        });

        var message = new MessageEntity
        {
            Id = Guid.NewGuid(),
            SessionId = session.Id,
            TenantId = TenantId,
            Role = MessageRole.Assistant,
            Content = "Response",
            CorrelationId = $"corr-{session.Id:N}",
            CreatedAt = DateTimeOffset.UtcNow,
        };
        _db.Messages.Add(message);

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
                ChunkIndex = i,
                ChunkText = chunkText,
                ChunkContext = chunkContext,
                SourceSystem = "AzureDevOps",
                SourceType = "Ticket",
                Status = "Closed",
                UpdatedAt = DateTimeOffset.UtcNow,
                Visibility = "Internal",
                AccessLabel = "Internal",
                Title = $"Work item {i}",
                SourceUrl = $"https://dev.azure.com/test/{i}",
                ErrorTokens = "[\"NullReferenceException\"]",
                ContentHash = $"hash-{session.Id:N}-{i}",
                CreatedAt = DateTimeOffset.UtcNow,
            });
        }

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

        _db.Feedbacks.Add(new FeedbackEntity
        {
            Id = Guid.NewGuid(),
            MessageId = message.Id,
            SessionId = session.Id,
            TenantId = TenantId,
            UserId = "u1",
            Type = FeedbackType.ThumbsUp,
            CreatedAt = DateTimeOffset.UtcNow,
        });

        _db.SaveChanges();
        return session.Id;
    }

    [Fact]
    public async Task Distill_WithQualityGate_SavesQualityScore()
    {
        SeedQualifiedSession();
        var service = CreateService();

        var result = await service.DistillAsync(TenantId, "admin", "corr-1");

        Assert.Equal(1, result.PatternsCreated);
        var pattern = _db.CasePatterns.First(p => p.PatternId == result.CreatedPatternIds[0]);
        Assert.NotNull(pattern.QualityScore);
        Assert.True(pattern.QualityScore > 0f);
    }

    [Fact]
    public async Task Distill_WithQualityGate_RejectsVeryLowQuality()
    {
        // Seed with minimal content that will produce a low-quality pattern
        SeedQualifiedSession(
            chunkText: "x", // very short resolution step
            chunkContext: null,
            sessionTitle: "",
            chunkCount: 1);

        // Very strict reject threshold
        var strictSettings = new CaseCardQualitySettings { RejectThreshold = 0.8f };
        var service = CreateService(strictSettings);

        var result = await service.DistillAsync(TenantId, "admin", "corr-2");

        // Pattern should be rejected (skipped), not saved
        Assert.Equal(0, result.PatternsCreated);
        Assert.Equal(1, result.PatternsSkipped);
        Assert.Empty(_db.CasePatterns.ToList());
    }

    [Fact]
    public async Task Distill_WithoutQualityGate_StillWorks()
    {
        SeedQualifiedSession();

        // Create service without quality validator (null — backwards-compatible)
        var service = new PatternDistillationService(
            _db, _distillSettings, _auditWriter,
            NullLogger<PatternDistillationService>.Instance);

        var result = await service.DistillAsync(TenantId, "admin", "corr-3");

        Assert.Equal(1, result.PatternsCreated);
        var pattern = _db.CasePatterns.First();
        Assert.Null(pattern.QualityScore); // no validator → null
    }

    [Fact]
    public async Task Distill_LowQualityButAboveReject_SavesAsDraft()
    {
        // Content that will produce a low-but-not-rejected pattern
        SeedQualifiedSession(
            chunkText: "x",
            chunkContext: null,
            sessionTitle: "Some title with context",
            chunkCount: 1);

        // Low reject threshold, but strict pass threshold
        var settings = new CaseCardQualitySettings
        {
            RejectThreshold = 0.1f,
            MinQualityScore = 0.9f,
        };
        var service = CreateService(settings);

        var result = await service.DistillAsync(TenantId, "admin", "corr-4");

        Assert.Equal(1, result.PatternsCreated);
        var pattern = _db.CasePatterns.First();
        Assert.NotNull(pattern.QualityScore);
        Assert.Equal("Draft", pattern.TrustLevel);
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
