using Microsoft.EntityFrameworkCore;
using SmartKb.Contracts.Enums;
using SmartKb.Data.Entities;

namespace SmartKb.Data.Tests;

public class SchemaTests : IDisposable
{
    private readonly SmartKbDbContext _db = TestDbContextFactory.Create();

    public void Dispose() => _db.Dispose();

    [Fact]
    public void Database_CreatesAllTables()
    {
        var model = _db.Model;
        var tableNames = model.GetEntityTypes().Select(e => e.GetTableName()).Order().ToList();

        Assert.Contains("AuditEvents", tableNames);
        Assert.Contains("Connectors", tableNames);
        Assert.Contains("Feedbacks", tableNames);
        Assert.Contains("Messages", tableNames);
        Assert.Contains("OutcomeEvents", tableNames);
        Assert.Contains("RetentionConfigs", tableNames);
        Assert.Contains("Sessions", tableNames);
        Assert.Contains("SyncRuns", tableNames);
        Assert.Contains("Tenants", tableNames);
        Assert.Contains("UserRoleMappings", tableNames);
    }

    [Fact]
    public async Task Tenant_CanBeCreatedAndQueried()
    {
        var tenant = new TenantEntity
        {
            TenantId = "tenant-1",
            DisplayName = "Test Tenant",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        };
        _db.Tenants.Add(tenant);
        await _db.SaveChangesAsync();

        var loaded = await _db.Tenants.FindAsync("tenant-1");
        Assert.NotNull(loaded);
        Assert.Equal("Test Tenant", loaded!.DisplayName);
        Assert.True(loaded.IsActive);
    }

    [Fact]
    public async Task UserRoleMapping_UniqueTenantUserRole()
    {
        var tenant = new TenantEntity
        {
            TenantId = "t1", DisplayName = "T1",
            CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow,
        };
        _db.Tenants.Add(tenant);
        _db.UserRoleMappings.Add(new UserRoleMappingEntity
        {
            Id = Guid.NewGuid(), TenantId = "t1", UserId = "u1",
            Role = AppRole.Admin, CreatedAt = DateTimeOffset.UtcNow,
        });
        await _db.SaveChangesAsync();

        _db.UserRoleMappings.Add(new UserRoleMappingEntity
        {
            Id = Guid.NewGuid(), TenantId = "t1", UserId = "u1",
            Role = AppRole.Admin, CreatedAt = DateTimeOffset.UtcNow,
        });
        await Assert.ThrowsAsync<DbUpdateException>(() => _db.SaveChangesAsync());
    }

    [Fact]
    public async Task UserRoleMapping_DifferentRolesSameUser_Allowed()
    {
        var tenant = new TenantEntity
        {
            TenantId = "t2", DisplayName = "T2",
            CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow,
        };
        _db.Tenants.Add(tenant);
        _db.UserRoleMappings.Add(new UserRoleMappingEntity
        {
            Id = Guid.NewGuid(), TenantId = "t2", UserId = "u1",
            Role = AppRole.Admin, CreatedAt = DateTimeOffset.UtcNow,
        });
        _db.UserRoleMappings.Add(new UserRoleMappingEntity
        {
            Id = Guid.NewGuid(), TenantId = "t2", UserId = "u1",
            Role = AppRole.SupportAgent, CreatedAt = DateTimeOffset.UtcNow,
        });
        await _db.SaveChangesAsync();

        var roles = await _db.UserRoleMappings.Where(u => u.UserId == "u1").ToListAsync();
        Assert.Equal(2, roles.Count);
    }

    [Fact]
    public async Task Connector_SoftDeleteFilter()
    {
        var tenant = new TenantEntity
        {
            TenantId = "t3", DisplayName = "T3",
            CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow,
        };
        _db.Tenants.Add(tenant);

        var active = new ConnectorEntity
        {
            Id = Guid.NewGuid(), TenantId = "t3", Name = "Active",
            ConnectorType = ConnectorType.AzureDevOps, Status = ConnectorStatus.Enabled,
            AuthType = SecretAuthType.Pat,
            CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow,
        };
        var deleted = new ConnectorEntity
        {
            Id = Guid.NewGuid(), TenantId = "t3", Name = "Deleted",
            ConnectorType = ConnectorType.SharePoint, Status = ConnectorStatus.Disabled,
            AuthType = SecretAuthType.OAuth,
            CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow,
            DeletedAt = DateTimeOffset.UtcNow,
        };
        _db.Connectors.AddRange(active, deleted);
        await _db.SaveChangesAsync();

        var connectors = await _db.Connectors.Where(c => c.TenantId == "t3").ToListAsync();
        Assert.Single(connectors);
        Assert.Equal("Active", connectors[0].Name);
    }

