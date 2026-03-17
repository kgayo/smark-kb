using Microsoft.Extensions.Logging.Abstractions;
using SmartKb.Contracts.Models;
using SmartKb.Data.Entities;
using SmartKb.Data.Repositories;

namespace SmartKb.Data.Tests;

public class AuditEventQueryServiceTests : IDisposable
{
    private readonly SmartKbDbContext _db = TestDbContextFactory.Create();
    private readonly AuditEventQueryService _service;

    public AuditEventQueryServiceTests()
    {
        _service = new AuditEventQueryService(_db, NullLogger<AuditEventQueryService>.Instance);
        SeedEvents();
    }

    public void Dispose() => _db.Dispose();

    private void SeedEvents()
    {
        var now = DateTimeOffset.UtcNow;

        for (int i = 0; i < 10; i++)
        {
            _db.AuditEvents.Add(new AuditEventEntity
            {
                Id = Guid.NewGuid(),
                EventType = i % 2 == 0 ? "connector.created" : "chat.feedback",
                TenantId = "t1",
                ActorId = i < 5 ? "user-a" : "user-b",
                CorrelationId = $"corr-{i}",
                Timestamp = now.AddMinutes(-i),
                Detail = $"Detail {i}",
            });
        }

        // Events for a different tenant (should never appear in t1 queries)
        for (int i = 0; i < 3; i++)
        {
            _db.AuditEvents.Add(new AuditEventEntity
            {
                Id = Guid.NewGuid(),
                EventType = "connector.created",
                TenantId = "t2",
                ActorId = "other-user",
                CorrelationId = $"other-corr-{i}",
                Timestamp = now.AddMinutes(-i),
                Detail = $"Other tenant detail {i}",
            });
        }

        _db.SaveChanges();
    }

    [Fact]
    public async Task QueryAsync_ReturnsTenantScopedEvents()
    {
        var result = await _service.QueryAsync("t1", new AuditEventQueryRequest());

        Assert.Equal(10, result.TotalCount);
        Assert.All(result.Events, e => Assert.Equal("t1", e.TenantId));
    }

    [Fact]
    public async Task QueryAsync_DoesNotLeakCrossTenantEvents()
    {
        var result = await _service.QueryAsync("t1", new AuditEventQueryRequest());

        Assert.DoesNotContain(result.Events, e => e.TenantId == "t2");
    }

    [Fact]
    public async Task QueryAsync_FiltersByEventType()
    {
        var result = await _service.QueryAsync("t1", new AuditEventQueryRequest
        {
            EventType = "chat.feedback",
        });

        Assert.Equal(5, result.TotalCount);
        Assert.All(result.Events, e => Assert.Equal("chat.feedback", e.EventType));
    }

    [Fact]
    public async Task QueryAsync_FiltersByActorId()
    {
        var result = await _service.QueryAsync("t1", new AuditEventQueryRequest
        {
            ActorId = "user-a",
        });

        Assert.Equal(5, result.TotalCount);
        Assert.All(result.Events, e => Assert.Equal("user-a", e.ActorId));
    }

    [Fact]
    public async Task QueryAsync_FiltersByDateRange()
    {
        var now = DateTimeOffset.UtcNow;
        var result = await _service.QueryAsync("t1", new AuditEventQueryRequest
        {
            From = now.AddMinutes(-4),
            To = now.AddMinutes(-1),
        });

        Assert.True(result.TotalCount >= 1);
        Assert.All(result.Events, e =>
        {
            Assert.True(e.Timestamp >= now.AddMinutes(-4));
            Assert.True(e.Timestamp <= now.AddMinutes(-1));
        });
    }

    [Fact]
    public async Task QueryAsync_FiltersByCorrelationId()
    {
        var result = await _service.QueryAsync("t1", new AuditEventQueryRequest
        {
            CorrelationId = "corr-3",
        });

        Assert.Equal(1, result.TotalCount);
        Assert.Equal("corr-3", result.Events[0].CorrelationId);
    }

