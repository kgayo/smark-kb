using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using SmartKb.Contracts.Configuration;
using SmartKb.Contracts.Models;
using SmartKb.Contracts.Services;
using SmartKb.Data.Entities;
using SmartKb.Data.Exceptions;
using SmartKb.Data.Repositories;

namespace SmartKb.Data.Tests;

public class SynonymMapServiceTests : IDisposable
{
    private readonly SmartKbDbContext _db;
    private readonly StubAuditWriter _auditWriter;
    private readonly SynonymMapService _service;

    private const string TenantId = "t1";
    private const string ActorId = "admin-1";
    private const string CorrelationId = "corr-1";

    public SynonymMapServiceTests()
    {
        _db = TestDbContextFactory.Create();
        _auditWriter = new StubAuditWriter();
        SeedTenant();
        _service = new SynonymMapService(
            _db, _auditWriter, new SearchServiceSettings(),
            NullLogger<SynonymMapService>.Instance);
    }

    public void Dispose() => _db.Dispose();

    private void SeedTenant()
    {
        _db.Tenants.Add(new TenantEntity
        {
            TenantId = TenantId,
            DisplayName = "Test",
            CreatedAt = DateTimeOffset.UtcNow,
        });
        _db.SaveChanges();
    }

    // --- Create ---

    [Fact]
    public async Task CreateAsync_ValidRule_ReturnsResponse()
    {
        var (response, validation) = await _service.CreateAsync(TenantId, ActorId, CorrelationId,
            new CreateSynonymRuleRequest { Rule = "crash, BSOD, blue screen", GroupName = "general", Description = "Test" });

        Assert.Null(validation);
        Assert.NotNull(response);
        Assert.Equal("crash, BSOD, blue screen", response!.Rule);
        Assert.Equal("general", response.GroupName);
        Assert.Equal("Test", response.Description);
        Assert.True(response.IsActive);
        Assert.Equal(TenantId, response.TenantId);
        Assert.Equal(ActorId, response.CreatedBy);
    }

    [Fact]
    public async Task CreateAsync_ExplicitMapping_ReturnsResponse()
    {
        var (response, validation) = await _service.CreateAsync(TenantId, ActorId, CorrelationId,
            new CreateSynonymRuleRequest { Rule = "BSOD => blue screen of death" });

        Assert.Null(validation);
        Assert.NotNull(response);
        Assert.Equal("BSOD => blue screen of death", response!.Rule);
    }

    [Fact]
    public async Task CreateAsync_EmptyRule_ReturnsValidationError()
    {
        var (response, validation) = await _service.CreateAsync(TenantId, ActorId, CorrelationId,
            new CreateSynonymRuleRequest { Rule = "" });

        Assert.NotNull(validation);
        Assert.False(validation!.IsValid);
        Assert.Null(response);
    }

    [Fact]
    public async Task CreateAsync_InvalidExplicitMapping_ReturnsValidationError()
    {
        var (response, validation) = await _service.CreateAsync(TenantId, ActorId, CorrelationId,
            new CreateSynonymRuleRequest { Rule = "=> something" });

        Assert.NotNull(validation);
        Assert.False(validation!.IsValid);
        Assert.Contains(validation.Errors, e => e.Contains("=>"));
    }

    [Fact]
    public async Task CreateAsync_WritesAuditEvent()
    {
        await _service.CreateAsync(TenantId, ActorId, CorrelationId,
            new CreateSynonymRuleRequest { Rule = "error, exception" });

        Assert.Single(_auditWriter.Events);
        Assert.Equal("SynonymRule.Created", _auditWriter.Events[0].EventType);
    }

    // --- List ---

    [Fact]
    public async Task ListAsync_ReturnsAllRulesForTenant()
    {
        await _service.CreateAsync(TenantId, ActorId, CorrelationId,
            new CreateSynonymRuleRequest { Rule = "crash, BSOD", GroupName = "general" });
        await _service.CreateAsync(TenantId, ActorId, CorrelationId,
            new CreateSynonymRuleRequest { Rule = "HTTP 500, 500 error", GroupName = "error-codes" });

        var result = await _service.ListAsync(TenantId);

        Assert.Equal(2, result.TotalCount);
        Assert.Equal(2, result.Groups.Count);
        Assert.Contains("general", result.Groups);
        Assert.Contains("error-codes", result.Groups);
    }

    [Fact]
    public async Task ListAsync_FiltersByGroup()
    {
        await _service.CreateAsync(TenantId, ActorId, CorrelationId,
            new CreateSynonymRuleRequest { Rule = "crash, BSOD", GroupName = "general" });
        await _service.CreateAsync(TenantId, ActorId, CorrelationId,
            new CreateSynonymRuleRequest { Rule = "HTTP 500, 500 error", GroupName = "error-codes" });

        var result = await _service.ListAsync(TenantId, "error-codes");

        Assert.Single(result.Rules);
        Assert.Equal("error-codes", result.Rules[0].GroupName);
    }

