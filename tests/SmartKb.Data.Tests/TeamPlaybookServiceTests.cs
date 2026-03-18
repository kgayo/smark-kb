using Microsoft.Extensions.Logging.Abstractions;
using SmartKb.Contracts;
using SmartKb.Contracts.Models;
using SmartKb.Contracts.Services;
using SmartKb.Data.Entities;
using SmartKb.Data.Repositories;

namespace SmartKb.Data.Tests;

public class TeamPlaybookServiceTests : IDisposable
{
    private readonly SmartKbDbContext _db;
    private readonly TeamPlaybookService _service;
    private readonly StubAuditWriter _auditWriter;

    public TeamPlaybookServiceTests()
    {
        _db = TestDbContextFactory.Create();
        _auditWriter = new StubAuditWriter();
        SeedTenant();
        _service = new TeamPlaybookService(_db, _auditWriter, NullLogger<TeamPlaybookService>.Instance);
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
    public async Task CreatePlaybook_Persists_AndWritesAudit()
    {
        var request = new CreateTeamPlaybookRequest
        {
            TeamName = "Billing",
            Description = "Billing team playbook",
            RequiredFields = ["CustomerSummary", "LogsIdsRequested"],
            Checklist = ["Verify account ID", "Check payment status"],
            ContactChannel = "#billing-oncall",
            RequiresApproval = true,
            MinSeverity = "P2",
            AutoRouteSeverity = "P1",
            MaxConcurrentEscalations = 5,
            FallbackTeam = "Engineering",
        };

        var result = await _service.CreatePlaybookAsync("t1", "u1", "corr-1", request);

        Assert.Equal("Billing", result.TeamName);
        Assert.Equal("Billing team playbook", result.Description);
        Assert.Equal(["CustomerSummary", "LogsIdsRequested"], result.RequiredFields);
        Assert.Equal(["Verify account ID", "Check payment status"], result.Checklist);
        Assert.Equal("#billing-oncall", result.ContactChannel);
        Assert.True(result.RequiresApproval);
        Assert.Equal("P2", result.MinSeverity);
        Assert.Equal("P1", result.AutoRouteSeverity);
        Assert.Equal(5, result.MaxConcurrentEscalations);
        Assert.Equal("Engineering", result.FallbackTeam);
        Assert.True(result.IsActive);
        Assert.NotEqual(Guid.Empty, result.Id);

        Assert.Single(_auditWriter.Events);
        Assert.Equal(AuditEventTypes.PlaybookCreated, _auditWriter.Events[0].EventType);
    }

    [Fact]
    public async Task CreatePlaybook_DuplicateTeamName_Throws()
    {
        await _service.CreatePlaybookAsync("t1", "u1", "c1",
            new CreateTeamPlaybookRequest { TeamName = "Billing" });

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _service.CreatePlaybookAsync("t1", "u1", "c2",
                new CreateTeamPlaybookRequest { TeamName = "Billing" }));
    }

    [Fact]
    public async Task CreatePlaybook_EmptyTeamName_Throws()
    {
        await Assert.ThrowsAsync<ArgumentException>(() =>
            _service.CreatePlaybookAsync("t1", "u1", "c1",
                new CreateTeamPlaybookRequest { TeamName = "" }));
    }

    [Fact]
    public async Task CreatePlaybook_InvalidSeverity_Throws()
    {
        await Assert.ThrowsAsync<ArgumentException>(() =>
            _service.CreatePlaybookAsync("t1", "u1", "c1",
                new CreateTeamPlaybookRequest { TeamName = "X", MinSeverity = "CRITICAL" }));
    }

    [Fact]
    public async Task CreatePlaybook_InvalidRequiredField_Throws()
    {
        await Assert.ThrowsAsync<ArgumentException>(() =>
            _service.CreatePlaybookAsync("t1", "u1", "c1",
                new CreateTeamPlaybookRequest { TeamName = "X", RequiredFields = ["NonExistentField"] }));
    }

    [Fact]
    public async Task CreatePlaybook_MaxConcurrentLessThanOne_Throws()
    {
        await Assert.ThrowsAsync<ArgumentException>(() =>
            _service.CreatePlaybookAsync("t1", "u1", "c1",
                new CreateTeamPlaybookRequest { TeamName = "X", MaxConcurrentEscalations = 0 }));
    }

