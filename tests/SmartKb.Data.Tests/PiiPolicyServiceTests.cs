using Microsoft.Extensions.Logging.Abstractions;
using SmartKb.Contracts;
using SmartKb.Contracts.Models;
using SmartKb.Contracts.Services;
using SmartKb.Data.Entities;
using SmartKb.Data.Repositories;

namespace SmartKb.Data.Tests;

public class PiiPolicyServiceTests : IDisposable
{
    private readonly SmartKbDbContext _db;
    private readonly StubAuditWriter _auditWriter;
    private readonly PiiPolicyService _service;

    public PiiPolicyServiceTests()
    {
        _db = TestDbContextFactory.Create();
        _auditWriter = new StubAuditWriter();

        _db.Tenants.Add(new TenantEntity
        {
            TenantId = "t1",
            DisplayName = "Test Tenant",
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow,
        });
        _db.SaveChanges();

        _service = new PiiPolicyService(
            _db, _auditWriter, NullLogger<PiiPolicyService>.Instance);
    }

    public void Dispose() => _db.Dispose();

    [Fact]
    public async Task GetPolicy_NoneExists_ReturnsNull()
    {
        var result = await _service.GetPolicyAsync("t1");
        Assert.Null(result);
    }

    [Fact]
    public async Task UpsertPolicy_CreatesNew_ReturnsPolicy()
    {
        var request = new PiiPolicyUpdateRequest
        {
            EnforcementMode = "redact",
            EnabledPiiTypes = ["email", "ssn"],
            AuditRedactions = true,
        };

        var result = await _service.UpsertPolicyAsync("t1", request, "admin-1");

        Assert.Equal("t1", result.TenantId);
        Assert.Equal("redact", result.EnforcementMode);
        Assert.Equal(2, result.EnabledPiiTypes.Count);
        Assert.Contains("email", result.EnabledPiiTypes);
        Assert.Contains("ssn", result.EnabledPiiTypes);
        Assert.True(result.AuditRedactions);
    }

    [Fact]
    public async Task UpsertPolicy_UpdatesExisting()
    {
        var initial = new PiiPolicyUpdateRequest
        {
            EnforcementMode = "redact",
            EnabledPiiTypes = ["email"],
        };
        await _service.UpsertPolicyAsync("t1", initial, "admin-1");

        var update = new PiiPolicyUpdateRequest
        {
            EnforcementMode = "detect",
            EnabledPiiTypes = ["email", "phone", "ssn", "credit_card"],
            AuditRedactions = false,
        };
        var result = await _service.UpsertPolicyAsync("t1", update, "admin-1");

        Assert.Equal("detect", result.EnforcementMode);
        Assert.Equal(4, result.EnabledPiiTypes.Count);
        Assert.False(result.AuditRedactions);
    }

    [Fact]
    public async Task UpsertPolicy_WithCustomPatterns()
    {
        var request = new PiiPolicyUpdateRequest
        {
            EnforcementMode = "redact",
            EnabledPiiTypes = ["email"],
            CustomPatterns =
            [
                new CustomPiiPattern
                {
                    Name = "order_id",
                    Pattern = @"ORD-\d{8}",
                    Placeholder = "[REDACTED-ORDER-ID]",
                },
            ],
        };

        var result = await _service.UpsertPolicyAsync("t1", request, "admin-1");

        Assert.Single(result.CustomPatterns);
        Assert.Equal("order_id", result.CustomPatterns[0].Name);
    }

    [Fact]
    public async Task UpsertPolicy_InvalidMode_Throws()
    {
        var request = new PiiPolicyUpdateRequest
        {
            EnforcementMode = "invalid",
            EnabledPiiTypes = ["email"],
        };

        await Assert.ThrowsAsync<ArgumentException>(
            () => _service.UpsertPolicyAsync("t1", request, "admin-1"));
    }

    [Fact]
    public async Task UpsertPolicy_InvalidPiiType_Throws()
    {
        var request = new PiiPolicyUpdateRequest
        {
            EnforcementMode = "redact",
            EnabledPiiTypes = ["email", "invalid_type"],
        };

        await Assert.ThrowsAsync<ArgumentException>(
            () => _service.UpsertPolicyAsync("t1", request, "admin-1"));
    }

    [Fact]
    public async Task UpsertPolicy_InvalidRegex_Throws()
    {
        var request = new PiiPolicyUpdateRequest
        {
            EnforcementMode = "redact",
            EnabledPiiTypes = ["email"],
            CustomPatterns =
            [
                new CustomPiiPattern { Name = "bad", Pattern = "[invalid", Placeholder = "[X]" },
            ],
        };

        await Assert.ThrowsAsync<ArgumentException>(
            () => _service.UpsertPolicyAsync("t1", request, "admin-1"));
    }

    [Fact]
    public async Task DeletePolicy_Existing_ReturnsTrue()
    {
        await _service.UpsertPolicyAsync("t1", new PiiPolicyUpdateRequest
        {
            EnforcementMode = "redact",
            EnabledPiiTypes = ["email"],
        }, "admin-1");

        var deleted = await _service.DeletePolicyAsync("t1", "admin-1");
        Assert.True(deleted);

        var result = await _service.GetPolicyAsync("t1");
        Assert.Null(result);
    }

    [Fact]
    public async Task DeletePolicy_NonExistent_ReturnsFalse()
    {
        var deleted = await _service.DeletePolicyAsync("t1", "admin-1");
        Assert.False(deleted);
    }

    [Fact]
    public async Task UpsertPolicy_EmitsAuditEvent()
    {
        await _service.UpsertPolicyAsync("t1", new PiiPolicyUpdateRequest
        {
            EnforcementMode = "redact",
            EnabledPiiTypes = ["email"],
        }, "admin-1");

        Assert.Contains(_auditWriter.Events,
            e => e.EventType == AuditEventTypes.PiiPolicyUpdated && e.TenantId == "t1");
    }

    [Fact]
    public async Task GetPolicy_AfterUpsert_ReturnsStored()
    {
        await _service.UpsertPolicyAsync("t1", new PiiPolicyUpdateRequest
        {
            EnforcementMode = "detect",
            EnabledPiiTypes = ["phone", "credit_card"],
            AuditRedactions = false,
        }, "admin-1");

        var result = await _service.GetPolicyAsync("t1");

        Assert.NotNull(result);
        Assert.Equal("detect", result.EnforcementMode);
        Assert.Equal(2, result.EnabledPiiTypes.Count);
        Assert.Contains("phone", result.EnabledPiiTypes);
        Assert.Contains("credit_card", result.EnabledPiiTypes);
        Assert.False(result.AuditRedactions);
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