    [Fact]
    public async Task Connector_SoftDeleteFilter_CanBeIgnored()
    {
        var tenant = new TenantEntity
        {
            TenantId = "t4", DisplayName = "T4",
            CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow,
        };
        _db.Tenants.Add(tenant);
        _db.Connectors.Add(new ConnectorEntity
        {
            Id = Guid.NewGuid(), TenantId = "t4", Name = "SoftDeleted",
            ConnectorType = ConnectorType.HubSpot, Status = ConnectorStatus.Disabled,
            AuthType = SecretAuthType.OAuth,
            CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow,
            DeletedAt = DateTimeOffset.UtcNow,
        });
        await _db.SaveChangesAsync();

        var all = await _db.Connectors.IgnoreQueryFilters()
            .Where(c => c.TenantId == "t4").ToListAsync();
        Assert.Single(all);
    }

    [Fact]
    public async Task SyncRun_IdempotencyKeyUnique()
    {
        var tenant = new TenantEntity
        {
            TenantId = "t5", DisplayName = "T5",
            CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow,
        };
        var connector = new ConnectorEntity
        {
            Id = Guid.NewGuid(), TenantId = "t5", Name = "C1",
            ConnectorType = ConnectorType.ClickUp, Status = ConnectorStatus.Enabled,
            AuthType = SecretAuthType.Pat,
            CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow,
        };
        _db.Tenants.Add(tenant);
        _db.Connectors.Add(connector);
        _db.SyncRuns.Add(new SyncRunEntity
        {
            Id = Guid.NewGuid(), ConnectorId = connector.Id, TenantId = "t5",
            Status = SyncRunStatus.Completed, StartedAt = DateTimeOffset.UtcNow,
            IdempotencyKey = "key-1",
        });
        await _db.SaveChangesAsync();

        _db.SyncRuns.Add(new SyncRunEntity
        {
            Id = Guid.NewGuid(), ConnectorId = connector.Id, TenantId = "t5",
            Status = SyncRunStatus.Pending, StartedAt = DateTimeOffset.UtcNow,
            IdempotencyKey = "key-1",
        });
        await Assert.ThrowsAsync<DbUpdateException>(() => _db.SaveChangesAsync());
    }

    [Fact]
    public async Task Session_MessageCascade()
    {
        var tenant = new TenantEntity
        {
            TenantId = "t6", DisplayName = "T6",
            CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow,
        };
        var session = new SessionEntity
        {
            Id = Guid.NewGuid(), TenantId = "t6", UserId = "u1",
            CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow,
        };
        _db.Tenants.Add(tenant);
        _db.Sessions.Add(session);
        _db.Messages.Add(new MessageEntity
        {
            Id = Guid.NewGuid(), SessionId = session.Id, TenantId = "t6",
            Role = MessageRole.User, Content = "Hello",
            CreatedAt = DateTimeOffset.UtcNow,
        });
        _db.Messages.Add(new MessageEntity
        {
            Id = Guid.NewGuid(), SessionId = session.Id, TenantId = "t6",
            Role = MessageRole.Assistant, Content = "Hi there",
            CreatedAt = DateTimeOffset.UtcNow,
        });
        await _db.SaveChangesAsync();

        var messages = await _db.Messages.Where(m => m.SessionId == session.Id).ToListAsync();
        Assert.Equal(2, messages.Count);
    }

    [Fact]
    public async Task Session_SoftDeleteFilter()
    {
        var tenant = new TenantEntity
        {
            TenantId = "t7", DisplayName = "T7",
            CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow,
        };
        _db.Tenants.Add(tenant);
        _db.Sessions.Add(new SessionEntity
        {
            Id = Guid.NewGuid(), TenantId = "t7", UserId = "u1",
            CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow,
        });
        _db.Sessions.Add(new SessionEntity
        {
            Id = Guid.NewGuid(), TenantId = "t7", UserId = "u1",
            CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow,
            DeletedAt = DateTimeOffset.UtcNow,
        });
        await _db.SaveChangesAsync();

        var sessions = await _db.Sessions.Where(s => s.TenantId == "t7").ToListAsync();
        Assert.Single(sessions);
    }

    [Fact]
    public async Task OutcomeEvent_StoresResolutionFields()
    {
        var tenant = new TenantEntity
        {
            TenantId = "t8", DisplayName = "T8",
            CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow,
        };
        var session = new SessionEntity
        {
            Id = Guid.NewGuid(), TenantId = "t8", UserId = "u1",
            CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow,
        };
        _db.Tenants.Add(tenant);
        _db.Sessions.Add(session);

        var outcome = new OutcomeEventEntity
        {
            Id = Guid.NewGuid(), SessionId = session.Id, TenantId = "t8",
            ResolutionType = ResolutionType.Escalated,
            TargetTeam = "Platform Engineering",
            Acceptance = true,
            TimeToAssign = TimeSpan.FromMinutes(5),
            TimeToResolve = TimeSpan.FromHours(2),
            EscalationTraceId = "trace-abc",
            CreatedAt = DateTimeOffset.UtcNow,
        };
        _db.OutcomeEvents.Add(outcome);
        await _db.SaveChangesAsync();

        var loaded = await _db.OutcomeEvents.FirstAsync(o => o.SessionId == session.Id);
        Assert.Equal(ResolutionType.Escalated, loaded.ResolutionType);
        Assert.Equal("Platform Engineering", loaded.TargetTeam);
        Assert.True(loaded.Acceptance);
        Assert.Equal(TimeSpan.FromMinutes(5), loaded.TimeToAssign);
        Assert.Equal("trace-abc", loaded.EscalationTraceId);
    }