    [Fact]
    public async Task ListAsync_TenantIsolation_OtherTenantNotVisible()
    {
        _db.Tenants.Add(new TenantEntity { TenantId = "t2", DisplayName = "Other", CreatedAt = DateTimeOffset.UtcNow });
        _db.SaveChanges();

        await _service.CreateAsync(TenantId, ActorId, CorrelationId,
            new CreateSynonymRuleRequest { Rule = "crash, BSOD" });
        await _service.CreateAsync("t2", ActorId, CorrelationId,
            new CreateSynonymRuleRequest { Rule = "deploy, release" });

        var t1Rules = await _service.ListAsync(TenantId);
        var t2Rules = await _service.ListAsync("t2");

        Assert.Single(t1Rules.Rules);
        Assert.Single(t2Rules.Rules);
        Assert.Equal("crash, BSOD", t1Rules.Rules[0].Rule);
        Assert.Equal("deploy, release", t2Rules.Rules[0].Rule);
    }

    // --- Get ---

    [Fact]
    public async Task GetAsync_ExistingRule_ReturnsResponse()
    {
        var (created, _) = await _service.CreateAsync(TenantId, ActorId, CorrelationId,
            new CreateSynonymRuleRequest { Rule = "crash, BSOD" });

        var result = await _service.GetAsync(TenantId, created!.Id);

        Assert.NotNull(result);
        Assert.Equal(created.Id, result!.Id);
    }

    [Fact]
    public async Task GetAsync_NonExistentRule_ReturnsNull()
    {
        var result = await _service.GetAsync(TenantId, Guid.NewGuid());
        Assert.Null(result);
    }

    [Fact]
    public async Task GetAsync_WrongTenant_ReturnsNull()
    {
        var (created, _) = await _service.CreateAsync(TenantId, ActorId, CorrelationId,
            new CreateSynonymRuleRequest { Rule = "crash, BSOD" });

        var result = await _service.GetAsync("other-tenant", created!.Id);
        Assert.Null(result);
    }

    // --- Update ---

    [Fact]
    public async Task UpdateAsync_ValidChanges_ReturnsUpdated()
    {
        var (created, _) = await _service.CreateAsync(TenantId, ActorId, CorrelationId,
            new CreateSynonymRuleRequest { Rule = "crash, BSOD" });

        var (updated, validation, notFound) = await _service.UpdateAsync(
            TenantId, ActorId, CorrelationId, created!.Id,
            new UpdateSynonymRuleRequest { Rule = "crash, BSOD, blue screen", Description = "Updated" });

        Assert.False(notFound);
        Assert.Null(validation);
        Assert.NotNull(updated);
        Assert.Equal("crash, BSOD, blue screen", updated!.Rule);
        Assert.Equal("Updated", updated.Description);
    }

    [Fact]
    public async Task UpdateAsync_ToggleActive_Works()
    {
        var (created, _) = await _service.CreateAsync(TenantId, ActorId, CorrelationId,
            new CreateSynonymRuleRequest { Rule = "crash, BSOD" });

        var (updated, _, _) = await _service.UpdateAsync(
            TenantId, ActorId, CorrelationId, created!.Id,
            new UpdateSynonymRuleRequest { IsActive = false });

        Assert.False(updated!.IsActive);
    }

    [Fact]
    public async Task UpdateAsync_InvalidRule_ReturnsValidation()
    {
        var (created, _) = await _service.CreateAsync(TenantId, ActorId, CorrelationId,
            new CreateSynonymRuleRequest { Rule = "crash, BSOD" });

        var (_, validation, _) = await _service.UpdateAsync(
            TenantId, ActorId, CorrelationId, created!.Id,
            new UpdateSynonymRuleRequest { Rule = "" });

        Assert.NotNull(validation);
        Assert.False(validation!.IsValid);
    }

    [Fact]
    public async Task UpdateAsync_NotFound_ReturnsNotFound()
    {
        var (_, _, notFound) = await _service.UpdateAsync(
            TenantId, ActorId, CorrelationId, Guid.NewGuid(),
            new UpdateSynonymRuleRequest { Rule = "test" });

        Assert.True(notFound);
    }

    // --- Delete ---

    [Fact]
    public async Task DeleteAsync_ExistingRule_ReturnsTrue()
    {
        var (created, _) = await _service.CreateAsync(TenantId, ActorId, CorrelationId,
            new CreateSynonymRuleRequest { Rule = "crash, BSOD" });

        var deleted = await _service.DeleteAsync(TenantId, ActorId, CorrelationId, created!.Id);
        Assert.True(deleted);

        var result = await _service.GetAsync(TenantId, created.Id);
        Assert.Null(result);
    }

    [Fact]
    public async Task DeleteAsync_NonExistent_ReturnsFalse()
    {
        var deleted = await _service.DeleteAsync(TenantId, ActorId, CorrelationId, Guid.NewGuid());
        Assert.False(deleted);
    }

