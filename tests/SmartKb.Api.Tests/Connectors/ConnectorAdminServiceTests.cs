using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using SmartKb.Api.Audit;
using SmartKb.Api.Connectors;
using SmartKb.Contracts;
using SmartKb.Contracts.Configuration;
using SmartKb.Contracts.Enums;
using SmartKb.Contracts.Models;
using SmartKb.Contracts.Services;
using SmartKb.Data;
using SmartKb.Data.Entities;

namespace SmartKb.Api.Tests.Connectors;

public sealed class ConnectorAdminServiceTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly ServiceProvider _sp;
    private readonly SmartKbDbContext _db;
    private readonly InMemoryAuditEventWriter _auditWriter;
    private readonly InMemorySecretProvider _secretProvider;
    private readonly TestSyncJobPublisher _syncJobPublisher;
    private readonly ConnectorAdminService _service;

    private const string TenantId = "test-tenant";
    private const string ActorId = "test-user";
    private const string CorrelationId = "corr-1";

    public ConnectorAdminServiceTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<SmartKbDbContext>(o => o.UseSqlite(_connection));

        _sp = services.BuildServiceProvider();
        _db = _sp.GetRequiredService<SmartKbDbContext>();
        _db.Database.EnsureCreated();

        _db.Tenants.Add(new TenantEntity { TenantId = TenantId, DisplayName = "Test Tenant" });
        _db.SaveChanges();

        _auditWriter = new InMemoryAuditEventWriter(
            NullLogger<InMemoryAuditEventWriter>.Instance);
        _secretProvider = new InMemorySecretProvider();
        _syncJobPublisher = new TestSyncJobPublisher();

        _service = new ConnectorAdminService(
            _db,
            _auditWriter,
            Array.Empty<IConnectorClient>(),
            Array.Empty<IWebhookManager>(),
            _syncJobPublisher,
            new WebhookSettings(),
            NullLogger<ConnectorAdminService>.Instance,
            _secretProvider);
    }

    public void Dispose()
    {
        _db.Dispose();
        _sp.Dispose();
        _connection.Dispose();
    }

    private CreateConnectorRequest MakeCreateRequest(string name = "Test Connector") => new()
    {
        Name = name,
        ConnectorType = ConnectorType.AzureDevOps,
        AuthType = SecretAuthType.Pat,
        KeyVaultSecretName = "test-secret",
        SourceConfig = """{"organizationUrl":"https://dev.azure.com/test"}""",
    };

    private FieldMappingConfig MakeValidFieldMapping() => new()
    {
        Rules =
        [
            new FieldMappingRule { SourceField = "title", TargetField = "Title" },
            new FieldMappingRule { SourceField = "description", TargetField = "TextContent" },
            new FieldMappingRule { SourceField = "type", TargetField = "SourceType" },
        ],
    };

    // --- Create ---

    [Fact]
    public async Task Create_ValidRequest_ReturnsConnectorResponse()
    {
        var (response, validation) = await _service.CreateAsync(TenantId, ActorId, CorrelationId, MakeCreateRequest());

        Assert.NotNull(response);
        Assert.Null(validation);
        Assert.Equal("Test Connector", response.Name);
        Assert.Equal(ConnectorType.AzureDevOps, response.ConnectorType);
        Assert.Equal(ConnectorStatus.Disabled, response.Status);
        Assert.Equal(SecretAuthType.Pat, response.AuthType);
        Assert.True(response.HasSecret);
    }

    [Fact]
    public async Task Create_EmptyName_ReturnsValidationError()
    {
        var request = MakeCreateRequest() with { Name = "" };

        var (response, validation) = await _service.CreateAsync(TenantId, ActorId, CorrelationId, request);

        Assert.Null(response);
        Assert.NotNull(validation);
        Assert.False(validation.IsValid);
        Assert.Contains(validation.Errors, e => e.Contains("Name is required"));
    }

    [Fact]
    public async Task Create_NameTooLong_ReturnsValidationError()
    {
        var request = MakeCreateRequest() with { Name = new string('x', 257) };

        var (response, validation) = await _service.CreateAsync(TenantId, ActorId, CorrelationId, request);

        Assert.Null(response);
        Assert.NotNull(validation);
        Assert.False(validation.IsValid);
        Assert.Contains(validation.Errors, e => e.Contains("256"));
    }

    [Fact]
    public async Task Create_DuplicateName_ReturnsValidationError()
    {
        await _service.CreateAsync(TenantId, ActorId, CorrelationId, MakeCreateRequest("Dup"));

        var (response, validation) = await _service.CreateAsync(TenantId, ActorId, CorrelationId, MakeCreateRequest("Dup"));

        Assert.Null(response);
        Assert.NotNull(validation);
        Assert.Contains(validation.Errors, e => e.Contains("already exists"));
    }

    [Fact]
    public async Task Create_WritesAuditEvent()
    {
        await _service.CreateAsync(TenantId, ActorId, CorrelationId, MakeCreateRequest());

        Assert.Contains(_auditWriter.GetEvents(), e => e.EventType == AuditEventTypes.ConnectorCreated);
    }

    // --- List ---

    [Fact]
    public async Task List_Empty_ReturnsEmptyList()
    {
        var result = await _service.ListAsync(TenantId);

        Assert.Empty(result.Connectors);
        Assert.Equal(0, result.TotalCount);
    }

    [Fact]
    public async Task List_ReturnsConnectorsForTenant()
    {
        await _service.CreateAsync(TenantId, ActorId, CorrelationId, MakeCreateRequest("A"));
        await _service.CreateAsync(TenantId, ActorId, CorrelationId, MakeCreateRequest("B"));

        var result = await _service.ListAsync(TenantId);

        Assert.Equal(2, result.TotalCount);
        Assert.Equal(2, result.Connectors.Count);
    }

    [Fact]
    public async Task List_TenantIsolation_DoesNotLeakCrossTenant()
    {
        _db.Tenants.Add(new TenantEntity { TenantId = "other-tenant", DisplayName = "Other" });
        _db.SaveChanges();

        await _service.CreateAsync(TenantId, ActorId, CorrelationId, MakeCreateRequest("Mine"));
        await _service.CreateAsync("other-tenant", ActorId, CorrelationId, MakeCreateRequest("Theirs"));

        var result = await _service.ListAsync(TenantId);

        Assert.Single(result.Connectors);
        Assert.Equal("Mine", result.Connectors[0].Name);
    }

    // --- Get ---

    [Fact]
    public async Task Get_ExistingConnector_ReturnsResponse()
    {
        var (created, _) = await _service.CreateAsync(TenantId, ActorId, CorrelationId, MakeCreateRequest());

        var result = await _service.GetAsync(TenantId, created!.Id);

        Assert.NotNull(result);
        Assert.Equal(created.Id, result.Id);
    }

    [Fact]
    public async Task Get_NonExistentId_ReturnsNull()
    {
        var result = await _service.GetAsync(TenantId, Guid.NewGuid());

        Assert.Null(result);
    }

    [Fact]
    public async Task Get_WrongTenant_ReturnsNull()
    {
        var (created, _) = await _service.CreateAsync(TenantId, ActorId, CorrelationId, MakeCreateRequest());

        var result = await _service.GetAsync("wrong-tenant", created!.Id);

        Assert.Null(result);
    }

    // --- Update ---

    [Fact]
    public async Task Update_ChangesName()
    {
        var (created, _) = await _service.CreateAsync(TenantId, ActorId, CorrelationId, MakeCreateRequest());
        var updateReq = new UpdateConnectorRequest { Name = "New Name" };

        var (response, validation, notFound) = await _service.UpdateAsync(
            TenantId, ActorId, CorrelationId, created!.Id, updateReq);

        Assert.False(notFound);
        Assert.Null(validation);
        Assert.NotNull(response);
        Assert.Equal("New Name", response.Name);
    }

    [Fact]
    public async Task Update_DuplicateName_ReturnsValidationError()
    {
        await _service.CreateAsync(TenantId, ActorId, CorrelationId, MakeCreateRequest("A"));
        var (createdB, _) = await _service.CreateAsync(TenantId, ActorId, CorrelationId, MakeCreateRequest("B"));

        var updateReq = new UpdateConnectorRequest { Name = "A" };
        var (response, validation, notFound) = await _service.UpdateAsync(
            TenantId, ActorId, CorrelationId, createdB!.Id, updateReq);

        Assert.False(notFound);
        Assert.Null(response);
        Assert.NotNull(validation);
        Assert.Contains(validation.Errors, e => e.Contains("already exists"));
    }

    [Fact]
    public async Task Update_NonExistentId_ReturnsNotFound()
    {
        var (_, _, notFound) = await _service.UpdateAsync(
            TenantId, ActorId, CorrelationId, Guid.NewGuid(), new UpdateConnectorRequest { Name = "X" });

        Assert.True(notFound);
    }

    [Fact]
    public async Task Update_WritesAuditEvent()
    {
        var (created, _) = await _service.CreateAsync(TenantId, ActorId, CorrelationId, MakeCreateRequest());

        await _service.UpdateAsync(TenantId, ActorId, CorrelationId, created!.Id,
            new UpdateConnectorRequest { Name = "Updated" });

        Assert.Contains(_auditWriter.GetEvents(), e => e.EventType == AuditEventTypes.ConnectorUpdated);
    }

    // --- Delete ---

    [Fact]
    public async Task Delete_ExistingConnector_SoftDeletes()
    {
        var (created, _) = await _service.CreateAsync(TenantId, ActorId, CorrelationId, MakeCreateRequest());

        var found = await _service.DeleteAsync(TenantId, ActorId, CorrelationId, created!.Id);

        Assert.True(found);

        // Soft-deleted connector should not appear in list.
        var list = await _service.ListAsync(TenantId);
        Assert.Empty(list.Connectors);
    }

    [Fact]
    public async Task Delete_NonExistentId_ReturnsFalse()
    {
        var found = await _service.DeleteAsync(TenantId, ActorId, CorrelationId, Guid.NewGuid());

        Assert.False(found);
    }

    [Fact]
    public async Task Delete_WritesAuditEvent()
    {
        var (created, _) = await _service.CreateAsync(TenantId, ActorId, CorrelationId, MakeCreateRequest());

        await _service.DeleteAsync(TenantId, ActorId, CorrelationId, created!.Id);

        Assert.Contains(_auditWriter.GetEvents(), e => e.EventType == AuditEventTypes.ConnectorDeleted);
    }

    // --- Enable ---

    [Fact]
    public async Task Enable_WithValidMapping_SetsStatusToEnabled()
    {
        var request = MakeCreateRequest() with { FieldMapping = MakeValidFieldMapping() };
        var (created, _) = await _service.CreateAsync(TenantId, ActorId, CorrelationId, request);

        var (found, validation, response) = await _service.EnableAsync(
            TenantId, ActorId, CorrelationId, created!.Id);

        Assert.True(found);
        Assert.Null(validation);
        Assert.NotNull(response);
        Assert.Equal(ConnectorStatus.Enabled, response.Status);
    }

    [Fact]
    public async Task Enable_WithoutMapping_ReturnsValidationError()
    {
        var (created, _) = await _service.CreateAsync(TenantId, ActorId, CorrelationId, MakeCreateRequest());

        var (found, validation, response) = await _service.EnableAsync(
            TenantId, ActorId, CorrelationId, created!.Id);

        Assert.True(found);
        Assert.NotNull(validation);
        Assert.False(validation.IsValid);
        Assert.Null(response);
    }

    [Fact]
    public async Task Enable_MissingRequiredFields_ReturnsValidationError()
    {
        var partialMapping = new FieldMappingConfig
        {
            Rules = [new FieldMappingRule { SourceField = "title", TargetField = "Title" }],
        };
        var request = MakeCreateRequest() with { FieldMapping = partialMapping };
        var (created, _) = await _service.CreateAsync(TenantId, ActorId, CorrelationId, request);

        var (found, validation, _) = await _service.EnableAsync(
            TenantId, ActorId, CorrelationId, created!.Id);

        Assert.True(found);
        Assert.NotNull(validation);
        Assert.False(validation.IsValid);
        Assert.Contains(validation.Errors, e => e.Contains("TextContent") || e.Contains("SourceType"));
    }

    [Fact]
    public async Task Enable_NonExistentId_ReturnsNotFound()
    {
        var (found, _, _) = await _service.EnableAsync(
            TenantId, ActorId, CorrelationId, Guid.NewGuid());

        Assert.False(found);
    }

    // --- Disable ---

    [Fact]
    public async Task Disable_EnabledConnector_SetsStatusToDisabled()
    {
        var request = MakeCreateRequest() with { FieldMapping = MakeValidFieldMapping() };
        var (created, _) = await _service.CreateAsync(TenantId, ActorId, CorrelationId, request);
        await _service.EnableAsync(TenantId, ActorId, CorrelationId, created!.Id);

        var (found, response) = await _service.DisableAsync(TenantId, ActorId, CorrelationId, created.Id);

        Assert.True(found);
        Assert.NotNull(response);
        Assert.Equal(ConnectorStatus.Disabled, response.Status);
    }

    [Fact]
    public async Task Disable_NonExistentId_ReturnsNotFound()
    {
        var (found, _) = await _service.DisableAsync(TenantId, ActorId, CorrelationId, Guid.NewGuid());

        Assert.False(found);
    }

    [Fact]
    public async Task Disable_WritesAuditEvent()
    {
        var request = MakeCreateRequest() with { FieldMapping = MakeValidFieldMapping() };
        var (created, _) = await _service.CreateAsync(TenantId, ActorId, CorrelationId, request);
        await _service.EnableAsync(TenantId, ActorId, CorrelationId, created!.Id);

        await _service.DisableAsync(TenantId, ActorId, CorrelationId, created.Id);

        Assert.Contains(_auditWriter.GetEvents(), e => e.EventType == AuditEventTypes.ConnectorDisabled);
    }

    // --- TestConnection ---

    [Fact]
    public async Task TestConnection_NoClientRegistered_ReturnsFailed()
    {
        var (created, _) = await _service.CreateAsync(TenantId, ActorId, CorrelationId, MakeCreateRequest());

        var result = await _service.TestConnectionAsync(TenantId, ActorId, CorrelationId, created!.Id);

        Assert.NotNull(result);
        Assert.False(result.Success);
        Assert.Contains("No connector client", result.Message);
    }

    [Fact]
    public async Task TestConnection_NonExistentId_ReturnsNull()
    {
        var result = await _service.TestConnectionAsync(TenantId, ActorId, CorrelationId, Guid.NewGuid());

        Assert.Null(result);
    }

    [Fact]
    public async Task TestConnection_WithClient_DelegatesAndReturnsResult()
    {
        var stubClient = new StubConnectorClient(ConnectorType.AzureDevOps,
            testResult: new TestConnectionResponse { Success = true, Message = "OK" });

        var service = new ConnectorAdminService(
            _db, _auditWriter,
            new[] { stubClient },
            Array.Empty<IWebhookManager>(),
            _syncJobPublisher,
            new WebhookSettings(),
            NullLogger<ConnectorAdminService>.Instance,
            _secretProvider);

        _secretProvider.Secrets["test-secret"] = "pat-value";
        var (created, _) = await service.CreateAsync(TenantId, ActorId, CorrelationId, MakeCreateRequest());

        var result = await service.TestConnectionAsync(TenantId, ActorId, CorrelationId, created!.Id);

        Assert.NotNull(result);
        Assert.True(result.Success);
        Assert.Equal("OK", result.Message);
    }

    // --- SyncNow ---

    [Fact]
    public async Task SyncNow_PublishesSyncJobMessage()
    {
        var (created, _) = await _service.CreateAsync(TenantId, ActorId, CorrelationId, MakeCreateRequest());

        var (syncRunId, notFound) = await _service.SyncNowAsync(
            TenantId, ActorId, CorrelationId, created!.Id, new SyncNowRequest { IsBackfill = true });

        Assert.False(notFound);
        Assert.NotNull(syncRunId);

        Assert.Single(_syncJobPublisher.PublishedMessages);
        var msg = _syncJobPublisher.PublishedMessages[0];
        Assert.Equal(created.Id, msg.ConnectorId);
        Assert.True(msg.IsBackfill);
        Assert.Equal(TenantId, msg.TenantId);
    }

    [Fact]
    public async Task SyncNow_NonExistentId_ReturnsNotFound()
    {
        var (syncRunId, notFound) = await _service.SyncNowAsync(
            TenantId, ActorId, CorrelationId, Guid.NewGuid(), new SyncNowRequest());

        Assert.True(notFound);
        Assert.Null(syncRunId);
    }

    [Fact]
    public async Task SyncNow_IncrementalWithCheckpoint_PassesLastCheckpoint()
    {
        var (created, _) = await _service.CreateAsync(TenantId, ActorId, CorrelationId, MakeCreateRequest());

        // Seed a completed sync run with a checkpoint.
        var completedRun = new SyncRunEntity
        {
            Id = Guid.NewGuid(),
            ConnectorId = created!.Id,
            TenantId = TenantId,
            Status = SyncRunStatus.Completed,
            IsBackfill = false,
            StartedAt = DateTimeOffset.UtcNow.AddHours(-1),
            CompletedAt = DateTimeOffset.UtcNow.AddMinutes(-30),
            Checkpoint = """{"lastModified":"2026-01-01"}""",
        };
        _db.SyncRuns.Add(completedRun);
        await _db.SaveChangesAsync();

        // Re-fetch to include sync runs. Clear the change tracker first.
        _db.ChangeTracker.Clear();

        var (syncRunId, _) = await _service.SyncNowAsync(
            TenantId, ActorId, CorrelationId, created.Id, new SyncNowRequest { IsBackfill = false });

        Assert.NotNull(syncRunId);
        var msg = _syncJobPublisher.PublishedMessages[0];
        Assert.Equal("""{"lastModified":"2026-01-01"}""", msg.Checkpoint);
    }

    // --- ListSyncRuns ---

    [Fact]
    public async Task ListSyncRuns_ReturnsRunsForConnector()
    {
        var (created, _) = await _service.CreateAsync(TenantId, ActorId, CorrelationId, MakeCreateRequest());

        _db.SyncRuns.Add(new SyncRunEntity
        {
            Id = Guid.NewGuid(),
            ConnectorId = created!.Id,
            TenantId = TenantId,
            Status = SyncRunStatus.Completed,
            StartedAt = DateTimeOffset.UtcNow,
        });
        await _db.SaveChangesAsync();

        var result = await _service.ListSyncRunsAsync(TenantId, created.Id);

        Assert.NotNull(result);
        Assert.Single(result.SyncRuns);
    }

    [Fact]
    public async Task ListSyncRuns_NonExistentConnector_ReturnsNull()
    {
        var result = await _service.ListSyncRunsAsync(TenantId, Guid.NewGuid());

        Assert.Null(result);
    }

    // --- GetSyncRun ---

    [Fact]
    public async Task GetSyncRun_ExistingRun_ReturnsDetails()
    {
        var (created, _) = await _service.CreateAsync(TenantId, ActorId, CorrelationId, MakeCreateRequest());
        var runId = Guid.NewGuid();

        _db.SyncRuns.Add(new SyncRunEntity
        {
            Id = runId,
            ConnectorId = created!.Id,
            TenantId = TenantId,
            Status = SyncRunStatus.Failed,
            StartedAt = DateTimeOffset.UtcNow,
            ErrorDetail = "timeout",
        });
        await _db.SaveChangesAsync();

        var result = await _service.GetSyncRunAsync(TenantId, created.Id, runId);

        Assert.NotNull(result);
        Assert.Equal(SyncRunStatus.Failed, result.Status);
        Assert.Equal("timeout", result.ErrorDetail);
    }

    [Fact]
    public async Task GetSyncRun_NonExistent_ReturnsNull()
    {
        var (created, _) = await _service.CreateAsync(TenantId, ActorId, CorrelationId, MakeCreateRequest());

        var result = await _service.GetSyncRunAsync(TenantId, created!.Id, Guid.NewGuid());

        Assert.Null(result);
    }

    // --- ValidateFieldMapping ---

    [Fact]
    public void ValidateFieldMapping_NullMapping_ReturnsInvalid()
    {
        var result = _service.ValidateFieldMapping(null);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("at least one rule"));
    }

    [Fact]
    public void ValidateFieldMapping_EmptyRules_ReturnsInvalid()
    {
        var result = _service.ValidateFieldMapping(new FieldMappingConfig { Rules = [] });

        Assert.False(result.IsValid);
    }

    [Fact]
    public void ValidateFieldMapping_ValidMapping_ReturnsValid()
    {
        var result = _service.ValidateFieldMapping(MakeValidFieldMapping());

        Assert.True(result.IsValid);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void ValidateFieldMapping_EmptySourceField_ReturnsError()
    {
        var mapping = new FieldMappingConfig
        {
            Rules = [new FieldMappingRule { SourceField = "", TargetField = "Title" }],
        };

        var result = _service.ValidateFieldMapping(mapping);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("SourceField"));
    }

    [Fact]
    public void ValidateFieldMapping_RegexTransformWithoutExpression_ReturnsError()
    {
        var mapping = new FieldMappingConfig
        {
            Rules =
            [
                new FieldMappingRule
                {
                    SourceField = "x",
                    TargetField = "Title",
                    Transform = FieldTransformType.Regex,
                    TransformExpression = null,
                },
            ],
        };

        var result = _service.ValidateFieldMapping(mapping);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("Regex") && e.Contains("TransformExpression"));
    }

    [Fact]
    public void ValidateFieldMapping_IncludesMissingFieldAnalysis()
    {
        var mapping = new FieldMappingConfig
        {
            Rules = [new FieldMappingRule { SourceField = "x", TargetField = "Title" }],
        };

        var result = _service.ValidateFieldMapping(mapping);

        Assert.NotNull(result.MissingFieldAnalysis);
        Assert.Contains("TextContent", result.MissingFieldAnalysis.MissingRequiredFields);
        Assert.Contains("SourceType", result.MissingFieldAnalysis.MissingRequiredFields);
    }

    // --- Stub connector client ---

    private sealed class StubConnectorClient : IConnectorClient
    {
        private readonly TestConnectionResponse _testResult;

        public StubConnectorClient(ConnectorType type, TestConnectionResponse testResult)
        {
            Type = type;
            _testResult = testResult;
        }

        public ConnectorType Type { get; }

        public Task<TestConnectionResponse> TestConnectionAsync(
            string tenantId, string? sourceConfig, string? secretValue, CancellationToken ct = default)
            => Task.FromResult(_testResult);

        public Task<IReadOnlyList<CanonicalRecord>> PreviewAsync(
            string tenantId, string? sourceConfig, FieldMappingConfig? fieldMapping,
            string? secretValue, int sampleSize, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<CanonicalRecord>>(Array.Empty<CanonicalRecord>());

        public Task<FetchResult> FetchAsync(
            string tenantId, string? sourceConfig, FieldMappingConfig? fieldMapping,
            string? secretValue, string? checkpoint, bool isBackfill, CancellationToken ct = default)
            => Task.FromResult(new FetchResult { Records = [], FailedRecords = 0, Errors = [] });
    }
}
