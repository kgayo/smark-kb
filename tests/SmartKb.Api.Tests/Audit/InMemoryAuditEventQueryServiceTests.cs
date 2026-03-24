using Microsoft.Extensions.Logging.Abstractions;
using SmartKb.Api.Audit;
using SmartKb.Contracts.Models;

namespace SmartKb.Api.Tests.Audit;

public class InMemoryAuditEventQueryServiceTests
{
    private readonly InMemoryAuditEventWriter _writer;
    private readonly InMemoryAuditEventQueryService _sut;

    public InMemoryAuditEventQueryServiceTests()
    {
        _writer = new InMemoryAuditEventWriter(NullLogger<InMemoryAuditEventWriter>.Instance);
        _sut = new InMemoryAuditEventQueryService(_writer);
    }

    private async Task SeedEvent(string eventId, string eventType, string tenantId,
        string actorId = "user1", string correlationId = "corr1",
        DateTimeOffset? timestamp = null, string detail = "test detail")
    {
        var evt = new AuditEvent(eventId, eventType, tenantId, actorId,
            correlationId, timestamp ?? DateTimeOffset.UtcNow, detail);
        await _writer.WriteAsync(evt);
    }

    #region QueryAsync Tests

    [Fact]
    public async Task QueryAsync_EmptyStore_ReturnsEmptyList()
    {
        var result = await _sut.QueryAsync("t1", new AuditEventQueryRequest());

        Assert.Empty(result.Events);
        Assert.Equal(0, result.TotalCount);
        Assert.Equal(1, result.Page);
        Assert.False(result.HasMore);
    }

    [Fact]
    public async Task QueryAsync_TenantIsolation_ExcludesOtherTenants()
    {
        await SeedEvent("e1", "test.event", "tenant-a");
        await SeedEvent("e2", "test.event", "tenant-b");
        await SeedEvent("e3", "other.event", "tenant-a");

        var result = await _sut.QueryAsync("tenant-a", new AuditEventQueryRequest());

        Assert.Equal(2, result.TotalCount);
        Assert.All(result.Events, e => Assert.Equal("tenant-a", e.TenantId));
    }

    [Fact]
    public async Task QueryAsync_FilterByEventType()
    {
        await SeedEvent("e1", "chat.query", "t1");
        await SeedEvent("e2", "admin.update", "t1");
        await SeedEvent("e3", "chat.query", "t1");

        var result = await _sut.QueryAsync("t1", new AuditEventQueryRequest { EventType = "chat.query" });

        Assert.Equal(2, result.TotalCount);
        Assert.All(result.Events, e => Assert.Equal("chat.query", e.EventType));
    }

    [Fact]
    public async Task QueryAsync_FilterByActorId()
    {
        await SeedEvent("e1", "test", "t1", actorId: "alice");
        await SeedEvent("e2", "test", "t1", actorId: "bob");

        var result = await _sut.QueryAsync("t1", new AuditEventQueryRequest { ActorId = "alice" });

        Assert.Single(result.Events);
        Assert.Equal("alice", result.Events[0].ActorId);
    }

    [Fact]
    public async Task QueryAsync_FilterByCorrelationId()
    {
        await SeedEvent("e1", "test", "t1", correlationId: "corr-abc");
        await SeedEvent("e2", "test", "t1", correlationId: "corr-xyz");

        var result = await _sut.QueryAsync("t1", new AuditEventQueryRequest { CorrelationId = "corr-abc" });

        Assert.Single(result.Events);
        Assert.Equal("corr-abc", result.Events[0].CorrelationId);
    }

    [Fact]
    public async Task QueryAsync_FilterByDateRange()
    {
        var t1 = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var t2 = new DateTimeOffset(2026, 2, 1, 0, 0, 0, TimeSpan.Zero);
        var t3 = new DateTimeOffset(2026, 3, 1, 0, 0, 0, TimeSpan.Zero);

        await SeedEvent("e1", "test", "t1", timestamp: t1);
        await SeedEvent("e2", "test", "t1", timestamp: t2);
        await SeedEvent("e3", "test", "t1", timestamp: t3);

        var result = await _sut.QueryAsync("t1", new AuditEventQueryRequest
        {
            From = new DateTimeOffset(2026, 1, 15, 0, 0, 0, TimeSpan.Zero),
            To = new DateTimeOffset(2026, 2, 15, 0, 0, 0, TimeSpan.Zero),
        });

        Assert.Single(result.Events);
        Assert.Equal("e2", result.Events[0].EventId);
    }

