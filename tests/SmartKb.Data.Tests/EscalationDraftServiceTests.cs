using Microsoft.Extensions.Logging.Abstractions;
using SmartKb.Contracts;
using SmartKb.Contracts.Configuration;
using SmartKb.Contracts.Models;
using SmartKb.Contracts.Services;
using SmartKb.Data.Entities;
using SmartKb.Data.Repositories;

namespace SmartKb.Data.Tests;

public class EscalationDraftServiceTests : IDisposable
{
    private readonly SmartKbDbContext _db;
    private readonly EscalationDraftService _service;
    private readonly StubAuditWriter _auditWriter;
    private readonly EscalationSettings _settings;

    public EscalationDraftServiceTests()
    {
        _db = TestDbContextFactory.Create();
        _auditWriter = new StubAuditWriter();
        _settings = new EscalationSettings();

        SeedData();

        var playbookService = new TeamPlaybookService(
            _db, _auditWriter, NullLogger<TeamPlaybookService>.Instance);

        _service = new EscalationDraftService(
            _db, _auditWriter, _settings, NullLogger<EscalationDraftService>.Instance,
            [], new StubSecretProvider(), playbookService);
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
            Id = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001"),
            TenantId = "t1",
            UserId = "u1",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        };
        _db.Sessions.Add(session);

