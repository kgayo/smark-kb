using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SmartKb.Contracts.Enums;
using SmartKb.Contracts.Models;
using SmartKb.Contracts.Services;
using SmartKb.Data;
using SmartKb.Data.Entities;

namespace SmartKb.Api.Connectors;

public sealed class ConnectorAdminService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false,
    };

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
    private readonly IEnumerable<IConnectorClient> _connectorClients;

    public ConnectorAdminService(
        SmartKbDbContext db,
        IAuditEventWriter auditWriter,
        IEnumerable<IConnectorClient> connectorClients,
        ISecretProvider? secretProvider = null)
    {
        _db = db;
        _auditWriter = auditWriter;
        _secretProvider = secretProvider;
        _connectorClients = connectorClients;
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
                ? JsonSerializer.Serialize(request.FieldMapping, JsonOptions)
                : null,
            ScheduleCron = request.ScheduleCron,
            CreatedAt = now,
            UpdatedAt = now,
        };

        _db.Connectors.Add(entity);
        await _db.SaveChangesAsync(ct);

        await WriteAuditAsync(tenantId, actorId, correlationId, "connector.created",
            $"Connector '{entity.Name}' (id={entity.Id}, type={entity.ConnectorType}) created.");

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
            entity.FieldMapping = JsonSerializer.Serialize(request.FieldMapping, JsonOptions);
        if (request.ScheduleCron is not null) entity.ScheduleCron = request.ScheduleCron;
        if (request.AuthType.HasValue) entity.AuthType = request.AuthType.Value;
        if (request.KeyVaultSecretName is not null) entity.KeyVaultSecretName = request.KeyVaultSecretName;

        entity.UpdatedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(ct);

        await WriteAuditAsync(tenantId, actorId, correlationId, "connector.updated",
            $"Connector '{entity.Name}' (id={entity.Id}) updated.");

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

        await WriteAuditAsync(tenantId, actorId, correlationId, "connector.deleted",
            $"Connector '{entity.Name}' (id={entity.Id}) soft-deleted.");

        return true;
    }

    public async Task<(bool Found, ConnectorResponse? Response)> SetStatusAsync(
        string tenantId, string actorId, string correlationId,
        Guid connectorId, ConnectorStatus newStatus, CancellationToken ct = default)
    {
        var entity = await FindConnectorAsync(tenantId, connectorId, ct);
        if (entity is null) return (false, null);

        // Validate before enabling: field mapping must cover required fields.
        if (newStatus == ConnectorStatus.Enabled)
        {
            var validation = ValidateFieldMappingForActivation(entity.FieldMapping);
            if (!validation.IsValid)
                return (true, null); // Found but validation failed — caller checks validation separately.
        }

        entity.Status = newStatus;
        entity.UpdatedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(ct);

        var verb = newStatus == ConnectorStatus.Enabled ? "enabled" : "disabled";
        await WriteAuditAsync(tenantId, actorId, correlationId, $"connector.{verb}",
            $"Connector '{entity.Name}' (id={entity.Id}) {verb}.");

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

        await WriteAuditAsync(tenantId, actorId, correlationId, "connector.enabled",
            $"Connector '{entity.Name}' (id={entity.Id}) enabled.");

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
            string? secretValue = null;
            if (!string.IsNullOrEmpty(entity.KeyVaultSecretName) && _secretProvider is not null)
            {
                try
                {
                    secretValue = await _secretProvider.GetSecretAsync(entity.KeyVaultSecretName, ct);
                }
                catch (Exception ex)
                {
                    result = new TestConnectionResponse
                    {
                        Success = false,
                        Message = "Failed to retrieve credentials from Key Vault.",
                        DiagnosticDetail = ex.Message,
                    };
                    await WriteAuditAsync(tenantId, actorId, correlationId, "connector.test_failed",
                        $"Connector '{entity.Name}' (id={entity.Id}) test failed: credential retrieval error.");
                    return result;
                }
            }

            result = await client.TestConnectionAsync(tenantId, entity.SourceConfig, secretValue, ct);
        }

        var eventType = result.Success ? "connector.test_passed" : "connector.test_failed";
        await WriteAuditAsync(tenantId, actorId, correlationId, eventType,
            $"Connector '{entity.Name}' (id={entity.Id}) test: {result.Message}");

        return result;
    }

    public async Task<(Guid? SyncRunId, bool NotFound)> SyncNowAsync(
        string tenantId, string actorId, string correlationId,
        Guid connectorId, SyncNowRequest request, CancellationToken ct = default)
    {
        var entity = await FindConnectorAsync(tenantId, connectorId, ct);
        if (entity is null) return (null, true);

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

        await WriteAuditAsync(tenantId, actorId, correlationId, "connector.sync_triggered",
            $"Sync triggered for connector '{entity.Name}' (id={entity.Id}, runId={syncRun.Id}, backfill={request.IsBackfill}).");

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

        string? secretValue = null;
        if (!string.IsNullOrEmpty(entity.KeyVaultSecretName) && _secretProvider is not null)
        {
            try
            {
                secretValue = await _secretProvider.GetSecretAsync(entity.KeyVaultSecretName, ct);
            }
            catch
            {
                return new PreviewResponse
                {
                    Records = [],
                    ValidationErrors = ["Failed to retrieve credentials from Key Vault."],
                };
            }
        }

        var mapping = request.FieldMapping ?? DeserializeFieldMapping(entity.FieldMapping);
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

        await WriteAuditAsync(tenantId, actorId, correlationId, "connector.preview",
            $"Preview for connector '{entity.Name}' (id={entity.Id}): {records.Count} records, {errors.Count} errors.");

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

            if (!string.IsNullOrWhiteSpace(rule.TargetField))
                targetFields.Add(rule.TargetField);
        }

        if (errors.Count > 0)
            return ConnectorValidationResult.Invalid([.. errors]);

        return ConnectorValidationResult.Valid();
    }

    // --- Private helpers ---

    private ConnectorValidationResult ValidateFieldMappingForActivation(string? fieldMappingJson)
    {
        var mapping = DeserializeFieldMapping(fieldMappingJson);
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

    private static FieldMappingConfig? DeserializeFieldMapping(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            return JsonSerializer.Deserialize<FieldMappingConfig>(json, JsonOptions);
        }
        catch
        {
            return null;
        }
    }

    private async Task WriteAuditAsync(string tenantId, string actorId, string correlationId, string eventType, string detail)
    {
        await _auditWriter.WriteAsync(new AuditEvent(
            EventId: Guid.NewGuid().ToString(),
            EventType: eventType,
            TenantId: tenantId,
            ActorId: actorId,
            CorrelationId: correlationId,
            Timestamp: DateTimeOffset.UtcNow,
            Detail: detail));
    }
}
