using Microsoft.Extensions.Logging.Abstractions;
using SmartKb.Contracts;
using SmartKb.Contracts.Enums;
using SmartKb.Contracts.Models;
using SmartKb.Contracts.Services;
using SmartKb.Data.Entities;
using SmartKb.Data.Repositories;

namespace SmartKb.Data.Tests;

public class OutcomeServiceTests : IDisposable
{
    private readonly SmartKbDbContext _db;
    private readonly OutcomeService _service;
    private readonly StubAuditWriter _auditWriter;

    private static readonly Guid SessionId = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000011");

    public OutcomeServiceTests()
    {
        _db = TestDbContextFactory.Create();
        _auditWriter = new StubAuditWriter();
        SeedData();
        _service = new OutcomeService(_db, _auditWriter, NullLogger<OutcomeService>.Instance);
    }

    public void Dispose() => _db.Dispose();

    private void SeedData()
    {
        _db.Tenants.Add(new TenantEntity
        {
            TenantId = "t1",
            DisplayName = "Test",
            CreatedAt = DateTimeOffset.UtcNow,
        });

        var session = new SessionEntity
        {
            Id = SessionId,
            TenantId = "t1",
            UserId = "u1",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        };
        _db.Sessions.Add(session);
        _db.SaveChanges();
    }

    [Fact]
    public async Task RecordOutcome_ResolvedWithoutEscalation_Persists()
    {
        var request = new RecordOutcomeRequest
        {
            ResolutionType = ResolutionType.ResolvedWithoutEscalation,
        };

        var result = await _service.RecordOutcomeAsync("t1", "u1", "corr-1", SessionId, request);

        Assert.Equal("ResolvedWithoutEscalation", result.ResolutionType);
        Assert.Equal(SessionId, result.SessionId);
        Assert.NotEqual(Guid.Empty, result.OutcomeId);
        Assert.Null(result.TargetTeam);
        Assert.Null(result.Acceptance);
    }

    [Fact]
    public async Task RecordOutcome_Escalated_WithAllFields()
    {
        var request = new RecordOutcomeRequest
        {
            ResolutionType = ResolutionType.Escalated,
            TargetTeam = "Engineering",
            Acceptance = true,
            TimeToAssign = TimeSpan.FromMinutes(15),
            TimeToResolve = TimeSpan.FromHours(2),
            EscalationTraceId = "trace-esc-1",
        };

        var result = await _service.RecordOutcomeAsync("t1", "u1", "corr-2", SessionId, request);

        Assert.Equal("Escalated", result.ResolutionType);
        Assert.Equal("Engineering", result.TargetTeam);
        Assert.True(result.Acceptance);
        Assert.NotNull(result.TimeToAssign);
        Assert.NotNull(result.TimeToResolve);
        Assert.Equal("trace-esc-1", result.EscalationTraceId);
    }

    [Fact]
    public async Task RecordOutcome_Rerouted_Persists()
    {
        var request = new RecordOutcomeRequest
        {
            ResolutionType = ResolutionType.Rerouted,
            TargetTeam = "Platform",
            Acceptance = false,
        };

        var result = await _service.RecordOutcomeAsync("t1", "u1", "corr-3", SessionId, request);

        Assert.Equal("Rerouted", result.ResolutionType);
        Assert.Equal("Platform", result.TargetTeam);
        Assert.False(result.Acceptance);
    }

    [Fact]
    public async Task RecordOutcome_WritesAuditEvent()
    {
        var request = new RecordOutcomeRequest
        {
            ResolutionType = ResolutionType.ResolvedWithoutEscalation,
        };

        await _service.RecordOutcomeAsync("t1", "u1", "corr-4", SessionId, request);

        Assert.Single(_auditWriter.Events);
        Assert.Equal(AuditEventTypes.ChatOutcome, _auditWriter.Events[0].EventType);
        Assert.Equal("t1", _auditWriter.Events[0].TenantId);
        Assert.Equal("u1", _auditWriter.Events[0].ActorId);
    }