        var message = new MessageEntity
        {
            Id = Guid.Parse("bbbbbbbb-0000-0000-0000-000000000001"),
            SessionId = session.Id,
            TenantId = "t1",
            Role = SmartKb.Contracts.Enums.MessageRole.Assistant,
            Content = "Escalation recommended.",
            ResponseType = "escalate",
            CreatedAt = DateTimeOffset.UtcNow,
        };
        _db.Messages.Add(message);
        _db.SaveChanges();
    }

    private static readonly Guid SessionId = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001");
    private static readonly Guid MessageId = Guid.Parse("bbbbbbbb-0000-0000-0000-000000000001");

    private CreateEscalationDraftRequest MakeRequest(string? title = null) => new()
    {
        SessionId = SessionId,
        MessageId = MessageId,
        Title = title ?? "Test Escalation",
        CustomerSummary = "Customer cannot log in.",
        StepsToReproduce = "1. Go to login. 2. Enter credentials. 3. Error shown.",
        LogsIdsRequested = "correlation-id-123",
        SuspectedComponent = "Auth Service",
        Severity = "P2",
        EvidenceLinks = [new CitationDto
        {
            ChunkId = "chunk_0",
            EvidenceId = "ev-1",
            Title = "Login Error",
            SourceUrl = "https://example.com/wiki/login",
            SourceSystem = "Wiki",
            Snippet = "Login failures observed.",
            UpdatedAt = DateTimeOffset.UtcNow,
            AccessLabel = "Internal",
        }],
        TargetTeam = "Auth Team",
        Reason = "Repeated login failures affecting multiple customers.",
    };

    [Fact]
    public async Task CreateDraft_Succeeds_WithValidData()
    {
        var result = await _service.CreateDraftAsync("t1", "u1", "corr-1", MakeRequest());

        Assert.NotEqual(Guid.Empty, result.DraftId);
        Assert.Equal(SessionId, result.SessionId);
        Assert.Equal(MessageId, result.MessageId);
        Assert.Equal("Test Escalation", result.Title);
        Assert.Equal("P2", result.Severity);
        Assert.Equal("Auth Team", result.TargetTeam);
        Assert.Single(result.EvidenceLinks);
        Assert.Equal("chunk_0", result.EvidenceLinks[0].ChunkId);
    }

    [Fact]
    public async Task CreateDraft_WritesAuditEvent()
    {
        await _service.CreateDraftAsync("t1", "u1", "corr-1", MakeRequest());
        Assert.Single(_auditWriter.Events);
        Assert.Equal(AuditEventTypes.EscalationDraftCreated, _auditWriter.Events[0].EventType);
    }

    [Fact]
    public async Task CreateDraft_Throws_WhenSessionNotOwned()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _service.CreateDraftAsync("t1", "other-user", "corr-1", MakeRequest()));
    }

    [Fact]
    public async Task CreateDraft_Throws_WhenMessageNotInSession()
    {
        var request = MakeRequest() with { MessageId = Guid.NewGuid() };
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _service.CreateDraftAsync("t1", "u1", "corr-1", request));
    }

    [Fact]
    public async Task CreateDraft_DefaultsTitle_WhenEmpty()
    {
        var request = MakeRequest() with { Title = "" };
        var result = await _service.CreateDraftAsync("t1", "u1", "corr-1", request);
        Assert.Equal("Escalation Draft", result.Title);
    }

    [Fact]
    public async Task CreateDraft_NormalizesSeverity()
    {
        var request = MakeRequest() with { Severity = "p1" };
        var result = await _service.CreateDraftAsync("t1", "u1", "corr-1", request);
        Assert.Equal("P1", result.Severity);
    }

    [Fact]
    public async Task CreateDraft_FallsBackSeverity_WhenInvalid()
    {
        var request = MakeRequest() with { Severity = "Critical" };
        var result = await _service.CreateDraftAsync("t1", "u1", "corr-1", request);
        Assert.Equal("P3", result.Severity);
    }

    [Fact]
    public async Task CreateDraft_UsesRoutingRule_WhenTargetTeamEmpty()
    {
        _db.EscalationRoutingRules.Add(new EscalationRoutingRuleEntity
        {
            Id = Guid.NewGuid(),
            TenantId = "t1",
            ProductArea = "Auth Service",
            TargetTeam = "Identity Team",
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        });
        await _db.SaveChangesAsync();

        var request = MakeRequest() with { TargetTeam = "" };
        var result = await _service.CreateDraftAsync("t1", "u1", "corr-1", request);
        Assert.Equal("Identity Team", result.TargetTeam);
    }

    [Fact]
    public async Task CreateDraft_UsesFallbackTeam_WhenNoRule()
    {
        var request = MakeRequest() with { TargetTeam = "", SuspectedComponent = "Unknown" };
        var result = await _service.CreateDraftAsync("t1", "u1", "corr-1", request);
        Assert.Equal("Engineering", result.TargetTeam);
    }

    [Fact]
    public async Task GetDraft_ReturnsDraft()
    {
        var created = await _service.CreateDraftAsync("t1", "u1", "corr-1", MakeRequest());
        var result = await _service.GetDraftAsync("t1", "u1", created.DraftId);
        Assert.NotNull(result);
        Assert.Equal(created.DraftId, result!.DraftId);
    }

    [Fact]
    public async Task GetDraft_ReturnsNull_WhenWrongUser()
    {
        var created = await _service.CreateDraftAsync("t1", "u1", "corr-1", MakeRequest());
        var result = await _service.GetDraftAsync("t1", "other-user", created.DraftId);
        Assert.Null(result);
    }

    [Fact]
    public async Task GetDraft_ReturnsNull_WhenWrongTenant()
    {
        var created = await _service.CreateDraftAsync("t1", "u1", "corr-1", MakeRequest());
        var result = await _service.GetDraftAsync("other-tenant", "u1", created.DraftId);
        Assert.Null(result);
    }

    [Fact]
    public async Task ListDrafts_ReturnsSessionDrafts()
    {
        await _service.CreateDraftAsync("t1", "u1", "corr-1", MakeRequest("Draft A"));
        await _service.CreateDraftAsync("t1", "u1", "corr-2", MakeRequest("Draft B"));

        var result = await _service.ListDraftsAsync("t1", "u1", SessionId);
        Assert.NotNull(result);
        Assert.Equal(2, result!.TotalCount);
        Assert.Equal(SessionId, result.SessionId);
    }

    [Fact]
    public async Task ListDrafts_ReturnsNull_WhenSessionNotFound()
    {
        var result = await _service.ListDraftsAsync("t1", "u1", Guid.NewGuid());
        Assert.Null(result);
    }

    [Fact]
    public async Task UpdateDraft_UpdatesFields()
    {
        var created = await _service.CreateDraftAsync("t1", "u1", "corr-1", MakeRequest());
        var update = new UpdateEscalationDraftRequest
        {
            Title = "Updated Title",
            Severity = "P1",
            TargetTeam = "Infra Team",
        };

        var (result, notFound) = await _service.UpdateDraftAsync("t1", "u1", created.DraftId, update);
        Assert.False(notFound);
        Assert.NotNull(result);
        Assert.Equal("Updated Title", result!.Title);
        Assert.Equal("P1", result.Severity);
        Assert.Equal("Infra Team", result.TargetTeam);
        // Unchanged fields preserved.
        Assert.Equal("Customer cannot log in.", result.CustomerSummary);
    }

    [Fact]
    public async Task UpdateDraft_ReturnsNotFound_WhenMissing()
    {
        var (result, notFound) = await _service.UpdateDraftAsync("t1", "u1", Guid.NewGuid(),
            new UpdateEscalationDraftRequest { Title = "x" });
        Assert.True(notFound);
        Assert.Null(result);
    }

    [Fact]
    public async Task ExportDraft_ReturnsMarkdown()
    {
        var created = await _service.CreateDraftAsync("t1", "u1", "corr-1", MakeRequest());
        var result = await _service.ExportDraftAsMarkdownAsync("t1", "u1", created.DraftId);

        Assert.NotNull(result);
        Assert.Equal(created.DraftId, result!.DraftId);
        Assert.Contains("# Test Escalation", result.Markdown);
        Assert.Contains("**Severity:** P2", result.Markdown);
        Assert.Contains("**Target Team:** Auth Team", result.Markdown);
        Assert.Contains("## Customer Summary", result.Markdown);
        Assert.Contains("## Steps to Reproduce", result.Markdown);
        Assert.Contains("## Evidence Links", result.Markdown);
        Assert.Contains("Login Error", result.Markdown);
    }

    [Fact]
    public async Task ExportDraft_SetsExportedAt()
    {
        var created = await _service.CreateDraftAsync("t1", "u1", "corr-1", MakeRequest());
        Assert.Null(created.ExportedAt);

        var exported = await _service.ExportDraftAsMarkdownAsync("t1", "u1", created.DraftId);
        Assert.NotEqual(default, exported!.ExportedAt);

        // Verify exported timestamp persisted.
        var fetched = await _service.GetDraftAsync("t1", "u1", created.DraftId);
        Assert.NotNull(fetched!.ExportedAt);
    }

    [Fact]
    public async Task ExportDraft_ReturnsNull_WhenNotFound()
    {
        var result = await _service.ExportDraftAsMarkdownAsync("t1", "u1", Guid.NewGuid());
        Assert.Null(result);
    }

    [Fact]
    public async Task DeleteDraft_SoftDeletes()
    {
        var created = await _service.CreateDraftAsync("t1", "u1", "corr-1", MakeRequest());
        var deleted = await _service.DeleteDraftAsync("t1", "u1", created.DraftId);
        Assert.True(deleted);

        // Should no longer be visible (query filter).
        var after = await _service.GetDraftAsync("t1", "u1", created.DraftId);
        Assert.Null(after);
    }

    [Fact]
    public async Task DeleteDraft_ReturnsFalse_WhenNotFound()
    {
        var result = await _service.DeleteDraftAsync("t1", "u1", Guid.NewGuid());
        Assert.False(result);
    }

    // ===== P1-003: ApproveAndCreateExternalAsync tests =====

    private static readonly Guid AdoConnectorId = Guid.Parse("cccccccc-0000-0000-0000-000000000001");
    private static readonly Guid ClickUpConnectorId = Guid.Parse("cccccccc-0000-0000-0000-000000000002");
    private static readonly Guid SharePointConnectorId = Guid.Parse("cccccccc-0000-0000-0000-000000000003");
    private static readonly Guid DisabledConnectorId = Guid.Parse("cccccccc-0000-0000-0000-000000000004");

    private void SeedConnectors()
    {
        _db.Connectors.AddRange(
            new ConnectorEntity
            {
                Id = AdoConnectorId,
                TenantId = "t1",
                Name = "ADO Connector",
                ConnectorType = SmartKb.Contracts.Enums.ConnectorType.AzureDevOps,
                Status = SmartKb.Contracts.Enums.ConnectorStatus.Enabled,
                AuthType = SmartKb.Contracts.Enums.SecretAuthType.Pat,
                KeyVaultSecretName = "ado-secret",
                SourceConfig = """{"organizationUrl":"https://dev.azure.com/testorg","projects":["MyProject"]}""",
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow,
            },
            new ConnectorEntity
            {
                Id = ClickUpConnectorId,
                TenantId = "t1",
                Name = "ClickUp Connector",
                ConnectorType = SmartKb.Contracts.Enums.ConnectorType.ClickUp,
                Status = SmartKb.Contracts.Enums.ConnectorStatus.Enabled,
                AuthType = SmartKb.Contracts.Enums.SecretAuthType.Pat,
                KeyVaultSecretName = "clickup-secret",
                SourceConfig = """{"workspaceId":"12345","listIds":["list-1"]}""",
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow,
            },
            new ConnectorEntity
            {
                Id = SharePointConnectorId,
                TenantId = "t1",
                Name = "SharePoint Connector",
                ConnectorType = SmartKb.Contracts.Enums.ConnectorType.SharePoint,
                Status = SmartKb.Contracts.Enums.ConnectorStatus.Enabled,
                AuthType = SmartKb.Contracts.Enums.SecretAuthType.OAuth,
                KeyVaultSecretName = "sp-secret",
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow,
            },
            new ConnectorEntity
            {
                Id = DisabledConnectorId,
                TenantId = "t1",
                Name = "Disabled ADO",
                ConnectorType = SmartKb.Contracts.Enums.ConnectorType.AzureDevOps,
                Status = SmartKb.Contracts.Enums.ConnectorStatus.Disabled,
                AuthType = SmartKb.Contracts.Enums.SecretAuthType.Pat,
                KeyVaultSecretName = "disabled-secret",
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow,
            });
        _db.SaveChanges();
    }

    private EscalationDraftService CreateServiceWithConnector(StubEscalationTargetConnector? connector = null)
    {
        var connectors = connector is not null
            ? new IEscalationTargetConnector[] { connector }
            : Array.Empty<IEscalationTargetConnector>();
        var playbookService = new TeamPlaybookService(
            _db, _auditWriter, NullLogger<TeamPlaybookService>.Instance);
        return new EscalationDraftService(
            _db, _auditWriter, _settings, NullLogger<EscalationDraftService>.Instance,
            connectors, new StubSecretProvider(), playbookService);
    }

    [Fact]
    public async Task ApproveAndCreateExternal_ReturnsNull_WhenDraftNotFound()
    {
        SeedConnectors();
        var svc = CreateServiceWithConnector();
        var request = new ApproveEscalationDraftRequest { ConnectorId = AdoConnectorId };

        var result = await svc.ApproveAndCreateExternalAsync("t1", "u1", "corr-1", Guid.NewGuid(), request);

        Assert.Null(result);
    }

    [Fact]
    public async Task ApproveAndCreateExternal_ThrowsWhenConnectorNotFound()
    {
        SeedConnectors();
        var svc = CreateServiceWithConnector();
        var draft = await svc.CreateDraftAsync("t1", "u1", "corr-1", MakeRequest());
        var request = new ApproveEscalationDraftRequest { ConnectorId = Guid.NewGuid() };

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            svc.ApproveAndCreateExternalAsync("t1", "u1", "corr-1", draft.DraftId, request));
    }

    [Fact]
    public async Task ApproveAndCreateExternal_ThrowsWhenConnectorDisabled()
    {
        SeedConnectors();
        var svc = CreateServiceWithConnector();
        var draft = await svc.CreateDraftAsync("t1", "u1", "corr-1", MakeRequest());
        var request = new ApproveEscalationDraftRequest { ConnectorId = DisabledConnectorId };

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            svc.ApproveAndCreateExternalAsync("t1", "u1", "corr-1", draft.DraftId, request));
        Assert.Contains("not enabled", ex.Message);
    }

    [Fact]
    public async Task ApproveAndCreateExternal_ThrowsWhenConnectorTypeNotSupported()
    {
        SeedConnectors();
        var svc = CreateServiceWithConnector();
        var draft = await svc.CreateDraftAsync("t1", "u1", "corr-1", MakeRequest());
        var request = new ApproveEscalationDraftRequest { ConnectorId = SharePointConnectorId };

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            svc.ApproveAndCreateExternalAsync("t1", "u1", "corr-1", draft.DraftId, request));
        Assert.Contains("SharePoint", ex.Message);
        Assert.Contains("does not support external escalation", ex.Message);
    }

    [Fact]
    public async Task ApproveAndCreateExternal_SuccessfulCreation_SetsExternalFields()
    {
        SeedConnectors();
        var stub = new StubEscalationTargetConnector(
            SmartKb.Contracts.Enums.ConnectorType.AzureDevOps,
            new ExternalWorkItemResult
            {
                Success = true,
                ExternalId = "12345",
                ExternalUrl = "https://dev.azure.com/testorg/MyProject/_workitems/edit/12345",
            });
        var svc = CreateServiceWithConnector(stub);
        var draft = await svc.CreateDraftAsync("t1", "u1", "corr-1", MakeRequest());
        _auditWriter.Events.Clear();
        var request = new ApproveEscalationDraftRequest
        {
            ConnectorId = AdoConnectorId,
            TargetProject = "MyProject",
        };

        var result = await svc.ApproveAndCreateExternalAsync("t1", "u1", "corr-2", draft.DraftId, request);

        Assert.NotNull(result);
        Assert.Equal("Created", result!.ExternalStatus);
        Assert.Equal("12345", result.ExternalId);
        Assert.Equal("https://dev.azure.com/testorg/MyProject/_workitems/edit/12345", result.ExternalUrl);
        Assert.NotNull(result.ApprovedAt);
        Assert.Equal("AzureDevOps", result.ConnectorType);
        Assert.Null(result.ErrorDetail);
    }

    [Fact]
    public async Task ApproveAndCreateExternal_FailedCreation_SetsFailedStatus()
    {
        SeedConnectors();
        var stub = new StubEscalationTargetConnector(
            SmartKb.Contracts.Enums.ConnectorType.AzureDevOps,
            new ExternalWorkItemResult
            {
                Success = false,
                ErrorDetail = "ADO API returned 403: Access denied",
            });
        var svc = CreateServiceWithConnector(stub);
        var draft = await svc.CreateDraftAsync("t1", "u1", "corr-1", MakeRequest());
        _auditWriter.Events.Clear();
        var request = new ApproveEscalationDraftRequest { ConnectorId = AdoConnectorId };

        var result = await svc.ApproveAndCreateExternalAsync("t1", "u1", "corr-2", draft.DraftId, request);

        Assert.NotNull(result);
        Assert.Equal("Failed", result!.ExternalStatus);
        Assert.Contains("403", result.ErrorDetail!);
        Assert.Null(result.ExternalId);
        Assert.Null(result.ExternalUrl);
    }

    [Fact]
    public async Task ApproveAndCreateExternal_IdempotentOnAlreadyCreated()
    {
        SeedConnectors();
        var stub = new StubEscalationTargetConnector(
            SmartKb.Contracts.Enums.ConnectorType.AzureDevOps,
            new ExternalWorkItemResult
            {
                Success = true,
                ExternalId = "99",
                ExternalUrl = "https://dev.azure.com/testorg/MyProject/_workitems/edit/99",
            });
        var svc = CreateServiceWithConnector(stub);
        var draft = await svc.CreateDraftAsync("t1", "u1", "corr-1", MakeRequest());
        var request = new ApproveEscalationDraftRequest { ConnectorId = AdoConnectorId };

        // First call.
        var result1 = await svc.ApproveAndCreateExternalAsync("t1", "u1", "corr-2", draft.DraftId, request);
        Assert.NotNull(result1);
        Assert.Equal("Created", result1!.ExternalStatus);

        var callCountAfterFirst = stub.CallCount;

        // Second call should return cached result without calling the connector again.
        var result2 = await svc.ApproveAndCreateExternalAsync("t1", "u1", "corr-3", draft.DraftId, request);
        Assert.NotNull(result2);
        Assert.Equal("Created", result2!.ExternalStatus);
        Assert.Equal("99", result2.ExternalId);
        Assert.Equal(callCountAfterFirst, stub.CallCount); // No new calls.
    }

    [Fact]
    public async Task ApproveAndCreateExternal_WritesAuditEvents()
    {
        SeedConnectors();
        var stub = new StubEscalationTargetConnector(
            SmartKb.Contracts.Enums.ConnectorType.AzureDevOps,
            new ExternalWorkItemResult
            {
                Success = true,
                ExternalId = "42",
                ExternalUrl = "https://example.com/wi/42",
            });
        var svc = CreateServiceWithConnector(stub);
        var draft = await svc.CreateDraftAsync("t1", "u1", "corr-1", MakeRequest());
        _auditWriter.Events.Clear();
        var request = new ApproveEscalationDraftRequest { ConnectorId = AdoConnectorId };

        await svc.ApproveAndCreateExternalAsync("t1", "u1", "corr-2", draft.DraftId, request);

        // Should write 2 audit events: Approved + ExternalCreated.
        Assert.Equal(2, _auditWriter.Events.Count);
        Assert.Equal(AuditEventTypes.EscalationDraftApproved, _auditWriter.Events[0].EventType);
        Assert.Equal(AuditEventTypes.EscalationExternalCreated, _auditWriter.Events[1].EventType);
        Assert.Contains("corr-2", _auditWriter.Events[0].CorrelationId);
    }

    [Fact]
    public async Task ApproveAndCreateExternal_FailedCreation_WritesFailedAuditEvent()
    {
        SeedConnectors();
        var stub = new StubEscalationTargetConnector(
            SmartKb.Contracts.Enums.ConnectorType.AzureDevOps,
            new ExternalWorkItemResult
            {
                Success = false,
                ErrorDetail = "timeout",
            });
        var svc = CreateServiceWithConnector(stub);
        var draft = await svc.CreateDraftAsync("t1", "u1", "corr-1", MakeRequest());
        _auditWriter.Events.Clear();
        var request = new ApproveEscalationDraftRequest { ConnectorId = AdoConnectorId };

        await svc.ApproveAndCreateExternalAsync("t1", "u1", "corr-2", draft.DraftId, request);

        Assert.Equal(2, _auditWriter.Events.Count);
        Assert.Equal(AuditEventTypes.EscalationDraftApproved, _auditWriter.Events[0].EventType);
        Assert.Equal(AuditEventTypes.EscalationExternalFailed, _auditWriter.Events[1].EventType);
    }

    [Fact]
    public void BuildExternalDescription_IncludesAllFields()
    {
        var entity = new EscalationDraftEntity
        {
            Id = Guid.NewGuid(),
            SessionId = SessionId,
            MessageId = MessageId,
            TenantId = "t1",
            UserId = "u1",
            Title = "Login Failure",
            CustomerSummary = "Cannot login after password reset.",
            StepsToReproduce = "1. Reset password\n2. Try to login",
            LogsIdsRequested = "correlation-xyz",
            SuspectedComponent = "Auth Service",
            Severity = "P1",
            EvidenceLinksJson = """[{"chunkId":"c1","evidenceId":"e1","title":"KB Article","sourceUrl":"https://wiki/login","sourceSystem":"Wiki","snippet":"Login issue","updatedAt":"2026-01-01T00:00:00Z","accessLabel":"Internal"}]""",
            TargetTeam = "Identity Team",
            Reason = "Repeated failures affecting multiple users.",
            CreatedAt = DateTimeOffset.UtcNow,
        };

        var description = EscalationDraftService.BuildExternalDescription(entity);

        Assert.Contains("Login Failure", description);
        Assert.Contains("**Severity:** P1", description);
        Assert.Contains("**Target Team:** Identity Team", description);
        Assert.Contains("**Suspected Component:** Auth Service", description);
        Assert.Contains("### Reason", description);
        Assert.Contains("Repeated failures", description);
        Assert.Contains("### Customer Summary", description);
        Assert.Contains("Cannot login after password reset", description);
        Assert.Contains("### Steps to Reproduce", description);
        Assert.Contains("1. Reset password", description);
        Assert.Contains("### Logs / IDs Requested", description);
        Assert.Contains("correlation-xyz", description);
        Assert.Contains("### Evidence Links", description);
        Assert.Contains("[KB Article](https://wiki/login)", description);
        Assert.Contains("Smart KB escalation workflow", description);
    }

    private sealed class StubEscalationTargetConnector : IEscalationTargetConnector
    {
        private readonly ExternalWorkItemResult _result;
        public int CallCount { get; private set; }

        public StubEscalationTargetConnector(SmartKb.Contracts.Enums.ConnectorType type, ExternalWorkItemResult result)
        {
            Type = type;
            _result = result;
        }

        public SmartKb.Contracts.Enums.ConnectorType Type { get; }

        public Task<ExternalWorkItemResult> CreateExternalWorkItemAsync(
            string sourceConfig, string secretValue, ExternalWorkItemRequest request, CancellationToken ct = default)
        {
            CallCount++;
            return Task.FromResult(_result);
        }
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

    private sealed class StubSecretProvider : ISecretProvider
    {
        public Task<string> GetSecretAsync(string secretName, CancellationToken cancellationToken = default)
            => Task.FromResult("stub-secret");

        public Task SetSecretAsync(string secretName, string secretValue, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task DeleteSecretAsync(string secretName, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task<SecretProperties?> GetSecretPropertiesAsync(string secretName, CancellationToken cancellationToken = default)
            => Task.FromResult<SecretProperties?>(new SecretProperties(secretName, DateTimeOffset.UtcNow, null, null, true));
    }
}
