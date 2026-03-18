using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using SmartKb.Data.Repositories;

namespace SmartKb.Data.Tests;

public class SqlAnswerTraceWriterTests : IDisposable
{
    private readonly SmartKbDbContext _db = TestDbContextFactory.Create();

    public void Dispose() => _db.Dispose();

    [Fact]
    public async Task WriteTraceAsync_PersistsAllFields()
    {
        var writer = new SqlAnswerTraceWriter(_db, NullLogger<SqlAnswerTraceWriter>.Instance);
        var traceId = Guid.NewGuid();

        await writer.WriteTraceAsync(
            traceId: traceId,
            tenantId: "tenant-1",
            userId: "user-1",
            correlationId: "corr-1",
            query: "How do I reset my password?",
            responseType: "final_answer",
            confidence: 0.85f,
            confidenceLabel: "High",
            citedChunkIds: ["chunk-a", "chunk-b"],
            retrievedChunkIds: ["chunk-a", "chunk-b", "chunk-c"],
            aclFilteredOutCount: 1,
            hasEvidence: true,
            escalationRecommended: false,
            systemPromptVersion: "v1.0",
            durationMs: 1200);

        var stored = await _db.AnswerTraces.FindAsync(traceId);
        Assert.NotNull(stored);
        Assert.Equal("tenant-1", stored!.TenantId);
        Assert.Equal("user-1", stored.UserId);
        Assert.Equal("corr-1", stored.CorrelationId);
        Assert.Equal("How do I reset my password?", stored.Query);
        Assert.Equal("final_answer", stored.ResponseType);
        Assert.Equal(0.85f, stored.Confidence, 0.001f);
        Assert.Equal("High", stored.ConfidenceLabel);
        Assert.Equal(3, stored.RetrievedChunkCount);
        Assert.Equal(1, stored.AclFilteredOutCount);
        Assert.True(stored.HasEvidence);
        Assert.False(stored.EscalationRecommended);
        Assert.Equal("v1.0", stored.SystemPromptVersion);
        Assert.Equal(1200, stored.DurationMs);

        var citedIds = JsonSerializer.Deserialize<List<string>>(stored.CitedChunkIds);
        Assert.NotNull(citedIds);
        Assert.Equal(2, citedIds!.Count);
        Assert.Contains("chunk-a", citedIds);

        var retrievedIds = JsonSerializer.Deserialize<List<string>>(stored.RetrievedChunkIds);
        Assert.NotNull(retrievedIds);
        Assert.Equal(3, retrievedIds!.Count);
    }

    [Fact]
    public async Task WriteTraceAsync_MultipleTraces_AllPersisted()
    {
        var writer = new SqlAnswerTraceWriter(_db, NullLogger<SqlAnswerTraceWriter>.Instance);

        for (int i = 0; i < 5; i++)
        {
            await writer.WriteTraceAsync(
                traceId: Guid.NewGuid(),
                tenantId: "tenant-multi",
                userId: $"user-{i}",
                correlationId: $"corr-{i}",
                query: $"Query {i}",
                responseType: "final_answer",
                confidence: 0.5f + i * 0.1f,
                confidenceLabel: "Medium",
                citedChunkIds: [$"chunk-{i}"],
                retrievedChunkIds: [$"chunk-{i}"],
                aclFilteredOutCount: 0,
                hasEvidence: true,
                escalationRecommended: false,
                systemPromptVersion: "v1.0",
                durationMs: 500 + i * 100);
        }

        var count = await _db.AnswerTraces.CountAsync(t => t.TenantId == "tenant-multi");
        Assert.Equal(5, count);
    }

    [Fact]
    public async Task WriteTraceAsync_EscalationRecommended_Persisted()
    {
        var writer = new SqlAnswerTraceWriter(_db, NullLogger<SqlAnswerTraceWriter>.Instance);
        var traceId = Guid.NewGuid();

        await writer.WriteTraceAsync(
            traceId: traceId,
            tenantId: "tenant-esc",
            userId: "user-1",
            correlationId: "corr-esc",
            query: "Critical production failure",
            responseType: "escalate",
            confidence: 0.15f,
            confidenceLabel: "Low",
            citedChunkIds: [],
            retrievedChunkIds: [],
            aclFilteredOutCount: 0,
            hasEvidence: false,
            escalationRecommended: true,
            systemPromptVersion: "v1.0",
            durationMs: 800);

        var stored = await _db.AnswerTraces.FindAsync(traceId);
        Assert.NotNull(stored);
        Assert.True(stored!.EscalationRecommended);
        Assert.False(stored.HasEvidence);
        Assert.Equal("escalate", stored.ResponseType);
        Assert.Equal(0, stored.RetrievedChunkCount);
    }

    [Fact]
    public async Task WriteTraceAsync_EmptyChunkLists_PersistsAsEmptyJsonArrays()
    {
        var writer = new SqlAnswerTraceWriter(_db, NullLogger<SqlAnswerTraceWriter>.Instance);
        var traceId = Guid.NewGuid();

        await writer.WriteTraceAsync(
            traceId: traceId,
            tenantId: "tenant-empty",
            userId: "user-1",
            correlationId: "corr-empty",
            query: "No results query",
            responseType: "next_steps_only",
            confidence: 0.1f,
            confidenceLabel: "Low",
            citedChunkIds: [],
            retrievedChunkIds: [],
            aclFilteredOutCount: 0,
            hasEvidence: false,
            escalationRecommended: false,
            systemPromptVersion: "v1.0",
            durationMs: 300);

        var stored = await _db.AnswerTraces.FindAsync(traceId);
        Assert.NotNull(stored);
        Assert.Equal("[]", stored!.CitedChunkIds);
        Assert.Equal("[]", stored.RetrievedChunkIds);
        Assert.Equal(0, stored.RetrievedChunkCount);
    }

    [Fact]
    public async Task WriteTraceAsync_SetsCreatedAtTimestamp()
    {
        var writer = new SqlAnswerTraceWriter(_db, NullLogger<SqlAnswerTraceWriter>.Instance);
        var traceId = Guid.NewGuid();
        var before = DateTimeOffset.UtcNow;

        await writer.WriteTraceAsync(
            traceId: traceId,
            tenantId: "tenant-ts",
            userId: "user-1",
            correlationId: "corr-ts",
            query: "test",
            responseType: "final_answer",
            confidence: 0.5f,
            confidenceLabel: "Medium",
            citedChunkIds: [],
            retrievedChunkIds: [],
            aclFilteredOutCount: 0,
            hasEvidence: true,
            escalationRecommended: false,
            systemPromptVersion: "v1.0",
            durationMs: 100);

        var after = DateTimeOffset.UtcNow;
        var stored = await _db.AnswerTraces.FindAsync(traceId);
        Assert.NotNull(stored);
        Assert.InRange(stored!.CreatedAt, before, after);
    }
}
