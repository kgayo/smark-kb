using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SmartKb.Contracts;
using SmartKb.Contracts.Configuration;
using SmartKb.Contracts.Enums;
using SmartKb.Contracts.Models;
using SmartKb.Contracts.Services;
using SmartKb.Data;
using SmartKb.Data.Entities;

namespace SmartKb.Api.Connectors;

public sealed class ConnectorAdminService
{
    // Required canonical fields that must be mapped for sync activation.
    private static readonly HashSet<string> RequiredTargetFields =
    [
        nameof(CanonicalRecord.Title),
        nameof(CanonicalRecord.TextContent),
        nameof(CanonicalRecord.SourceType),
    ];

    private readonly SmartKbDbContext _db;
    private readonly IAuditEventWriter _auditWriter;
    private readonly ISecretProvider? _secretProvider;
    private readonly IOAuthTokenService? _oauthTokenService;
    private readonly IEnumerable<IConnectorClient> _connectorClients;
    private readonly IEnumerable<IWebhookManager> _webhookManagers;
    private readonly ISyncJobPublisher _syncJobPublisher;
    private readonly WebhookSettings _webhookSettings;
    private readonly ILogger<ConnectorAdminService> _logger;

    public ConnectorAdminService(
        SmartKbDbContext db,
        IAuditEventWriter auditWriter,
        IEnumerable<IConnectorClient> connectorClients,
        IEnumerable<IWebhookManager> webhookManagers,
        ISyncJobPublisher syncJobPublisher,
        WebhookSettings webhookSettings,
        ILogger<ConnectorAdminService> logger,
        ISecretProvider? secretProvider = null,
        IOAuthTokenService? oauthTokenService = null)
    {
        _db = db;
        _auditWriter = auditWriter;
        _secretProvider = secretProvider;
        _oauthTokenService = oauthTokenService;
        _connectorClients = connectorClients;
        _webhookManagers = webhookManagers;
        _syncJobPublisher = syncJobPublisher;
        _webhookSettings = webhookSettings;
        _logger = logger;
    }

    public async Task<ConnectorListResponse> ListAsync(string tenantId, CancellationToken ct = default)
    {
        var connectors = await _db.Connectors
            .Where(c => c.TenantId == tenantId)
            .Include(c => c.SyncRuns)
            .ToListAsync(ct);

        // Sort client-side (per-tenant list is small; avoids DateTimeOffset ordering issues across providers).
        var sorted = connectors.OrderByDescending(c => c.UpdatedAt).ToList();

        return new ConnectorListResponse
        {
            Connectors = sorted.Select(ToResponse).ToList(),
            TotalCount = connectors.Count,
        };
    }

    public async Task<ConnectorResponse?> GetAsync(string tenantId, Guid connectorId, CancellationToken ct = default)
    {
        var entity = await FindConnectorAsync(tenantId, connectorId, ct);
        return entity is null ? null : ToResponse(entity);
    }

    public async Task<(ConnectorResponse? Response, ConnectorValidationResult? Validation)> CreateAsync(
        string tenantId, string actorId, string correlationId,
        CreateConnectorRequest request, CancellationToken ct = default)
    {
        var validation = ValidateCreateRequest(request);
        if (!validation.IsValid)
            return (null, validation);

        // Check for duplicate name within tenant.
        var exists = await _db.Connectors
            .AnyAsync(c => c.TenantId == tenantId && c.Name == request.Name, ct);
        if (exists)
            return (null, ConnectorValidationResult.Invalid($"A connector named '{request.Name}' already exists for this tenant."));

        var now = DateTimeOffset.UtcNow;
        var entity = new ConnectorEntity
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Name = request.Name,
            ConnectorType = request.ConnectorType,
            Status = ConnectorStatus.Disabled, // New connectors start disabled.
            AuthType = request.AuthType,
            KeyVaultSecretName = request.KeyVaultSecretName,
            SourceConfig = request.SourceConfig,
            FieldMapping = request.FieldMapping is not null
                ? JsonSerializer.Serialize(request.FieldMapping, SharedJsonOptions.CamelCaseCompact)
                : null,
            ScheduleCron = request.ScheduleCron,
            CreatedAt = now,
            UpdatedAt = now,
        };