    [Fact]
    public async Task QueryAsync_PaginatesCorrectly()
    {
        var page1 = await _service.QueryAsync("t1", new AuditEventQueryRequest
        {
            PageSize = 3,
            Page = 1,
        });

        var page2 = await _service.QueryAsync("t1", new AuditEventQueryRequest
        {
            PageSize = 3,
            Page = 2,
        });

        Assert.Equal(3, page1.Events.Count);
        Assert.Equal(3, page2.Events.Count);
        Assert.True(page1.HasMore);
        Assert.Equal(10, page1.TotalCount);
        Assert.NotEqual(page1.Events[0].EventId, page2.Events[0].EventId);
    }

    [Fact]
    public async Task QueryAsync_OrdersByTimestampDescending()
    {
        var result = await _service.QueryAsync("t1", new AuditEventQueryRequest());

        for (int i = 1; i < result.Events.Count; i++)
        {
            Assert.True(result.Events[i - 1].Timestamp >= result.Events[i].Timestamp);
        }
    }

    [Fact]
    public async Task QueryAsync_ClampsPageSizeToMax()
    {
        var result = await _service.QueryAsync("t1", new AuditEventQueryRequest
        {
            PageSize = 999,
        });

        Assert.True(result.PageSize <= 200);
    }

    [Fact]
    public async Task QueryAsync_CombinesMultipleFilters()
    {
        var result = await _service.QueryAsync("t1", new AuditEventQueryRequest
        {
            EventType = "connector.created",
            ActorId = "user-a",
        });

        Assert.All(result.Events, e =>
        {
            Assert.Equal("connector.created", e.EventType);
            Assert.Equal("user-a", e.ActorId);
        });
    }

    [Fact]
    public async Task ExportAsync_ReturnsTenantScopedEvents()
    {
        var events = new List<AuditEventResponse>();
        await foreach (var e in _service.ExportAsync("t1", new AuditExportCursor()))
        {
            events.Add(e);
        }

        Assert.Equal(10, events.Count);
        Assert.All(events, e => Assert.Equal("t1", e.TenantId));
    }

    [Fact]
    public async Task ExportAsync_CursorPagination_ReturnsNextBatch()
    {
        var batch1 = new List<AuditEventResponse>();
        await foreach (var e in _service.ExportAsync("t1", new AuditExportCursor { Limit = 4 }))
        {
            batch1.Add(e);
        }

        Assert.Equal(4, batch1.Count);

        var lastEvent = batch1.Last();
        var batch2 = new List<AuditEventResponse>();
        await foreach (var e in _service.ExportAsync("t1", new AuditExportCursor
        {
            AfterTimestamp = lastEvent.Timestamp,
            AfterId = Guid.Parse(lastEvent.EventId),
            Limit = 4,
        }))
        {
            batch2.Add(e);
        }

        Assert.True(batch2.Count > 0);
        // No overlap between batches
        var batch1Ids = batch1.Select(e => e.EventId).ToHashSet();
        Assert.DoesNotContain(batch2, e => batch1Ids.Contains(e.EventId));
    }

    [Fact]
    public async Task ExportAsync_FiltersApply()
    {
        var events = new List<AuditEventResponse>();
        await foreach (var e in _service.ExportAsync("t1", new AuditExportCursor
        {
            EventType = "chat.feedback",
        }))
        {
            events.Add(e);
        }

        Assert.Equal(5, events.Count);
        Assert.All(events, e => Assert.Equal("chat.feedback", e.EventType));
    }

    [Fact]
    public async Task ExportAsync_RespectsLimit()
    {
        var events = new List<AuditEventResponse>();
        await foreach (var e in _service.ExportAsync("t1", new AuditExportCursor { Limit = 2 }))
        {
            events.Add(e);
        }

        Assert.Equal(2, events.Count);
    }

    [Fact]
    public async Task ExportAsync_DoesNotLeakCrossTenantEvents()
    {
        var events = new List<AuditEventResponse>();
        await foreach (var e in _service.ExportAsync("t1", new AuditExportCursor()))
        {
            events.Add(e);
        }

        Assert.DoesNotContain(events, e => e.TenantId == "t2");
    }
}