    // --- Seed ---

    [Fact]
    public async Task SeedDefaultsAsync_EmptyTenant_SeedsRules()
    {
        var count = await _service.SeedDefaultsAsync(TenantId, ActorId, CorrelationId);

        Assert.True(count > 0);

        var result = await _service.ListAsync(TenantId);
        Assert.Equal(count, result.TotalCount);
    }

    [Fact]
    public async Task SeedDefaultsAsync_ExistingRules_ReturnsZero()
    {
        await _service.CreateAsync(TenantId, ActorId, CorrelationId,
            new CreateSynonymRuleRequest { Rule = "crash, BSOD" });

        var count = await _service.SeedDefaultsAsync(TenantId, ActorId, CorrelationId);
        Assert.Equal(0, count);
    }

    [Fact]
    public async Task SeedDefaultsAsync_OverwriteTrue_ReplacesExisting()
    {
        await _service.CreateAsync(TenantId, ActorId, CorrelationId,
            new CreateSynonymRuleRequest { Rule = "custom rule, my term" });

        var count = await _service.SeedDefaultsAsync(TenantId, ActorId, CorrelationId, overwriteExisting: true);
        Assert.True(count > 0);

        var result = await _service.ListAsync(TenantId);
        Assert.DoesNotContain(result.Rules, r => r.Rule == "custom rule, my term");
    }

    // --- Sync ---

    [Fact]
    public async Task SyncToSearchAsync_NoSearchClient_ReturnsFailure()
    {
        var result = await _service.SyncToSearchAsync(TenantId, CorrelationId);

        Assert.False(result.Success);
        Assert.Contains("not configured", result.ErrorDetail!);
    }

    // --- Validation (static) ---

    [Fact]
    public void ValidateRule_EquivalentSynonyms_Valid()
    {
        var result = SynonymMapService.ValidateRule("crash, BSOD, blue screen");
        Assert.True(result.IsValid);
    }

    [Fact]
    public void ValidateRule_ExplicitMapping_Valid()
    {
        var result = SynonymMapService.ValidateRule("BSOD => blue screen of death");
        Assert.True(result.IsValid);
    }

    [Fact]
    public void ValidateRule_SingleTerm_Valid()
    {
        var result = SynonymMapService.ValidateRule("crash");
        Assert.True(result.IsValid);
    }

    [Fact]
    public void ValidateRule_Empty_Invalid()
    {
        var result = SynonymMapService.ValidateRule("");
        Assert.False(result.IsValid);
    }

    [Fact]
    public void ValidateRule_TooLong_Invalid()
    {
        var result = SynonymMapService.ValidateRule(new string('a', 1025));
        Assert.False(result.IsValid);
    }

    [Fact]
    public void ValidateRule_InvalidExplicit_BothSidesRequired()
    {
        var result = SynonymMapService.ValidateRule("=> something");
        Assert.False(result.IsValid);
    }

    [Fact]
    public void ValidateRule_EmptyTermInCommaList_Invalid()
    {
        var result = SynonymMapService.ValidateRule("crash, , BSOD");
        Assert.False(result.IsValid);
    }

    [Fact]
    public void ValidateRule_UnicodeExplicitMapping_Valid()
    {
        var result = SynonymMapService.ValidateRule("Ärger => anger");
        Assert.True(result.IsValid);
    }

    [Fact]
    public void ValidateRule_UnicodeEquivalent_Valid()
    {
        var result = SynonymMapService.ValidateRule("café, coffee shop");
        Assert.True(result.IsValid);
    }

    // --- Default rules ---

    [Fact]
    public void GetDefaultSynonymRules_HasExpectedGroups()
    {
        var defaults = SynonymMapService.GetDefaultSynonymRules();
        Assert.True(defaults.Count >= 10);

        var groups = defaults.Select(d => d.Group).Distinct().ToList();
        Assert.Contains("general", groups);
        Assert.Contains("error-codes", groups);
        Assert.Contains("product-names", groups);
    }

    [Fact]
    public async Task Update_ConcurrentModification_ThrowsConcurrencyConflictException()
    {
        // Create a synonym rule.
        var (created, _, _) = await _service.CreateAsync(
            "t1", "actor", "corr", new CreateSynonymRuleRequest
            {
                GroupName = "test",
                Rule = "crash, BSOD",
            });

        // Load entity and simulate concurrent modification.
        var entity = await _db.SynonymMaps.FirstAsync(s => s.Id == created!.Id);
        await _db.Database.ExecuteSqlRawAsync(
            "UPDATE SynonymMaps SET UpdatedAt = {0} WHERE Id = {1}",
            entity.UpdatedAt.AddMinutes(5), entity.Id);

        // The service update should detect the conflict and throw.
        await Assert.ThrowsAsync<ConcurrencyConflictException>(() =>
            _service.UpdateAsync("t1", "actor", "corr", created!.Id,
                new UpdateSynonymRuleRequest { Description = "updated" }));
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
