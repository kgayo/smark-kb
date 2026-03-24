using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SmartKb.Contracts;
using SmartKb.Contracts.Models;
using SmartKb.Contracts.Services;
using SmartKb.Data.Entities;

namespace SmartKb.Data.Repositories;

public sealed class PiiPolicyService : IPiiPolicyService
{
    private static readonly string[] ValidEnforcementModes = ["redact", "detect", "disabled"];
    private static readonly string[] ValidBuiltInPiiTypes = ["email", "phone", "ssn", "credit_card"];

    private readonly SmartKbDbContext _db;
    private readonly IAuditEventWriter _auditWriter;
    private readonly ILogger<PiiPolicyService> _logger;

    public PiiPolicyService(
        SmartKbDbContext db,
        IAuditEventWriter auditWriter,
        ILogger<PiiPolicyService> logger)
    {
        _db = db;
        _auditWriter = auditWriter;
        _logger = logger;
    }

    public async Task<PiiPolicyResponse?> GetPolicyAsync(string tenantId, CancellationToken ct = default)
    {
        var entity = await _db.PiiPolicies
            .FirstOrDefaultAsync(p => p.TenantId == tenantId, ct);

        return entity is null ? null : ToResponse(entity);
    }

    public async Task<PiiPolicyResponse> UpsertPolicyAsync(
        string tenantId, PiiPolicyUpdateRequest request, string actorId, CancellationToken ct = default)
    {
        if (!ValidEnforcementModes.Contains(request.EnforcementMode))
            throw new ArgumentException($"Invalid enforcement mode: {request.EnforcementMode}. Must be one of: {string.Join(", ", ValidEnforcementModes)}");

        foreach (var piiType in request.EnabledPiiTypes)
        {
            if (!ValidBuiltInPiiTypes.Contains(piiType))
                throw new ArgumentException($"Invalid PII type: {piiType}. Must be one of: {string.Join(", ", ValidBuiltInPiiTypes)}");
        }

        if (request.CustomPatterns is not null)
        {
            foreach (var pattern in request.CustomPatterns)
            {
                try { System.Text.RegularExpressions.Regex.Match("", pattern.Pattern); }
                catch (System.Text.RegularExpressions.RegexParseException ex)
                {
                    throw new ArgumentException($"Invalid regex pattern '{pattern.Name}': {ex.Message}");
                }
            }
        }

        var now = DateTimeOffset.UtcNow;
        var entity = await _db.PiiPolicies
            .FirstOrDefaultAsync(p => p.TenantId == tenantId, ct);

        if (entity is null)
        {
            entity = new PiiPolicyEntity
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                CreatedAt = now,
            };
            _db.PiiPolicies.Add(entity);
        }

        entity.EnforcementMode = request.EnforcementMode;
        entity.EnabledPiiTypes = string.Join(",", request.EnabledPiiTypes);
        entity.CustomPatternsJson = JsonSerializer.Serialize(request.CustomPatterns ?? [], SharedJsonOptions.CamelCaseWrite);
        entity.AuditRedactions = request.AuditRedactions;
        entity.UpdatedAt = now;

        await _db.SaveChangesAsync(ct);

        _logger.LogInformation(
            "PII policy updated. TenantId={TenantId} Mode={Mode} Types={Types}",
            tenantId, request.EnforcementMode, entity.EnabledPiiTypes);

        await _auditWriter.WriteAsync(new AuditEvent(
            EventId: Guid.NewGuid().ToString(),
            EventType: AuditEventTypes.PiiPolicyUpdated,
            TenantId: tenantId,
            ActorId: actorId,
            CorrelationId: Guid.NewGuid().ToString(),
            Timestamp: now,
            Detail: $"PII policy updated: mode={request.EnforcementMode}, types={entity.EnabledPiiTypes}, auditRedactions={request.AuditRedactions}"), ct);

        return ToResponse(entity);
    }

    public async Task<bool> DeletePolicyAsync(string tenantId, string actorId, CancellationToken ct = default)
    {
        var entity = await _db.PiiPolicies
            .FirstOrDefaultAsync(p => p.TenantId == tenantId, ct);

        if (entity is null) return false;

        _db.PiiPolicies.Remove(entity);
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation("PII policy deleted (reset to defaults). TenantId={TenantId}", tenantId);

        await _auditWriter.WriteAsync(new AuditEvent(
            EventId: Guid.NewGuid().ToString(),
            EventType: AuditEventTypes.PiiPolicyUpdated,
            TenantId: tenantId,
            ActorId: actorId,
            CorrelationId: Guid.NewGuid().ToString(),
            Timestamp: DateTimeOffset.UtcNow,
            Detail: "PII policy deleted (reset to defaults)."), ct);

        return true;
    }

    private static PiiPolicyResponse ToResponse(PiiPolicyEntity entity)
    {
        var customPatterns = string.IsNullOrWhiteSpace(entity.CustomPatternsJson) || entity.CustomPatternsJson == "[]"
            ? []
            : JsonSerializer.Deserialize<List<CustomPiiPattern>>(entity.CustomPatternsJson, SharedJsonOptions.CamelCaseWrite) ?? [];

        return new PiiPolicyResponse
        {
            TenantId = entity.TenantId,
            EnforcementMode = entity.EnforcementMode,
            EnabledPiiTypes = entity.EnabledPiiTypes.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
            CustomPatterns = customPatterns,
            AuditRedactions = entity.AuditRedactions,
            UpdatedAt = entity.UpdatedAt,
        };
    }
}
