using Microsoft.EntityFrameworkCore;
using SmartKb.Contracts.Models;
using SmartKb.Data.Entities;
using SmartKb.Data.Interceptors;

namespace SmartKb.Data.Tests;

public class AuditImmutabilityInterceptorTests : IDisposable
{
    private readonly SmartKbDbContext _db;

    public AuditImmutabilityInterceptorTests()
    {
        var interceptor = new AuditImmutabilityInterceptor();
        var options = new DbContextOptionsBuilder<SmartKbDbContext>()
            .UseSqlite("DataSource=:memory:")
            .AddInterceptors(interceptor)
            .Options;

        _db = new SmartKbDbContext(options);
        _db.Database.OpenConnection();
        _db.Database.EnsureCreated();
    }

    public void Dispose() => _db.Dispose();

    [Fact]
    public async Task Insert_AuditEvent_Succeeds()
    {
        _db.AuditEvents.Add(new AuditEventEntity
        {
            Id = Guid.NewGuid(),
            EventType = "test.event",
            TenantId = "t1",
            ActorId = "a1",
            CorrelationId = "c1",
            Timestamp = DateTimeOffset.UtcNow,
            Detail = "Test detail"
        });

        await _db.SaveChangesAsync();

        Assert.Single(await _db.AuditEvents.ToListAsync());
    }

    [Fact]
    public async Task Modify_AuditEvent_Throws()
    {
        var entity = new AuditEventEntity
        {
            Id = Guid.NewGuid(),
            EventType = "test.event",
            TenantId = "t1",
            ActorId = "a1",
            CorrelationId = "c1",
            Timestamp = DateTimeOffset.UtcNow,
            Detail = "Original detail"
        };

        _db.AuditEvents.Add(entity);
        await _db.SaveChangesAsync();

        entity.Detail = "Modified detail";

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => _db.SaveChangesAsync());

        Assert.Contains("cannot be modified", ex.Message);
        Assert.Contains(entity.Id.ToString(), ex.Message);
    }

    [Fact]
    public async Task Delete_AuditEvent_Throws()
    {
        var entity = new AuditEventEntity
        {
            Id = Guid.NewGuid(),
            EventType = "test.event",
            TenantId = "t1",
            ActorId = "a1",
            CorrelationId = "c1",
            Timestamp = DateTimeOffset.UtcNow,
            Detail = "Will try to delete"
        };

        _db.AuditEvents.Add(entity);
        await _db.SaveChangesAsync();

        _db.AuditEvents.Remove(entity);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => _db.SaveChangesAsync());

        Assert.Contains("cannot be deleted", ex.Message);
        Assert.Contains(entity.Id.ToString(), ex.Message);
    }

    [Fact]
    public void Modify_AuditEvent_Sync_Throws()
    {
        var entity = new AuditEventEntity
        {
            Id = Guid.NewGuid(),
            EventType = "test.event",
            TenantId = "t1",
            ActorId = "a1",
            CorrelationId = "c1",
            Timestamp = DateTimeOffset.UtcNow,
            Detail = "Original"
        };

        _db.AuditEvents.Add(entity);
        _db.SaveChanges();

        entity.Detail = "Modified";

        var ex = Assert.Throws<InvalidOperationException>(() => _db.SaveChanges());
        Assert.Contains("cannot be modified", ex.Message);
    }

    [Fact]
    public async Task Delete_AuditEvent_Sync_Throws()
    {
        var entity = new AuditEventEntity
        {
            Id = Guid.NewGuid(),
            EventType = "test.event",
            TenantId = "t1",
            ActorId = "a1",
            CorrelationId = "c1",
            Timestamp = DateTimeOffset.UtcNow,
            Detail = "Will try to delete sync"
        };

        _db.AuditEvents.Add(entity);
        await _db.SaveChangesAsync();

        _db.AuditEvents.Remove(entity);

        var ex = Assert.Throws<InvalidOperationException>(() => _db.SaveChanges());
        Assert.Contains("cannot be deleted", ex.Message);
    }

    [Fact]
    public async Task Modify_NonAuditEntity_Succeeds()
    {
        // Ensure other entities can still be modified normally
        var tenant = new TenantEntity
        {
            TenantId = "t-immutability-test",
            DisplayName = "Original",
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };

        _db.Tenants.Add(tenant);
        await _db.SaveChangesAsync();

        tenant.DisplayName = "Modified";
        await _db.SaveChangesAsync(); // Should not throw

        var updated = await _db.Tenants.FirstAsync(t => t.TenantId == "t-immutability-test");
        Assert.Equal("Modified", updated.DisplayName);
    }

    [Fact]
    public async Task Multiple_Inserts_Succeed()
    {
        for (int i = 0; i < 5; i++)
        {
            _db.AuditEvents.Add(new AuditEventEntity
            {
                Id = Guid.NewGuid(),
                EventType = $"batch.event.{i}",
                TenantId = "t-batch",
                ActorId = "a1",
                CorrelationId = $"c-{i}",
                Timestamp = DateTimeOffset.UtcNow,
                Detail = $"Batch detail {i}"
            });
        }

        await _db.SaveChangesAsync();

        Assert.Equal(5, await _db.AuditEvents.CountAsync(a => a.TenantId == "t-batch"));
    }

    [Fact]
    public async Task Modify_EventType_Throws()
    {
        var entity = new AuditEventEntity
        {
            Id = Guid.NewGuid(),
            EventType = "original.type",
            TenantId = "t1",
            ActorId = "a1",
            CorrelationId = "c1",
            Timestamp = DateTimeOffset.UtcNow,
            Detail = "Detail"
        };

        _db.AuditEvents.Add(entity);
        await _db.SaveChangesAsync();

        entity.EventType = "tampered.type";

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => _db.SaveChangesAsync());

        Assert.Contains("cannot be modified", ex.Message);
    }

    [Fact]
    public async Task Mixed_Insert_And_Modify_Throws()
    {
        // Insert first event
        var existing = new AuditEventEntity
        {
            Id = Guid.NewGuid(),
            EventType = "existing.event",
            TenantId = "t-mixed",
            ActorId = "a1",
            CorrelationId = "c1",
            Timestamp = DateTimeOffset.UtcNow,
            Detail = "Existing"
        };

        _db.AuditEvents.Add(existing);
        await _db.SaveChangesAsync();

        // Try to insert a new one while also modifying the existing one
        _db.AuditEvents.Add(new AuditEventEntity
        {
            Id = Guid.NewGuid(),
            EventType = "new.event",
            TenantId = "t-mixed",
            ActorId = "a1",
            CorrelationId = "c2",
            Timestamp = DateTimeOffset.UtcNow,
            Detail = "New"
        });

        existing.Detail = "Tampered";

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _db.SaveChangesAsync());
    }
}