    [Fact]
    public async Task GetPlaybooks_ReturnsAllForTenant()
    {
        await _service.CreatePlaybookAsync("t1", "u1", "c1",
            new CreateTeamPlaybookRequest { TeamName = "Alpha" });
        await _service.CreatePlaybookAsync("t1", "u1", "c2",
            new CreateTeamPlaybookRequest { TeamName = "Beta" });

        var result = await _service.GetPlaybooksAsync("t1");

        Assert.Equal(2, result.TotalCount);
        Assert.Equal(2, result.Playbooks.Count);
        Assert.Equal("Alpha", result.Playbooks[0].TeamName); // Ordered by name.
        Assert.Equal("Beta", result.Playbooks[1].TeamName);
    }

    [Fact]
    public async Task GetPlaybook_ById_ReturnsSpecific()
    {
        var created = await _service.CreatePlaybookAsync("t1", "u1", "c1",
            new CreateTeamPlaybookRequest { TeamName = "Auth" });

        var result = await _service.GetPlaybookAsync("t1", created.Id);

        Assert.NotNull(result);
        Assert.Equal("Auth", result!.TeamName);
    }

    [Fact]
    public async Task GetPlaybook_WrongTenant_ReturnsNull()
    {
        var created = await _service.CreatePlaybookAsync("t1", "u1", "c1",
            new CreateTeamPlaybookRequest { TeamName = "Auth" });

        var result = await _service.GetPlaybookAsync("t-other", created.Id);

        Assert.Null(result);
    }

    [Fact]
    public async Task GetPlaybookByTeam_ReturnsCorrectPlaybook()
    {
        await _service.CreatePlaybookAsync("t1", "u1", "c1",
            new CreateTeamPlaybookRequest { TeamName = "Auth" });

        var result = await _service.GetPlaybookByTeamAsync("t1", "Auth");

        Assert.NotNull(result);
        Assert.Equal("Auth", result!.TeamName);
    }

    [Fact]
    public async Task GetPlaybookByTeam_NotFound_ReturnsNull()
    {
        var result = await _service.GetPlaybookByTeamAsync("t1", "NonExistent");
        Assert.Null(result);
    }

    [Fact]
    public async Task UpdatePlaybook_ModifiesFields()
    {
        var created = await _service.CreatePlaybookAsync("t1", "u1", "c1",
            new CreateTeamPlaybookRequest { TeamName = "Auth", Description = "Original" });

        var updated = await _service.UpdatePlaybookAsync("t1", "u1", "c2", created.Id,
            new UpdateTeamPlaybookRequest
            {
                Description = "Updated",
                RequiresApproval = true,
                Checklist = ["Step 1"],
                IsActive = false,
            });

        Assert.NotNull(updated);
        Assert.Equal("Updated", updated!.Description);
        Assert.True(updated.RequiresApproval);
        Assert.Equal(["Step 1"], updated.Checklist);
        Assert.False(updated.IsActive);

        Assert.Equal(2, _auditWriter.Events.Count);
        Assert.Equal(AuditEventTypes.PlaybookUpdated, _auditWriter.Events[1].EventType);
    }

    [Fact]
    public async Task UpdatePlaybook_NotFound_ReturnsNull()
    {
        var result = await _service.UpdatePlaybookAsync("t1", "u1", "c1", Guid.NewGuid(),
            new UpdateTeamPlaybookRequest { Description = "X" });
        Assert.Null(result);
    }

    [Fact]
    public async Task DeletePlaybook_SoftDeletes()
    {
        var created = await _service.CreatePlaybookAsync("t1", "u1", "c1",
            new CreateTeamPlaybookRequest { TeamName = "Auth" });

        var deleted = await _service.DeletePlaybookAsync("t1", "u1", "c2", created.Id);
        Assert.True(deleted);

        var result = await _service.GetPlaybookAsync("t1", created.Id);
        Assert.Null(result); // Soft-deleted, invisible.

        Assert.Equal(AuditEventTypes.PlaybookDeleted, _auditWriter.Events.Last().EventType);
    }

    [Fact]
    public async Task DeletePlaybook_NotFound_ReturnsFalse()
    {
        var result = await _service.DeletePlaybookAsync("t1", "u1", "c1", Guid.NewGuid());
        Assert.False(result);
    }

    [Fact]
    public async Task DeletePlaybook_AllowsTeamNameReuse()
    {
        var created = await _service.CreatePlaybookAsync("t1", "u1", "c1",
            new CreateTeamPlaybookRequest { TeamName = "Auth" });

        await _service.DeletePlaybookAsync("t1", "u1", "c2", created.Id);

        // Should be able to create a new playbook with the same team name.
        var reCreated = await _service.CreatePlaybookAsync("t1", "u1", "c3",
            new CreateTeamPlaybookRequest { TeamName = "Auth" });

        Assert.NotEqual(created.Id, reCreated.Id);
        Assert.Equal("Auth", reCreated.TeamName);
    }

    // --- Validation Tests ---