        _db.Connectors.Add(entity);
        await _db.SaveChangesAsync(ct);

        await WriteAuditAsync(tenantId, actorId, correlationId, AuditEventTypes.ConnectorCreated,
            $"Connector '{entity.Name}' (id={entity.Id}, type={entity.ConnectorType}) created.", ct);

        return (ToResponse(entity), null);
    }

    public async Task<(ConnectorResponse? Response, ConnectorValidationResult? Validation, bool NotFound)> UpdateAsync(
        string tenantId, string actorId, string correlationId,
        Guid connectorId, UpdateConnectorRequest request, CancellationToken ct = default)
    {
        var entity = await FindConnectorAsync(tenantId, connectorId, ct);
        if (entity is null)
            return (null, null, true);

        // Check name uniqueness if changing name.
        if (request.Name is not null && request.Name != entity.Name)
        {
            var nameExists = await _db.Connectors
                .AnyAsync(c => c.TenantId == tenantId && c.Name == request.Name && c.Id != connectorId, ct);
            if (nameExists)
                return (null, ConnectorValidationResult.Invalid($"A connector named '{request.Name}' already exists for this tenant."), false);
            entity.Name = request.Name;
        }

        if (request.SourceConfig is not null) entity.SourceConfig = request.SourceConfig;
        if (request.FieldMapping is not null)
            entity.FieldMapping = JsonSerializer.Serialize(request.FieldMapping, SharedJsonOptions.CamelCaseCompact);
        if (request.ScheduleCron is not null) entity.ScheduleCron = request.ScheduleCron;
        if (request.AuthType.HasValue) entity.AuthType = request.AuthType.Value;
        if (request.KeyVaultSecretName is not null) entity.KeyVaultSecretName = request.KeyVaultSecretName;

        entity.UpdatedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(ct);

        await WriteAuditAsync(tenantId, actorId, correlationId, AuditEventTypes.ConnectorUpdated,
            $"Connector '{entity.Name}' (id={entity.Id}) updated.", ct);

        return (ToResponse(entity), null, false);
    }

    public async Task<bool> DeleteAsync(
        string tenantId, string actorId, string correlationId,
        Guid connectorId, CancellationToken ct = default)
    {
        var entity = await FindConnectorAsync(tenantId, connectorId, ct);
        if (entity is null) return false;

        entity.DeletedAt = DateTimeOffset.UtcNow;
        entity.UpdatedAt = entity.DeletedAt.Value;
        await _db.SaveChangesAsync(ct);

        await WriteAuditAsync(tenantId, actorId, correlationId, AuditEventTypes.ConnectorDeleted,
            $"Connector '{entity.Name}' (id={entity.Id}) soft-deleted.", ct);

        return true;
    }

    public async Task<(bool Found, ConnectorResponse? Response)> DisableAsync(
        string tenantId, string actorId, string correlationId,
        Guid connectorId, CancellationToken ct = default)
    {
        var entity = await FindConnectorAsync(tenantId, connectorId, ct);
        if (entity is null) return (false, null);

        // Deregister webhooks on disable.
        await DeregisterWebhooksAsync(entity, correlationId, ct);

        entity.Status = ConnectorStatus.Disabled;
        entity.UpdatedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(ct);

        await WriteAuditAsync(tenantId, actorId, correlationId, AuditEventTypes.ConnectorDisabled,
            $"Connector '{entity.Name}' (id={entity.Id}) disabled.", ct);

        return (true, ToResponse(entity));
    }

    public async Task<(bool Found, ConnectorValidationResult? Validation, ConnectorResponse? Response)> EnableAsync(
        string tenantId, string actorId, string correlationId,
        Guid connectorId, CancellationToken ct = default)
    {
        var entity = await FindConnectorAsync(tenantId, connectorId, ct);
        if (entity is null) return (false, null, null);

        var validation = ValidateFieldMappingForActivation(entity.FieldMapping);
        if (!validation.IsValid)
            return (true, validation, null);

        entity.Status = ConnectorStatus.Enabled;
        entity.UpdatedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(ct);

        // Register webhooks on enable.
        await RegisterWebhooksAsync(entity, correlationId, ct);

        await WriteAuditAsync(tenantId, actorId, correlationId, AuditEventTypes.ConnectorEnabled,
            $"Connector '{entity.Name}' (id={entity.Id}) enabled.", ct);

        return (true, null, ToResponse(entity));
    }

    public async Task<TestConnectionResponse?> TestConnectionAsync(
        string tenantId, string actorId, string correlationId,
        Guid connectorId, CancellationToken ct = default)
    {
        var entity = await FindConnectorAsync(tenantId, connectorId, ct);
        if (entity is null) return null;

        var client = _connectorClients.FirstOrDefault(c => c.Type == entity.ConnectorType);

        TestConnectionResponse result;
        if (client is null)
        {
            result = new TestConnectionResponse
            {
                Success = false,
                Message = $"No connector client registered for type '{entity.ConnectorType}'.",
            };
        }
        else
        {
            var (secretValue, secretError) = await ResolveSecretValueAsync(entity, ct);
            if (secretError is not null)
            {
                result = new TestConnectionResponse
                {
                    Success = false,
                    Message = "Failed to retrieve credentials.",
                    DiagnosticDetail = secretError,
                };
                await WriteAuditAsync(tenantId, actorId, correlationId, AuditEventTypes.ConnectorTestFailed,
                    $"Connector '{entity.Name}' (id={entity.Id}) test failed: credential retrieval error.", ct);
                return result;
            }

            result = await client.TestConnectionAsync(tenantId, entity.SourceConfig, secretValue, ct);
        }

        var eventType = result.Success ? AuditEventTypes.ConnectorTestPassed : AuditEventTypes.ConnectorTestFailed;
        await WriteAuditAsync(tenantId, actorId, correlationId, eventType,
            $"Connector '{entity.Name}' (id={entity.Id}) test: {result.Message}", ct);

        return result;
    }

    public async Task<(Guid? SyncRunId, bool NotFound)> SyncNowAsync(
        string tenantId, string actorId, string correlationId,
        Guid connectorId, SyncNowRequest request, CancellationToken ct = default)
    {
        var entity = await FindConnectorAsync(tenantId, connectorId, ct);
        if (entity is null) return (null, true);

        // Retrieve last checkpoint for incremental syncs.
        string? lastCheckpoint = null;
        if (!request.IsBackfill)
        {
            var lastCompleted = entity.SyncRuns?
                .Where(r => r.Status == SyncRunStatus.Completed && r.Checkpoint is not null)
                .OrderByDescending(r => r.CompletedAt)
                .FirstOrDefault();
            lastCheckpoint = lastCompleted?.Checkpoint;
        }

        var syncRun = new SyncRunEntity
        {
            Id = Guid.NewGuid(),
            ConnectorId = entity.Id,
            TenantId = tenantId,
            Status = SyncRunStatus.Pending,
            IsBackfill = request.IsBackfill,
            StartedAt = DateTimeOffset.UtcNow,
            IdempotencyKey = request.IdempotencyKey ?? Guid.NewGuid().ToString(),
        };

        _db.SyncRuns.Add(syncRun);
        await _db.SaveChangesAsync(ct);

        var message = new SyncJobMessage
        {
            SyncRunId = syncRun.Id,
            ConnectorId = entity.Id,
            TenantId = tenantId,
            ConnectorType = entity.ConnectorType,
            IsBackfill = request.IsBackfill,
            SourceConfig = entity.SourceConfig,
            FieldMapping = entity.FieldMapping,
            KeyVaultSecretName = entity.KeyVaultSecretName,
            AuthType = entity.AuthType,
            Checkpoint = lastCheckpoint,
            CorrelationId = correlationId,
            EnqueuedAt = DateTimeOffset.UtcNow,
        };

        await _syncJobPublisher.PublishAsync(message, ct);

        await WriteAuditAsync(tenantId, actorId, correlationId, AuditEventTypes.ConnectorSyncTriggered,
            $"Sync triggered for connector '{entity.Name}' (id={entity.Id}, runId={syncRun.Id}, backfill={request.IsBackfill}).", ct);

        return (syncRun.Id, false);
    }

    public async Task<PreviewResponse?> PreviewAsync(
        string tenantId, string actorId, string correlationId,
        Guid connectorId, PreviewRequest request, CancellationToken ct = default)
    {
        var entity = await FindConnectorAsync(tenantId, connectorId, ct);
        if (entity is null) return null;

        var client = _connectorClients.FirstOrDefault(c => c.Type == entity.ConnectorType);
        if (client is null)
        {
            return new PreviewResponse
            {
                Records = [],
                ValidationErrors = [$"No connector client registered for type '{entity.ConnectorType}'."],
            };
        }

        var (secretValue, secretError) = await ResolveSecretValueAsync(entity, ct);
        if (secretError is not null)
        {
            return new PreviewResponse
            {
                Records = [],
                ValidationErrors = [$"Failed to retrieve credentials: {secretError}"],
            };
        }

        var mapping = request.FieldMapping ?? DeserializeFieldMapping(entity.FieldMapping, _logger);
        var records = await client.PreviewAsync(tenantId, entity.SourceConfig, mapping, secretValue, request.SampleSize, ct);

        // Validate preview records against required fields.
        var errors = new List<string>();
        foreach (var record in records)
        {
            if (string.IsNullOrWhiteSpace(record.Title))
                errors.Add($"Record '{record.EvidenceId}': missing required field 'Title'.");
            if (string.IsNullOrWhiteSpace(record.TextContent))
                errors.Add($"Record '{record.EvidenceId}': missing required field 'TextContent'.");
        }

        await WriteAuditAsync(tenantId, actorId, correlationId, AuditEventTypes.ConnectorPreview,
            $"Preview for connector '{entity.Name}' (id={entity.Id}): {records.Count} records, {errors.Count} errors.", ct);

        return new PreviewResponse { Records = records.ToList(), ValidationErrors = errors };
    }

    public async Task<SyncRunListResponse?> ListSyncRunsAsync(
        string tenantId, Guid connectorId, CancellationToken ct = default)
    {
        var connectorExists = await _db.Connectors
            .AnyAsync(c => c.TenantId == tenantId && c.Id == connectorId, ct);
        if (!connectorExists) return null;

        var runs = await _db.SyncRuns
            .Where(s => s.ConnectorId == connectorId && s.TenantId == tenantId)
            .ToListAsync(ct);

        runs = runs.OrderByDescending(s => s.StartedAt).ToList();

        return new SyncRunListResponse
        {
            SyncRuns = runs.Select(ToSyncRunSummary).ToList(),
            TotalCount = runs.Count,
        };
    }

    public async Task<SyncRunSummary?> GetSyncRunAsync(
        string tenantId, Guid connectorId, Guid syncRunId, CancellationToken ct = default)
    {
        var run = await _db.SyncRuns
            .FirstOrDefaultAsync(s => s.Id == syncRunId && s.ConnectorId == connectorId && s.TenantId == tenantId, ct);
        return run is null ? null : ToSyncRunSummary(run);
    }

    public async Task<PreviewRetrievalResponse?> PreviewRetrievalAsync(
        string tenantId, string actorId, string correlationId,
        Guid connectorId, PreviewRetrievalRequest request, CancellationToken ct = default)
    {
        var entity = await FindConnectorAsync(tenantId, connectorId, ct);
        if (entity is null) return null;

        // Count total chunks for this connector.
        var totalChunks = await _db.EvidenceChunks
            .CountAsync(c => c.ConnectorId == connectorId && c.TenantId == tenantId, ct);

        if (totalChunks == 0)
        {
            await WriteAuditAsync(tenantId, actorId, correlationId, AuditEventTypes.ConnectorPreviewRetrieval,
                $"Preview retrieval for connector '{entity.Name}' (id={entity.Id}): no chunks indexed.", ct);
            return new PreviewRetrievalResponse
            {
                Chunks = [],
                TotalChunksForConnector = 0,
                HasEvidence = false,
                Message = "No chunks have been indexed for this connector yet. Run a sync first.",
            };
        }

        // Simple text search against chunks for this connector using LIKE matching.
        var queryLower = request.Query.ToLower();
        var maxResults = Math.Clamp(request.MaxResults, 1, 20);

        var matchingChunks = (await _db.EvidenceChunks
            .Where(c => c.ConnectorId == connectorId && c.TenantId == tenantId)
            .Where(c => c.ChunkText.ToLower().Contains(queryLower)
                     || c.Title.ToLower().Contains(queryLower))
            .ToListAsync(ct))
            .OrderByDescending(c => c.UpdatedAt)
            .Take(maxResults)
            .ToList();

        var chunks = matchingChunks.Select(c => new PreviewRetrievalChunk
        {
            ChunkId = c.ChunkId,
            Title = c.Title,
            ChunkText = c.ChunkText.Length > 500 ? c.ChunkText[..500] + "..." : c.ChunkText,
            SourceType = c.SourceType,
            ProductArea = c.ProductArea,
            Score = 1.0, // Text-match score (no vector search in preview).
            UpdatedAt = c.UpdatedAt,
        }).ToList();

        await WriteAuditAsync(tenantId, actorId, correlationId, AuditEventTypes.ConnectorPreviewRetrieval,
            $"Preview retrieval for connector '{entity.Name}' (id={entity.Id}): query='{request.Query}', {chunks.Count} results from {totalChunks} total chunks.", ct);

        return new PreviewRetrievalResponse
        {
            Chunks = chunks,
            TotalChunksForConnector = totalChunks,
            HasEvidence = chunks.Count > 0,
            Message = chunks.Count == 0
                ? $"No chunks matched the query '{request.Query}'. Try a different search term."
                : null,
        };
    }

    public ConnectorValidationResult ValidateFieldMapping(FieldMappingConfig? mapping)
    {
        if (mapping is null || mapping.Rules.Count == 0)
            return ConnectorValidationResult.Invalid("Field mapping must contain at least one rule.");

        var errors = new List<string>();
        var targetFields = new HashSet<string>();

        foreach (var rule in mapping.Rules)
        {
            if (string.IsNullOrWhiteSpace(rule.SourceField))
                errors.Add("SourceField must not be empty.");
            if (string.IsNullOrWhiteSpace(rule.TargetField))
                errors.Add("TargetField must not be empty.");

            if (rule.Transform == FieldTransformType.Regex && string.IsNullOrWhiteSpace(rule.TransformExpression))
                errors.Add($"Rule '{rule.SourceField}' → '{rule.TargetField}': Regex transform requires a TransformExpression.");

            if (rule.Transform == FieldTransformType.Template && string.IsNullOrWhiteSpace(rule.TransformExpression))
                errors.Add($"Rule '{rule.SourceField}' → '{rule.TargetField}': Template transform requires a TransformExpression.");

            if (!string.IsNullOrWhiteSpace(rule.RoutingTag) && !RoutingTagNames.All.Contains(rule.RoutingTag))
                errors.Add($"Rule '{rule.SourceField}' → '{rule.TargetField}': RoutingTag '{rule.RoutingTag}' is not valid. Allowed values: {string.Join(", ", RoutingTagNames.All)}.");

            if (!string.IsNullOrWhiteSpace(rule.TargetField))
                targetFields.Add(rule.TargetField);
        }

        var analysis = BuildMissingFieldAnalysis(targetFields);

        if (errors.Count > 0)
            return ConnectorValidationResult.Invalid(errors, analysis);

        return ConnectorValidationResult.Valid(analysis);
    }

    private static MissingFieldAnalysis BuildMissingFieldAnalysis(IReadOnlySet<string> mappedTargetFields)
    {
        // All canonical fields that can be mapped (required + optional well-known fields).
        var allFields = new (string Name, bool IsRequired)[]
        {
            (nameof(CanonicalRecord.Title), true),
            (nameof(CanonicalRecord.TextContent), true),
            (nameof(CanonicalRecord.SourceType), true),
            (nameof(CanonicalRecord.ProductArea), false),
            (nameof(CanonicalRecord.Tags), false),
            (nameof(CanonicalRecord.Severity), false),
            (nameof(CanonicalRecord.Author), false),
            (nameof(CanonicalRecord.Status), false),
        };

        var coverage = allFields.Select(f => new FieldCoverage
        {
            FieldName = f.Name,
            IsMapped = mappedTargetFields.Contains(f.Name),
            IsRequired = f.IsRequired,
        }).ToList();

        var missingRequired = coverage
            .Where(f => f.IsRequired && !f.IsMapped)
            .Select(f => f.FieldName)
            .ToList();

        return new MissingFieldAnalysis
        {
            MissingRequiredFields = missingRequired,
            FieldCoverage = coverage,
        };
    }

    public async Task<OAuthAuthorizeUrlResponse?> GetOAuthAuthorizeUrlAsync(
        string tenantId, Guid connectorId, CancellationToken ct = default)
    {
        if (_oauthTokenService is null)
            return null;

        var entity = await FindConnectorAsync(tenantId, connectorId, ct);
        if (entity is null) return null;

        if (entity.AuthType != SecretAuthType.OAuth)
            return null;

        var url = _oauthTokenService.BuildAuthorizeUrl(
            entity.ConnectorType, entity.Id, tenantId, entity.SourceConfig);

        return new OAuthAuthorizeUrlResponse { AuthorizeUrl = url };
    }

    public async Task<(OAuthCallbackResponse? Response, bool NotFound, bool InvalidState)> HandleOAuthCallbackAsync(
        string tenantId, string actorId, string correlationId,
        Guid connectorId, string code, string state, CancellationToken ct = default)
    {
        if (_oauthTokenService is null)
            return (new OAuthCallbackResponse { Success = false, Message = "OAuth is not configured." }, false, false);

        var entity = await FindConnectorAsync(tenantId, connectorId, ct);
        if (entity is null) return (null, true, false);

        if (!_oauthTokenService.ValidateState(state, connectorId, tenantId))
        {
            _logger.LogWarning("Invalid OAuth state parameter for connector {ConnectorId}.", connectorId);
            await WriteAuditAsync(tenantId, actorId, correlationId, AuditEventTypes.ConnectorOAuthFailed,
                $"OAuth callback for connector '{entity.Name}' (id={entity.Id}) failed: invalid state parameter.", ct);
            return (null, false, true);
        }

        if (string.IsNullOrEmpty(entity.KeyVaultSecretName))
        {
            return (new OAuthCallbackResponse
            {
                Success = false,
                Message = "Connector has no Key Vault secret configured. Set KeyVaultSecretName with client_id and client_secret JSON first.",
            }, false, false);
        }

        var success = await _oauthTokenService.ExchangeCodeAsync(
            connectorId, tenantId, code, entity.KeyVaultSecretName,
            entity.SourceConfig, entity.ConnectorType, ct);

        if (success)
        {
            await WriteAuditAsync(tenantId, actorId, correlationId, AuditEventTypes.ConnectorOAuthCompleted,
                $"OAuth authorization completed for connector '{entity.Name}' (id={entity.Id}).", ct);
        }
        else
        {
            await WriteAuditAsync(tenantId, actorId, correlationId, AuditEventTypes.ConnectorOAuthFailed,
                $"OAuth token exchange failed for connector '{entity.Name}' (id={entity.Id}).", ct);
        }

        return (new OAuthCallbackResponse
        {
            Success = success,
            Message = success ? "OAuth authorization completed successfully." : "Failed to exchange authorization code for tokens.",
        }, false, false);
    }

    // --- Private helpers ---

    /// <summary>
    /// Resolves the usable secret value for a connector. For OAuth connectors, resolves and
    /// refreshes the access token via <see cref="IOAuthTokenService"/>. For other auth types,
    /// reads the raw secret from Key Vault.
    /// </summary>
    private async Task<(string? SecretValue, string? Error)> ResolveSecretValueAsync(
        ConnectorEntity entity, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(entity.KeyVaultSecretName))
            return (null, null);

        if (entity.AuthType == SecretAuthType.OAuth && _oauthTokenService is not null)
        {
            try
            {
                var accessToken = await _oauthTokenService.ResolveAccessTokenAsync(
                    entity.KeyVaultSecretName, entity.SourceConfig, entity.ConnectorType, ct);
                return (accessToken, accessToken is null ? "OAuth token resolution failed." : null);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "OAuth token resolution failed for connector {ConnectorId}", entity.Id);
                return (null, $"OAuth token resolution error: {ex.Message}");
            }
        }

        if (_secretProvider is null)
            return (null, null);

        try
        {
            var secret = await _secretProvider.GetSecretAsync(entity.KeyVaultSecretName, ct);
            return (secret, null);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Secret retrieval failed for connector {ConnectorId}", entity.Id);
            return (null, ex.Message);
        }
    }

    private ConnectorValidationResult ValidateFieldMappingForActivation(string? fieldMappingJson)
    {
        var mapping = DeserializeFieldMapping(fieldMappingJson, _logger);
        if (mapping is null || mapping.Rules.Count == 0)
            return ConnectorValidationResult.Invalid("Field mapping is required before enabling a connector.");

        var mappedTargets = mapping.Rules
            .Select(r => r.TargetField)
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .ToHashSet();

        var missing = RequiredTargetFields.Except(mappedTargets).ToList();
        if (missing.Count > 0)
            return ConnectorValidationResult.Invalid(
                $"Required canonical fields not mapped: {string.Join(", ", missing)}. These must be mapped before sync activation.");

        return ConnectorValidationResult.Valid();
    }

    private static ConnectorValidationResult ValidateCreateRequest(CreateConnectorRequest request)
    {
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(request.Name))
            errors.Add("Name is required.");
        if (request.Name?.Length > 256)
            errors.Add("Name must not exceed 256 characters.");

        return errors.Count > 0
            ? ConnectorValidationResult.Invalid([.. errors])
            : ConnectorValidationResult.Valid();
    }

    private async Task<ConnectorEntity?> FindConnectorAsync(string tenantId, Guid connectorId, CancellationToken ct)
    {
        return await _db.Connectors
            .Include(c => c.SyncRuns)
            .FirstOrDefaultAsync(c => c.Id == connectorId && c.TenantId == tenantId, ct);
    }

    private static ConnectorResponse ToResponse(ConnectorEntity entity)
    {
        return new ConnectorResponse
        {
            Id = entity.Id,
            Name = entity.Name,
            ConnectorType = entity.ConnectorType,
            Status = entity.Status,
            AuthType = entity.AuthType,
            HasSecret = !string.IsNullOrEmpty(entity.KeyVaultSecretName),
            SourceConfig = entity.SourceConfig,
            FieldMapping = DeserializeFieldMapping(entity.FieldMapping),
            ScheduleCron = entity.ScheduleCron,
            LastScheduledSyncAt = entity.LastScheduledSyncAt,
            CreatedAt = entity.CreatedAt,
            UpdatedAt = entity.UpdatedAt,
            LastSyncRun = entity.SyncRuns?.OrderByDescending(s => s.StartedAt).FirstOrDefault() is { } run
                ? ToSyncRunSummary(run) : null,
        };
    }

    private static SyncRunSummary ToSyncRunSummary(SyncRunEntity run)
    {
        return new SyncRunSummary
        {
            Id = run.Id,
            Status = run.Status,
            IsBackfill = run.IsBackfill,
            StartedAt = run.StartedAt,
            CompletedAt = run.CompletedAt,
            RecordsProcessed = run.RecordsProcessed,
            RecordsFailed = run.RecordsFailed,
            ErrorDetail = run.ErrorDetail,
        };
    }

    private static FieldMappingConfig? DeserializeFieldMapping(string? json, ILogger? logger = null) =>
        string.IsNullOrWhiteSpace(json) ? null : JsonDeserializeHelper.DeserializeOrNull<FieldMappingConfig>(json, SharedJsonOptions.CamelCaseCompact, logger);

    private async Task RegisterWebhooksAsync(ConnectorEntity entity, string correlationId, CancellationToken ct)
    {
        var webhookManager = _webhookManagers.FirstOrDefault(m => m.Type == entity.ConnectorType);
        if (webhookManager is null || !_webhookSettings.IsConfigured)
        {
            _logger.LogInformation(
                "Webhook registration skipped: no manager for {ConnectorType} or webhook base URL not configured (connector={ConnectorId})",
                entity.ConnectorType, entity.Id);
            return;
        }

        var (secretValue, secretError) = await ResolveSecretValueAsync(entity, ct);
        if (secretError is not null)
        {
            _logger.LogWarning("Failed to retrieve secret for webhook registration (connector={ConnectorId}): {Error}",
                entity.Id, secretError);
            return;
        }

        try
        {
            var registrations = await webhookManager.RegisterAsync(
                new WebhookRegistrationContext(
                    entity.Id, entity.TenantId, entity.SourceConfig,
                    secretValue, _webhookSettings.BaseCallbackUrl!),
                ct);

            var now = DateTimeOffset.UtcNow;
            foreach (var reg in registrations)
            {
                // Store webhook secret in Key Vault if a secret provider is available.
                string? webhookSecretName = null;
                if (!string.IsNullOrEmpty(reg.WebhookSecret) && _secretProvider is not null)
                {
                    webhookSecretName = $"webhook-{entity.Id}-{reg.EventType.Replace('.', '-')}";
                    try
                    {
                        await _secretProvider.SetSecretAsync(webhookSecretName, reg.WebhookSecret, ct);
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        _logger.LogWarning(ex, "Failed to store webhook secret in Key Vault (connector={ConnectorId})", entity.Id);
                        webhookSecretName = null;
                    }
                }

                var subscription = new WebhookSubscriptionEntity
                {
                    Id = Guid.NewGuid(),
                    ConnectorId = entity.Id,
                    TenantId = entity.TenantId,
                    ExternalSubscriptionId = reg.ExternalSubscriptionId,
                    EventType = reg.EventType,
                    CallbackUrl = reg.CallbackUrl,
                    WebhookSecretName = webhookSecretName,
                    IsActive = true,
                    PollingFallbackActive = false,
                    ConsecutiveFailures = 0,
                    CreatedAt = now,
                    UpdatedAt = now,
                };

                _db.Set<WebhookSubscriptionEntity>().Add(subscription);
            }

            await _db.SaveChangesAsync(ct);

            _logger.LogInformation(
                "Registered {Count} webhook subscriptions for connector {ConnectorId}",
                registrations.Count, entity.Id);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Webhook registration failed for connector {ConnectorId}; polling fallback will be used",
                entity.Id);
        }
    }

    private async Task DeregisterWebhooksAsync(ConnectorEntity entity, string correlationId, CancellationToken ct)
    {
        var subscriptions = await _db.Set<WebhookSubscriptionEntity>()
            .Where(w => w.ConnectorId == entity.Id && w.IsActive)
            .ToListAsync(ct);

        if (subscriptions.Count == 0) return;

        var webhookManager = _webhookManagers.FirstOrDefault(m => m.Type == entity.ConnectorType);
        if (webhookManager is not null)
        {
            var (secretValue, _) = await ResolveSecretValueAsync(entity, ct);

            var externalIds = subscriptions
                .Where(s => !string.IsNullOrEmpty(s.ExternalSubscriptionId))
                .Select(s => s.ExternalSubscriptionId!)
                .ToList();

            if (externalIds.Count > 0)
            {
                try
                {
                    await webhookManager.DeregisterAsync(
                        new WebhookDeregistrationContext(
                            entity.Id, entity.TenantId, entity.SourceConfig,
                            secretValue, externalIds),
                        ct);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger.LogWarning(ex, "Failed to deregister webhooks from external system (connector={ConnectorId})", entity.Id);
                }
            }
        }

        // Mark all subscriptions as inactive.
        var now = DateTimeOffset.UtcNow;
        foreach (var sub in subscriptions)
        {
            sub.IsActive = false;
            sub.UpdatedAt = now;
        }

        await _db.SaveChangesAsync(ct);

        _logger.LogInformation(
            "Deactivated {Count} webhook subscriptions for connector {ConnectorId}",
            subscriptions.Count, entity.Id);
    }

    private async Task WriteAuditAsync(string tenantId, string actorId, string correlationId, string eventType, string detail, CancellationToken ct = default)
    {
        await _auditWriter.WriteAsync(new AuditEvent(
            EventId: Guid.NewGuid().ToString(),
            EventType: eventType,
            TenantId: tenantId,
            ActorId: actorId,
            CorrelationId: correlationId,
            Timestamp: DateTimeOffset.UtcNow,
            Detail: detail), ct);
    }
}
