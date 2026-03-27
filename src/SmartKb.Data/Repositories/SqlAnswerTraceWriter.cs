using System.Text.Json;
using Microsoft.Extensions.Logging;
using SmartKb.Contracts.Services;
using SmartKb.Data.Entities;

namespace SmartKb.Data.Repositories;

public sealed class SqlAnswerTraceWriter : IAnswerTraceWriter
{
    private readonly SmartKbDbContext _db;
    private readonly ILogger<SqlAnswerTraceWriter> _logger;

    public SqlAnswerTraceWriter(SmartKbDbContext db, ILogger<SqlAnswerTraceWriter> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task WriteTraceAsync(
        Guid traceId,
        string tenantId,
        string userId,
        string correlationId,
        string query,
        string responseType,
        float confidence,
        string confidenceLabel,
        IReadOnlyList<string> citedChunkIds,
        IReadOnlyList<string> retrievedChunkIds,
        int aclFilteredOutCount,
        bool hasEvidence,
        bool escalationRecommended,
        string systemPromptVersion,
        long durationMs,
        CancellationToken cancellationToken = default)
    {
        var entity = new AnswerTraceEntity
        {
            Id = traceId,
            TenantId = tenantId,
            UserId = userId,
            CorrelationId = correlationId,
            Query = query,
            ResponseType = responseType,
            Confidence = confidence,
            ConfidenceLabel = confidenceLabel,
            CitedChunkIds = JsonSerializer.Serialize(citedChunkIds),
            RetrievedChunkIds = JsonSerializer.Serialize(retrievedChunkIds),
            RetrievedChunkCount = retrievedChunkIds.Count,
            AclFilteredOutCount = aclFilteredOutCount,
            HasEvidence = hasEvidence,
            EscalationRecommended = escalationRecommended,
            SystemPromptVersion = systemPromptVersion,
            DurationMs = durationMs,
            CreatedAt = DateTimeOffset.UtcNow,
            CreatedAtEpoch = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
        };

        _db.AnswerTraces.Add(entity);
        await _db.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Answer trace persisted: {ResponseType}, confidence={Confidence:F3}, tenant={TenantId}, traceId={TraceId}",
            responseType, confidence, tenantId, correlationId);
    }
}