    [Fact]
    public async Task QueryAsync_Pagination_FirstPage()
    {
        for (var i = 0; i < 5; i++)
            await SeedEvent($"e{i}", "test", "t1",
                timestamp: DateTimeOffset.UtcNow.AddMinutes(-i));

        var result = await _sut.QueryAsync("t1", new AuditEventQueryRequest { PageSize = 2, Page = 1 });

        Assert.Equal(2, result.Events.Count);
        Assert.Equal(5, result.TotalCount);
        Assert.Equal(1, result.Page);
        Assert.Equal(2, result.PageSize);
        Assert.True(result.HasMore);
    }

    [Fact]
    public async Task QueryAsync_Pagination_LastPage()
    {
        for (var i = 0; i < 5; i++)
            await SeedEvent($"e{i}", "test", "t1",
                timestamp: DateTimeOffset.UtcNow.AddMinutes(-i));

        var result = await _sut.QueryAsync("t1", new AuditEventQueryRequest { PageSize = 2, Page = 3 });

        Assert.Single(result.Events);
        Assert.Equal(5, result.TotalCount);
        Assert.Equal(3, result.Page);
        Assert.False(result.HasMore);
    }

    [Fact]
    public async Task QueryAsync_PageSizeClamped_ToMax200()
    {
        await SeedEvent("e1", "test", "t1");

        var result = await _sut.QueryAsync("t1", new AuditEventQueryRequest { PageSize = 500 });

        Assert.Equal(200, result.PageSize);
    }

    [Fact]
    public async Task QueryAsync_PageSizeClamped_MinOne()
    {
        await SeedEvent("e1", "test", "t1");

        var result = await _sut.QueryAsync("t1", new AuditEventQueryRequest { PageSize = 0 });

        Assert.Equal(1, result.PageSize);
    }

    [Fact]
    public async Task QueryAsync_OrderedByTimestampDescending()
    {
        var t1 = DateTimeOffset.UtcNow.AddMinutes(-10);
        var t2 = DateTimeOffset.UtcNow.AddMinutes(-5);
        var t3 = DateTimeOffset.UtcNow;

        await SeedEvent("e1", "test", "t1", timestamp: t1);
        await SeedEvent("e2", "test", "t1", timestamp: t3);
        await SeedEvent("e3", "test", "t1", timestamp: t2);

        var result = await _sut.QueryAsync("t1", new AuditEventQueryRequest());

        Assert.Equal("e2", result.Events[0].EventId);
        Assert.Equal("e3", result.Events[1].EventId);
        Assert.Equal("e1", result.Events[2].EventId);
    }

    [Fact]
    public async Task QueryAsync_CombinedFilters()
    {
        var t1 = new DateTimeOffset(2026, 3, 1, 0, 0, 0, TimeSpan.Zero);
        var t2 = new DateTimeOffset(2026, 3, 15, 0, 0, 0, TimeSpan.Zero);

        await SeedEvent("e1", "chat.query", "t1", actorId: "alice", timestamp: t1);
        await SeedEvent("e2", "chat.query", "t1", actorId: "bob", timestamp: t2);
        await SeedEvent("e3", "admin.update", "t1", actorId: "alice", timestamp: t2);
        await SeedEvent("e4", "chat.query", "t2", actorId: "alice", timestamp: t2);

        var result = await _sut.QueryAsync("t1", new AuditEventQueryRequest
        {
            EventType = "chat.query",
            ActorId = "alice",
        });

        Assert.Single(result.Events);
        Assert.Equal("e1", result.Events[0].EventId);
    }

    [Fact]
    public async Task QueryAsync_MapsAllFieldsCorrectly()
    {
        var ts = new DateTimeOffset(2026, 3, 24, 12, 0, 0, TimeSpan.Zero);
        await SeedEvent("evt-1", "chat.query", "tenant-1",
            actorId: "user-42", correlationId: "corr-99", timestamp: ts, detail: "queried SSO issue");

        var result = await _sut.QueryAsync("tenant-1", new AuditEventQueryRequest());

        Assert.Single(result.Events);
        var e = result.Events[0];
        Assert.Equal("evt-1", e.EventId);
        Assert.Equal("chat.query", e.EventType);
        Assert.Equal("tenant-1", e.TenantId);
        Assert.Equal("user-42", e.ActorId);
        Assert.Equal("corr-99", e.CorrelationId);
        Assert.Equal(ts, e.Timestamp);
        Assert.Equal("queried SSO issue", e.Detail);
    }

    #endregion

    #region ExportAsync Tests