    [Fact]
    public async Task ValidateDraft_NoPlaybook_ReturnsValid()
    {
        var draft = new CreateEscalationDraftRequest
        {
            SessionId = Guid.NewGuid(),
            MessageId = Guid.NewGuid(),
        };

        var result = await _service.ValidateDraftAsync("t1", "NonExistentTeam", draft);

        Assert.True(result.IsValid);
        Assert.Empty(result.MissingRequiredFields);
        Assert.Empty(result.Checklist);
        Assert.False(result.RequiresApproval);
    }

    [Fact]
    public async Task ValidateDraft_MissingRequiredFields_ReturnsInvalid()
    {
        await _service.CreatePlaybookAsync("t1", "u1", "c1",
            new CreateTeamPlaybookRequest
            {
                TeamName = "Billing",
                RequiredFields = ["CustomerSummary", "LogsIdsRequested", "EvidenceLinks"],
                Checklist = ["Check account"],
            });

        var draft = new CreateEscalationDraftRequest
        {
            SessionId = Guid.NewGuid(),
            MessageId = Guid.NewGuid(),
            CustomerSummary = "Has value",
            // LogsIdsRequested and EvidenceLinks are missing.
        };

        var result = await _service.ValidateDraftAsync("t1", "Billing", draft);

        Assert.False(result.IsValid);
        Assert.Contains("LogsIdsRequested", result.MissingRequiredFields);
        Assert.Contains("EvidenceLinks", result.MissingRequiredFields);
        Assert.DoesNotContain("CustomerSummary", result.MissingRequiredFields);
        Assert.Equal(["Check account"], result.Checklist);
    }

    [Fact]
    public async Task ValidateDraft_AllFieldsPresent_ReturnsValid()
    {
        await _service.CreatePlaybookAsync("t1", "u1", "c1",
            new CreateTeamPlaybookRequest
            {
                TeamName = "Billing",
                RequiredFields = ["CustomerSummary", "Title"],
            });

        var draft = new CreateEscalationDraftRequest
        {
            SessionId = Guid.NewGuid(),
            MessageId = Guid.NewGuid(),
            CustomerSummary = "Problem description",
            Title = "Some Title",
        };

        var result = await _service.ValidateDraftAsync("t1", "Billing", draft);

        Assert.True(result.IsValid);
        Assert.Empty(result.MissingRequiredFields);
    }

    [Fact]
    public async Task ValidateDraft_SeverityBelowMinimum_ReturnsPolicyViolation()
    {
        await _service.CreatePlaybookAsync("t1", "u1", "c1",
            new CreateTeamPlaybookRequest
            {
                TeamName = "Infra",
                MinSeverity = "P1",
            });

        var draft = new CreateEscalationDraftRequest
        {
            SessionId = Guid.NewGuid(),
            MessageId = Guid.NewGuid(),
            Severity = "P3",
        };

        var result = await _service.ValidateDraftAsync("t1", "Infra", draft);

        Assert.False(result.IsValid);
        Assert.NotNull(result.PolicyViolation);
        Assert.Contains("P3", result.PolicyViolation!);
        Assert.Contains("P1", result.PolicyViolation!);
    }

    [Fact]
    public async Task ValidateDraft_SeverityMeetsMinimum_NoPolicyViolation()
    {
        await _service.CreatePlaybookAsync("t1", "u1", "c1",
            new CreateTeamPlaybookRequest
            {
                TeamName = "Infra",
                MinSeverity = "P2",
            });

        var draft = new CreateEscalationDraftRequest
        {
            SessionId = Guid.NewGuid(),
            MessageId = Guid.NewGuid(),
            Severity = "P1",
        };

        var result = await _service.ValidateDraftAsync("t1", "Infra", draft);

        Assert.True(result.IsValid);
        Assert.Null(result.PolicyViolation);
    }

    [Fact]
    public async Task ValidateDraft_ReturnsApprovalRequirement()
    {
        await _service.CreatePlaybookAsync("t1", "u1", "c1",
            new CreateTeamPlaybookRequest
            {
                TeamName = "Security",
                RequiresApproval = true,
                ContactChannel = "#security-team",
            });

        var draft = new CreateEscalationDraftRequest
        {
            SessionId = Guid.NewGuid(),
            MessageId = Guid.NewGuid(),
        };

        var result = await _service.ValidateDraftAsync("t1", "Security", draft);

        Assert.True(result.IsValid);
        Assert.True(result.RequiresApproval);
        Assert.Equal("#security-team", result.ContactChannel);
    }

