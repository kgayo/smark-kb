using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using SmartKb.Contracts.Models;
using SmartKb.Data.Repositories;

namespace SmartKb.Data.Tests;

public class SqlAuditEventWriterTests : IDisposable
{
    private readonly SmartKbDbContext _db = TestDbContextFactory.Create();

    public void Dispose() => _db.Dispose();

    [Fact]
    public async Task WriteAsync_PersistsAuditEvent()
    {
        var writer = new SqlAuditEventWriter(_db, NullLogger<SqlAuditEventWriter>.Instance);
        var auditEvent = new AuditEvent(
            EventId: Guid.NewGuid().ToString(),
            EventType: "test.event",
            TenantId: "tenant-1",
            ActorId: "actor-1",
            CorrelationId: "corr-1",
            Timestamp: DateTimeOffset.UtcNow,
            Detail: "Test audit event");

        await writer.WriteAsync(auditEvent);

        var stored = await _db.AuditEvents.FirstOrDefaultAsync(a => a.TenantId == "tenant-1");
        Assert.NotNull(stored);
        Assert.Equal("test.event", stored!.EventType);
        Assert.Equal("actor-1", stored.ActorId);
        Assert.Equal("Test audit event", stored.Detail);
    }

    [Fact]
    public async Task WriteAsync_MultipleEvents_AllPersisted()
    {
        var writer = new SqlAuditEventWriter(_db, NullLogger<SqlAuditEventWriter>.Instance);

        for (int i = 0; i < 5; i++)
        {
            await writer.WriteAsync(new AuditEvent(
                EventId: Guid.NewGuid().ToString(),
                EventType: $"event.type.{i}",
                TenantId: "tenant-multi",
                ActorId: "actor-1",
                CorrelationId: $"corr-{i}",
                Timestamp: DateTimeOffset.UtcNow,
                Detail: $"Detail {i}"));
        }

        var count = await _db.AuditEvents.CountAsync(a => a.TenantId == "tenant-multi");
        Assert.Equal(5, count);
    }

    [Fact]
    public async Task WriteAsync_ParsesGuidEventId()
    {
        var writer = new SqlAuditEventWriter(_db, NullLogger<SqlAuditEventWriter>.Instance);
        var knownId = Guid.NewGuid();

        await writer.WriteAsync(new AuditEvent(
            EventId: knownId.ToString(),
            EventType: "guid.test",
            TenantId: "t-guid",
            ActorId: "a1",
            CorrelationId: "c1",
            Timestamp: DateTimeOffset.UtcNow,
            Detail: "detail"));

        var stored = await _db.AuditEvents.FindAsync(knownId);
        Assert.NotNull(stored);
    }

    [Fact]
    public async Task WriteAsync_NonGuidEventId_GeneratesNewGuid()
    {
        var writer = new SqlAuditEventWriter(_db, NullLogger<SqlAuditEventWriter>.Instance);

        await writer.WriteAsync(new AuditEvent(
            EventId: "not-a-guid",
            EventType: "nonguid.test",
            TenantId: "t-nonguid",
            ActorId: "a1",
            CorrelationId: "c1",
            Timestamp: DateTimeOffset.UtcNow,
            Detail: "detail"));

        var stored = await _db.AuditEvents.FirstOrDefaultAsync(a => a.TenantId == "t-nonguid");
        Assert.NotNull(stored);
        Assert.NotEqual(Guid.Empty, stored!.Id);
    }
}
