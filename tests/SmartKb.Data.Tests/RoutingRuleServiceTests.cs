using Microsoft.Extensions.Logging.Abstractions;
using SmartKb.Contracts;
using SmartKb.Contracts.Models;
using SmartKb.Contracts.Services;
using SmartKb.Data.Entities;
using SmartKb.Data.Repositories;

namespace SmartKb.Data.Tests;

public class RoutingRuleServiceTests : IDisposable
{
    private readonly SmartKbDbContext _db;
    private readonly RoutingRuleService _service;
    private readonly StubAuditWriter _auditWriter;

    public RoutingRuleServiceTests()
    {
        _db = TestDbContextFactory.Create();
        _auditWriter = new StubAuditWriter();
        SeedTenant();
        _service = new RoutingRuleService(_db, _auditWriter, NullLogger<RoutingRuleService>.Instance);
    }

    public void Dispose() => _db.Dispose();

    private void SeedTenant()
    {
        _db.Tenants.Add(new TenantEntity
        {
            TenantId = "t1",
            DisplayName = "Test",
            CreatedAt = DateTimeOffset.UtcNow,
        });
        _db.SaveChanges();
    }

    [Fact]
    public async Task CreateRule_Persists_AndWritesAudit()
    {
        var request = new CreateRoutingRuleRequest
        {
            ProductArea = "Billing",
            TargetTeam = "Finance",
        };

        var result = await _service.CreateRuleAsync("t1", "u1", "corr-1", request);

        Assert.Equal("Billing", result.ProductArea);
        Assert.Equal("Finance", result.TargetTeam);
        Assert.Equal(0.4f, result.EscalationThreshold);
        Assert.Equal("P2", result.MinSeverity);
        Assert.True(result.IsActive);
        Assert.NotEqual(Guid.Empty, result.RuleId);

        Assert.Single(_auditWriter.Events);
        Assert.Equal(AuditEventTypes.RoutingRuleCreated, _auditWriter.Events[0].EventType);
    }

    [Fact]
    public async Task GetRules_ReturnsAllForTenant()
    {
        await _service.CreateRuleAsync("t1", "u1", "c1",
            new CreateRoutingRuleRequest { ProductArea = "A", TargetTeam = "T1" });
        await _service.CreateRuleAsync("t1", "u1", "c2",
            new CreateRoutingRuleRequest { ProductArea = "B", TargetTeam = "T2" });

        var result = await _service.GetRulesAsync("t1");

        Assert.Equal(2, result.TotalCount);
        Assert.Equal(2, result.Rules.Count);
    }

    [Fact]
    public async Task GetRule_ReturnsSpecific()
    {
        var created = await _service.CreateRuleAsync("t1", "u1", "c1",
            new CreateRoutingRuleRequest { ProductArea = "Auth", TargetTeam = "Security" });

        var result = await _service.GetRuleAsync("t1", created.RuleId);

        Assert.NotNull(result);
        Assert.Equal("Auth", result!.ProductArea);
    }

    [Fact]
    public async Task GetRule_WrongTenant_ReturnsNull()
    {
        var created = await _service.CreateRuleAsync("t1", "u1", "c1",
            new CreateRoutingRuleRequest { ProductArea = "Auth", TargetTeam = "Security" });

        var result = await _service.GetRuleAsync("t-other", created.RuleId);

        Assert.Null(result);
    }

    [Fact]
    public async Task UpdateRule_ModifiesFields()
    {
        var created = await _service.CreateRuleAsync("t1", "u1", "c1",
            new CreateRoutingRuleRequest { ProductArea = "Auth", TargetTeam = "Security" });

        var updated = await _service.UpdateRuleAsync("t1", "u1", "c2", created.RuleId,
            new UpdateRoutingRuleRequest
            {
                TargetTeam = "Identity",
                EscalationThreshold = 0.3f,
                MinSeverity = "P1",
            });

        Assert.NotNull(updated);
        Assert.Equal("Identity", updated!.TargetTeam);
        Assert.Equal(0.3f, updated.EscalationThreshold);
        Assert.Equal("P1", updated.MinSeverity);

        Assert.Equal(2, _auditWriter.Events.Count);
        Assert.Equal(AuditEventTypes.RoutingRuleUpdated, _auditWriter.Events[1].EventType);
    }

    [Fact]
    public async Task UpdateRule_PartialUpdate_OnlyModifiesProvidedFields()
    {
        var created = await _service.CreateRuleAsync("t1", "u1", "c1",
            new CreateRoutingRuleRequest { ProductArea = "Auth", TargetTeam = "Security", EscalationThreshold = 0.5f });

        var updated = await _service.UpdateRuleAsync("t1", "u1", "c2", created.RuleId,
            new UpdateRoutingRuleRequest { TargetTeam = "Identity" });

        Assert.NotNull(updated);
        Assert.Equal("Identity", updated!.TargetTeam);
        Assert.Equal(0.5f, updated.EscalationThreshold); // Unchanged.
    }

    [Fact]
    public async Task DeleteRule_RemovesFromDb()
    {
        var created = await _service.CreateRuleAsync("t1", "u1", "c1",
            new CreateRoutingRuleRequest { ProductArea = "Auth", TargetTeam = "Security" });

        var deleted = await _service.DeleteRuleAsync("t1", "u1", "c2", created.RuleId);
        Assert.True(deleted);

        var result = await _service.GetRuleAsync("t1", created.RuleId);
        Assert.Null(result);

        Assert.Equal(AuditEventTypes.RoutingRuleDeleted, _auditWriter.Events.Last().EventType);
    }

    [Fact]
    public async Task DeleteRule_NotFound_ReturnsFalse()
    {
        var result = await _service.DeleteRuleAsync("t1", "u1", "c1", Guid.NewGuid());
        Assert.False(result);
    }

    [Fact]
    public async Task CreateRule_NormalizesInvalidSeverity()
    {
        var result = await _service.CreateRuleAsync("t1", "u1", "c1",
            new CreateRoutingRuleRequest { ProductArea = "X", TargetTeam = "Y", MinSeverity = "invalid" });

        Assert.Equal("P3", result.MinSeverity);
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