    [Fact]
    public async Task ValidateDraft_InactivePlaybook_ReturnsValid()
    {
        var created = await _service.CreatePlaybookAsync("t1", "u1", "c1",
            new CreateTeamPlaybookRequest
            {
                TeamName = "Billing",
                RequiredFields = ["Title", "CustomerSummary"],
            });

        await _service.UpdatePlaybookAsync("t1", "u1", "c2", created.Id,
            new UpdateTeamPlaybookRequest { IsActive = false });

        var draft = new CreateEscalationDraftRequest
        {
            SessionId = Guid.NewGuid(),
            MessageId = Guid.NewGuid(),
            // Missing Title and CustomerSummary — but playbook is inactive.
        };

        var result = await _service.ValidateDraftAsync("t1", "Billing", draft);

        Assert.True(result.IsValid); // Inactive playbook is ignored.
    }

    [Fact]
    public async Task ValidateDraft_MaxConcurrentReached_ReturnsPolicyViolation()
    {
        await _service.CreatePlaybookAsync("t1", "u1", "c1",
            new CreateTeamPlaybookRequest
            {
                TeamName = "OnCall",
                MaxConcurrentEscalations = 1,
                FallbackTeam = "Engineering",
            });

        // Seed an open escalation draft targeting this team.
        SeedSession();
        _db.EscalationDrafts.Add(new EscalationDraftEntity
        {
            Id = Guid.NewGuid(),
            SessionId = _sessionId,
            MessageId = _messageId,
            TenantId = "t1",
            UserId = "u1",
            Title = "Existing Draft",
            CustomerSummary = "x",
            StepsToReproduce = "x",
            LogsIdsRequested = "x",
            SuspectedComponent = "x",
            Severity = "P1",
            EvidenceLinksJson = "[]",
            TargetTeam = "OnCall",
            Reason = "Test",
            CreatedAt = DateTimeOffset.UtcNow,
        });
        _db.SaveChanges();

        var draft = new CreateEscalationDraftRequest
        {
            SessionId = _sessionId,
            MessageId = _messageId,
        };

        var result = await _service.ValidateDraftAsync("t1", "OnCall", draft);

        Assert.False(result.IsValid);
        Assert.NotNull(result.PolicyViolation);
        Assert.Contains("max concurrent", result.PolicyViolation!);
        Assert.Contains("Engineering", result.PolicyViolation!);
    }

    // --- IsFieldPopulated Tests ---

    [Theory]
    [InlineData("Title", true)]
    [InlineData("CustomerSummary", true)]
    [InlineData("StepsToReproduce", true)]
    [InlineData("LogsIdsRequested", true)]
    [InlineData("SuspectedComponent", true)]
    [InlineData("Severity", true)]
    [InlineData("Reason", true)]
    public void IsFieldPopulated_WithValue_ReturnsTrue(string fieldName, bool expected)
    {
        var draft = new CreateEscalationDraftRequest
        {
            SessionId = Guid.NewGuid(),
            MessageId = Guid.NewGuid(),
            Title = "T",
            CustomerSummary = "C",
            StepsToReproduce = "S",
            LogsIdsRequested = "L",
            SuspectedComponent = "SC",
            Severity = "P1",
            Reason = "R",
            EvidenceLinks = [new CitationDto { Title = "x", SourceUrl = "y", ChunkId = "z", EvidenceId = "e1", SourceSystem = "ADO", Snippet = "s", UpdatedAt = DateTimeOffset.UtcNow, AccessLabel = "public" }],
        };

        Assert.Equal(expected, TeamPlaybookService.IsFieldPopulated(draft, fieldName));
    }

    [Fact]
    public void IsFieldPopulated_EvidenceLinks_EmptyList_ReturnsFalse()
    {
        var draft = new CreateEscalationDraftRequest
        {
            SessionId = Guid.NewGuid(),
            MessageId = Guid.NewGuid(),
        };

        Assert.False(TeamPlaybookService.IsFieldPopulated(draft, "EvidenceLinks"));
    }

    private Guid _sessionId;
    private Guid _messageId;

    private void SeedSession()
    {
        _sessionId = Guid.NewGuid();
        _messageId = Guid.NewGuid();
        _db.Sessions.Add(new SessionEntity
        {
            Id = _sessionId,
            TenantId = "t1",
            UserId = "u1",
            Title = "Test Session",
            CreatedAt = DateTimeOffset.UtcNow,
            ExpiresAt = DateTimeOffset.UtcNow.AddHours(1),
        });
        _db.Messages.Add(new MessageEntity
        {
            Id = _messageId,
            SessionId = _sessionId,
            TenantId = "t1",
            Role = SmartKb.Contracts.Enums.MessageRole.User,
            Content = "Test message",
            CreatedAt = DateTimeOffset.UtcNow,
        });
        _db.SaveChanges();
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