    [Fact]
    public async Task ExportAsync_EmptyStore_ReturnsNothing()
    {
        var results = new List<AuditEventResponse>();
        await foreach (var e in _sut.ExportAsync("t1", new AuditExportCursor()))
            results.Add(e);

        Assert.Empty(results);
    }

    [Fact]
    public async Task ExportAsync_TenantIsolation()
    {
        await SeedEvent("e1", "test", "tenant-a");
        await SeedEvent("e2", "test", "tenant-b");

        var results = new List<AuditEventResponse>();
        await foreach (var e in _sut.ExportAsync("tenant-a", new AuditExportCursor()))
            results.Add(e);

        Assert.Single(results);
        Assert.Equal("tenant-a", results[0].TenantId);
    }

    [Fact]
    public async Task ExportAsync_FilterByEventType()
    {
        await SeedEvent("e1", "chat.query", "t1");
        await SeedEvent("e2", "admin.update", "t1");

        var results = new List<AuditEventResponse>();
        await foreach (var e in _sut.ExportAsync("t1", new AuditExportCursor { EventType = "admin.update" }))
            results.Add(e);

        Assert.Single(results);
        Assert.Equal("admin.update", results[0].EventType);
    }

    [Fact]
    public async Task ExportAsync_CursorAfterTimestamp_ExcludesNewerOrEqual()
    {
        var t1 = DateTimeOffset.UtcNow.AddMinutes(-30);
        var t2 = DateTimeOffset.UtcNow.AddMinutes(-20);
        var t3 = DateTimeOffset.UtcNow.AddMinutes(-10);

        await SeedEvent("e1", "test", "t1", timestamp: t1);
        await SeedEvent("e2", "test", "t1", timestamp: t2);
        await SeedEvent("e3", "test", "t1", timestamp: t3);

        var results = new List<AuditEventResponse>();
        await foreach (var e in _sut.ExportAsync("t1", new AuditExportCursor { AfterTimestamp = t2 }))
            results.Add(e);

        // Should only return events with timestamp < t2 (i.e., e1)
        Assert.Single(results);
        Assert.Equal("e1", results[0].EventId);
    }

    [Fact]
    public async Task ExportAsync_LimitClamped_RespectsMax()
    {
        for (var i = 0; i < 10; i++)
            await SeedEvent($"e{i}", "test", "t1",
                timestamp: DateTimeOffset.UtcNow.AddMinutes(-i));

        var results = new List<AuditEventResponse>();
        await foreach (var e in _sut.ExportAsync("t1", new AuditExportCursor { Limit = 3 }))
            results.Add(e);

        Assert.Equal(3, results.Count);
    }

    [Fact]
    public async Task ExportAsync_LimitClamped_MinOne()
    {
        await SeedEvent("e1", "test", "t1");
        await SeedEvent("e2", "test", "t1");

        var results = new List<AuditEventResponse>();
        await foreach (var e in _sut.ExportAsync("t1", new AuditExportCursor { Limit = 0 }))
            results.Add(e);

        Assert.Single(results);
    }

    [Fact]
    public async Task ExportAsync_FilterByActorId()
    {
        await SeedEvent("e1", "test", "t1", actorId: "alice");
        await SeedEvent("e2", "test", "t1", actorId: "bob");

        var results = new List<AuditEventResponse>();
        await foreach (var e in _sut.ExportAsync("t1", new AuditExportCursor { ActorId = "alice" }))
            results.Add(e);

        Assert.Single(results);
        Assert.Equal("alice", results[0].ActorId);
    }

    [Fact]
    public async Task ExportAsync_FilterByDateRange()
    {
        var t1 = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var t2 = new DateTimeOffset(2026, 2, 1, 0, 0, 0, TimeSpan.Zero);
        var t3 = new DateTimeOffset(2026, 3, 1, 0, 0, 0, TimeSpan.Zero);

        await SeedEvent("e1", "test", "t1", timestamp: t1);
        await SeedEvent("e2", "test", "t1", timestamp: t2);
        await SeedEvent("e3", "test", "t1", timestamp: t3);

        var results = new List<AuditEventResponse>();
        await foreach (var e in _sut.ExportAsync("t1", new AuditExportCursor
        {
            From = new DateTimeOffset(2026, 1, 15, 0, 0, 0, TimeSpan.Zero),
            To = new DateTimeOffset(2026, 2, 15, 0, 0, 0, TimeSpan.Zero),
        }))
            results.Add(e);

        Assert.Single(results);
        Assert.Equal("e2", results[0].EventId);
    }

    #endregion
}