    [Fact]
    public async Task RecordOutcome_Throws_WhenSessionNotFound()
    {
        var request = new RecordOutcomeRequest { ResolutionType = ResolutionType.Escalated };
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _service.RecordOutcomeAsync("t1", "u1", "corr-5", Guid.NewGuid(), request));
    }

    [Fact]
    public async Task RecordOutcome_Throws_WhenWrongTenant()
    {
        var request = new RecordOutcomeRequest { ResolutionType = ResolutionType.Escalated };
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _service.RecordOutcomeAsync("other-tenant", "u1", "corr-6", SessionId, request));
    }

    [Fact]
    public async Task RecordOutcome_Throws_WhenWrongUser()
    {
        var request = new RecordOutcomeRequest { ResolutionType = ResolutionType.Escalated };
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _service.RecordOutcomeAsync("t1", "other-user", "corr-7", SessionId, request));
    }

    [Fact]
    public async Task RecordOutcome_AllowsMultipleOutcomesPerSession()
    {
        await _service.RecordOutcomeAsync("t1", "u1", "corr-8", SessionId,
            new RecordOutcomeRequest { ResolutionType = ResolutionType.ResolvedWithoutEscalation });
        await _service.RecordOutcomeAsync("t1", "u1", "corr-9", SessionId,
            new RecordOutcomeRequest { ResolutionType = ResolutionType.Escalated, TargetTeam = "Eng" });

        var outcomes = await _service.GetOutcomesAsync("t1", "u1", SessionId);
        Assert.NotNull(outcomes);
        Assert.Equal(2, outcomes.TotalCount);
    }

    [Fact]
    public async Task GetOutcomes_ReturnsSessionOutcomes()
    {
        await _service.RecordOutcomeAsync("t1", "u1", "corr-10", SessionId,
            new RecordOutcomeRequest { ResolutionType = ResolutionType.ResolvedWithoutEscalation });

        var result = await _service.GetOutcomesAsync("t1", "u1", SessionId);

        Assert.NotNull(result);
        Assert.Equal(SessionId, result.SessionId);
        Assert.Equal(1, result.TotalCount);
        Assert.Equal("ResolvedWithoutEscalation", result.Outcomes[0].ResolutionType);
    }

    [Fact]
    public async Task GetOutcomes_ReturnsNull_WhenSessionNotFound()
    {
        var result = await _service.GetOutcomesAsync("t1", "u1", Guid.NewGuid());
        Assert.Null(result);
    }

    [Fact]
    public async Task GetOutcomes_ReturnsNull_WhenWrongTenant()
    {
        var result = await _service.GetOutcomesAsync("other-tenant", "u1", SessionId);
        Assert.Null(result);
    }

    [Fact]
    public async Task GetOutcomes_ReturnsEmpty_WhenNoOutcomes()
    {
        var result = await _service.GetOutcomesAsync("t1", "u1", SessionId);

        Assert.NotNull(result);
        Assert.Equal(0, result.TotalCount);
        Assert.Empty(result.Outcomes);
    }

    [Fact]
    public async Task GetOutcomes_OrderedByCreatedAtDescending()
    {
        await _service.RecordOutcomeAsync("t1", "u1", "c1", SessionId,
            new RecordOutcomeRequest { ResolutionType = ResolutionType.ResolvedWithoutEscalation });
        await _service.RecordOutcomeAsync("t1", "u1", "c2", SessionId,
            new RecordOutcomeRequest { ResolutionType = ResolutionType.Escalated, TargetTeam = "Eng" });

        var result = await _service.GetOutcomesAsync("t1", "u1", SessionId);

        Assert.NotNull(result);
        Assert.Equal(2, result.TotalCount);
        Assert.Equal("Escalated", result.Outcomes[0].ResolutionType);
        Assert.Equal("ResolvedWithoutEscalation", result.Outcomes[1].ResolutionType);
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