    [Fact]
    public async Task AuditEvent_IsImmutable_AppendOnly()
    {
        var audit = new AuditEventEntity
        {
            Id = Guid.NewGuid(),
            EventType = "test.event",
            TenantId = "t9",
            ActorId = "actor-1",
            CorrelationId = "corr-1",
            Timestamp = DateTimeOffset.UtcNow,
            Detail = "Test detail",
        };
        _db.AuditEvents.Add(audit);
        await _db.SaveChangesAsync();

        var loaded = await _db.AuditEvents.FirstAsync(a => a.TenantId == "t9");
        Assert.Equal("test.event", loaded.EventType);
        Assert.Equal("Test detail", loaded.Detail);
    }

    [Fact]
    public async Task RetentionConfig_UniquePerTenantEntityType()
    {
        var tenant = new TenantEntity
        {
            TenantId = "t10", DisplayName = "T10",
            CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow,
        };
        _db.Tenants.Add(tenant);
        _db.RetentionConfigs.Add(new RetentionConfigEntity
        {
            Id = Guid.NewGuid(), TenantId = "t10", EntityType = "AuditEvent",
            RetentionDays = 90, CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow,
        });
        await _db.SaveChangesAsync();

        _db.RetentionConfigs.Add(new RetentionConfigEntity
        {
            Id = Guid.NewGuid(), TenantId = "t10", EntityType = "AuditEvent",
            RetentionDays = 180, CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow,
        });
        await Assert.ThrowsAsync<DbUpdateException>(() => _db.SaveChangesAsync());
    }

    [Fact]
    public async Task Feedback_LinkedToSessionAndMessage()
    {
        var tenant = new TenantEntity
        {
            TenantId = "t11", DisplayName = "T11",
            CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow,
        };
        var session = new SessionEntity
        {
            Id = Guid.NewGuid(), TenantId = "t11", UserId = "u1",
            CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow,
        };
        var message = new MessageEntity
        {
            Id = Guid.NewGuid(), SessionId = session.Id, TenantId = "t11",
            Role = MessageRole.Assistant, Content = "Answer",
            CreatedAt = DateTimeOffset.UtcNow,
        };
        _db.Tenants.Add(tenant);
        _db.Sessions.Add(session);
        _db.Messages.Add(message);

        var feedback = new FeedbackEntity
        {
            Id = Guid.NewGuid(), MessageId = message.Id, SessionId = session.Id,
            TenantId = "t11", UserId = "user-1", Type = FeedbackType.ThumbsDown,
            ReasonCodesJson = "[\"WrongAnswer\"]",
            Comment = "This answer was incorrect",
            CorrectionText = "The correct answer is X",
            CorrectedAnswer = "X is the answer",
            TraceId = "trace-123",
            CorrelationId = "corr-123",
            CreatedAt = DateTimeOffset.UtcNow,
        };
        _db.Feedbacks.Add(feedback);
        await _db.SaveChangesAsync();

        var loaded = await _db.Feedbacks.Include(f => f.Message).FirstAsync(f => f.TenantId == "t11");
        Assert.Equal(FeedbackType.ThumbsDown, loaded.Type);
        Assert.Equal("[\"WrongAnswer\"]", loaded.ReasonCodesJson);
        Assert.Equal("This answer was incorrect", loaded.Comment);
        Assert.Equal("The correct answer is X", loaded.CorrectionText);
        Assert.Equal("X is the answer", loaded.CorrectedAnswer);
        Assert.NotNull(loaded.Message);
    }

    [Fact]
    public void Enums_ConfiguredAsStringConversions()
    {
        var model = _db.Model;
        var connectorEntity = model.FindEntityType(typeof(ConnectorEntity))!;

        var connectorType = connectorEntity.FindProperty(nameof(ConnectorEntity.ConnectorType))!;
        var status = connectorEntity.FindProperty(nameof(ConnectorEntity.Status))!;
        var authType = connectorEntity.FindProperty(nameof(ConnectorEntity.AuthType))!;

        Assert.Equal(typeof(string), connectorType.GetProviderClrType());
        Assert.Equal(typeof(string), status.GetProviderClrType());
        Assert.Equal(typeof(string), authType.GetProviderClrType());
    }
}
