using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SmartKb.Contracts;
using SmartKb.Contracts.Configuration;
using SmartKb.Contracts.Models;
using SmartKb.Contracts.Services;
using SmartKb.Data.Entities;

namespace SmartKb.Data.Repositories;

public sealed class TeamPlaybookService : ITeamPlaybookService
{
    private readonly SmartKbDbContext _db;
    private readonly IAuditEventWriter _auditWriter;
    private readonly ILogger<TeamPlaybookService> _logger;

    /// <summary>
    /// Standard handoff fields present on every escalation draft.
    /// Playbook RequiredFields refer to these by name; validation checks
    /// that the corresponding property on the draft request is non-empty.
    /// </summary>
    internal static readonly HashSet<string> KnownHandoffFields = new(StringComparer.OrdinalIgnoreCase)
    {
        "Title",
        "CustomerSummary",
        "StepsToReproduce",
        "LogsIdsRequested",
        "SuspectedComponent",
        "Severity",
        "Reason",
        "EvidenceLinks",
    };

    public TeamPlaybookService(
        SmartKbDbContext db,
        IAuditEventWriter auditWriter,
        ILogger<TeamPlaybookService> logger)
    {
        _db = db;
        _auditWriter = auditWriter;
        _logger = logger;
    }

    public async Task<TeamPlaybookListResponse> GetPlaybooksAsync(
        string tenantId, CancellationToken ct = default)
    {
        var playbooks = await _db.TeamPlaybooks
            .Where(p => p.TenantId == tenantId)
            .OrderBy(p => p.TeamName)
            .ToListAsync(ct);

        return new TeamPlaybookListResponse
        {
            Playbooks = playbooks.Select(MapPlaybook).ToList(),
            TotalCount = playbooks.Count,
        };
    }

    public async Task<TeamPlaybookDto?> GetPlaybookAsync(
        string tenantId, Guid playbookId, CancellationToken ct = default)
    {
        var entity = await _db.TeamPlaybooks
            .FirstOrDefaultAsync(p => p.Id == playbookId && p.TenantId == tenantId, ct);

        return entity is null ? null : MapPlaybook(entity);
    }

    public async Task<TeamPlaybookDto?> GetPlaybookByTeamAsync(
        string tenantId, string teamName, CancellationToken ct = default)
    {
        var entity = await _db.TeamPlaybooks
            .FirstOrDefaultAsync(p => p.TenantId == tenantId && p.TeamName == teamName, ct);

        return entity is null ? null : MapPlaybook(entity);
    }

    public async Task<TeamPlaybookDto> CreatePlaybookAsync(
        string tenantId, string userId, string correlationId,
        CreateTeamPlaybookRequest request, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request.TeamName))
            throw new ArgumentException("TeamName is required.");

        // Validate severity values if provided.
        if (request.MinSeverity is not null)
            EscalationSettings.ValidateSeverity(request.MinSeverity);
        if (request.AutoRouteSeverity is not null)
            EscalationSettings.ValidateSeverity(request.AutoRouteSeverity);

        // Validate required fields reference known handoff fields.
        ValidateRequiredFields(request.RequiredFields);

        // Check for duplicate team name within tenant.
        var exists = await _db.TeamPlaybooks
            .AnyAsync(p => p.TenantId == tenantId && p.TeamName == request.TeamName, ct);

        if (exists)
            throw new InvalidOperationException($"A playbook for team '{request.TeamName}' already exists in this tenant.");

        if (request.MaxConcurrentEscalations is < 1)
            throw new ArgumentException("MaxConcurrentEscalations must be at least 1.");

        var now = DateTimeOffset.UtcNow;
        var entity = new TeamPlaybookEntity
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            TeamName = request.TeamName,
            Description = request.Description,
            RequiredFieldsJson = JsonSerializer.Serialize(request.RequiredFields, SharedJsonOptions.CamelCaseWrite),
            ChecklistJson = JsonSerializer.Serialize(request.Checklist, SharedJsonOptions.CamelCaseWrite),
            ContactChannel = request.ContactChannel,
            RequiresApproval = request.RequiresApproval,
            MinSeverity = request.MinSeverity?.ToUpperInvariant(),
            AutoRouteSeverity = request.AutoRouteSeverity?.ToUpperInvariant(),
            MaxConcurrentEscalations = request.MaxConcurrentEscalations,
            FallbackTeam = request.FallbackTeam,
            IsActive = true,
            CreatedAt = now,
            UpdatedAt = now,
        };

        _db.TeamPlaybooks.Add(entity);
        await _db.SaveChangesAsync(ct);

        await _auditWriter.WriteAsync(new AuditEvent(
            EventId: entity.Id.ToString(),
            EventType: AuditEventTypes.PlaybookCreated,
            TenantId: tenantId,
            ActorId: userId,
            CorrelationId: correlationId,
            Timestamp: now,
            Detail: $"Team playbook created: {request.TeamName}"), ct);

        _logger.LogInformation(
            "Team playbook created. PlaybookId={PlaybookId}, TeamName={TeamName}, TenantId={TenantId}",
            entity.Id, request.TeamName, tenantId);

        return MapPlaybook(entity);
    }

    public async Task<TeamPlaybookDto?> UpdatePlaybookAsync(
        string tenantId, string userId, string correlationId,
        Guid playbookId, UpdateTeamPlaybookRequest request, CancellationToken ct = default)
    {
        var entity = await _db.TeamPlaybooks
            .FirstOrDefaultAsync(p => p.Id == playbookId && p.TenantId == tenantId, ct);

        if (entity is null) return null;

        if (request.MinSeverity is not null)
            EscalationSettings.ValidateSeverity(request.MinSeverity);
        if (request.AutoRouteSeverity is not null)
            EscalationSettings.ValidateSeverity(request.AutoRouteSeverity);
        if (request.RequiredFields is not null)
            ValidateRequiredFields(request.RequiredFields);
        if (request.MaxConcurrentEscalations is < 1)
            throw new ArgumentException("MaxConcurrentEscalations must be at least 1.");

        var now = DateTimeOffset.UtcNow;

        if (request.Description is not null) entity.Description = request.Description;
        if (request.RequiredFields is not null) entity.RequiredFieldsJson = JsonSerializer.Serialize(request.RequiredFields, SharedJsonOptions.CamelCaseWrite);
        if (request.Checklist is not null) entity.ChecklistJson = JsonSerializer.Serialize(request.Checklist, SharedJsonOptions.CamelCaseWrite);
        if (request.ContactChannel is not null) entity.ContactChannel = request.ContactChannel;
        if (request.RequiresApproval.HasValue) entity.RequiresApproval = request.RequiresApproval.Value;
        if (request.MinSeverity is not null) entity.MinSeverity = request.MinSeverity.ToUpperInvariant();
        if (request.AutoRouteSeverity is not null) entity.AutoRouteSeverity = request.AutoRouteSeverity.ToUpperInvariant();
        if (request.MaxConcurrentEscalations.HasValue) entity.MaxConcurrentEscalations = request.MaxConcurrentEscalations.Value;
        if (request.FallbackTeam is not null) entity.FallbackTeam = request.FallbackTeam;
        if (request.IsActive.HasValue) entity.IsActive = request.IsActive.Value;
        entity.UpdatedAt = now;

        await _db.SaveChangesAsync(ct);

        await _auditWriter.WriteAsync(new AuditEvent(
            EventId: entity.Id.ToString(),
            EventType: AuditEventTypes.PlaybookUpdated,
            TenantId: tenantId,
            ActorId: userId,
            CorrelationId: correlationId,
            Timestamp: now,
            Detail: $"Team playbook updated: {entity.TeamName}"), ct);

        return MapPlaybook(entity);
    }

    public async Task<bool> DeletePlaybookAsync(
        string tenantId, string userId, string correlationId,
        Guid playbookId, CancellationToken ct = default)
    {
        var entity = await _db.TeamPlaybooks
            .FirstOrDefaultAsync(p => p.Id == playbookId && p.TenantId == tenantId, ct);

        if (entity is null) return false;

        entity.DeletedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(ct);

        await _auditWriter.WriteAsync(new AuditEvent(
            EventId: playbookId.ToString(),
            EventType: AuditEventTypes.PlaybookDeleted,
            TenantId: tenantId,
            ActorId: userId,
            CorrelationId: correlationId,
            Timestamp: DateTimeOffset.UtcNow,
            Detail: $"Team playbook deleted: {entity.TeamName}"), ct);

        _logger.LogInformation(
            "Team playbook deleted. PlaybookId={PlaybookId}, TeamName={TeamName}, TenantId={TenantId}",
            playbookId, entity.TeamName, tenantId);

        return true;
    }

    public async Task<PlaybookValidationResult> ValidateDraftAsync(
        string tenantId, string targetTeam, CreateEscalationDraftRequest draft,
        CancellationToken ct = default)
    {
        var playbook = await _db.TeamPlaybooks
            .FirstOrDefaultAsync(p =>
                p.TenantId == tenantId &&
                p.TeamName == targetTeam &&
                p.IsActive, ct);

        // No playbook for this team — no constraints, validation passes.
        if (playbook is null)
        {
            return new PlaybookValidationResult
            {
                IsValid = true,
                TeamName = targetTeam,
                MissingRequiredFields = [],
                Checklist = [],
                RequiresApproval = false,
            };
        }

        var missingFields = new List<string>();
        var requiredFields = DeserializeStringList(playbook.RequiredFieldsJson);

        foreach (var field in requiredFields)
        {
            if (!IsFieldPopulated(draft, field))
                missingFields.Add(field);
        }

        // Check severity policy.
        string? policyViolation = null;
        if (playbook.MinSeverity is not null)
        {
            var severityIndex = Array.IndexOf(EscalationSettings.SeverityOrder, draft.Severity.ToUpperInvariant());
            var minIndex = Array.IndexOf(EscalationSettings.SeverityOrder, playbook.MinSeverity);
            if (severityIndex >= 0 && minIndex >= 0 && severityIndex > minIndex)
            {
                policyViolation = $"Severity {draft.Severity} does not meet minimum {playbook.MinSeverity} for team {targetTeam}.";
            }
        }

        // Check concurrent escalation limit.
        if (playbook.MaxConcurrentEscalations.HasValue)
        {
            var openCount = await _db.EscalationDrafts
                .CountAsync(d =>
                    d.TenantId == tenantId &&
                    d.TargetTeam == targetTeam &&
                    d.ExternalStatus != EscalationExternalStatus.Completed &&
                    d.DeletedAt == null, ct);

            if (openCount >= playbook.MaxConcurrentEscalations.Value)
            {
                policyViolation = policyViolation is null
                    ? $"Team {targetTeam} has reached max concurrent escalations ({playbook.MaxConcurrentEscalations.Value}). Fallback: {playbook.FallbackTeam ?? "none"}."
                    : policyViolation + $" Also: max concurrent escalations reached ({playbook.MaxConcurrentEscalations.Value}).";
            }
        }

        var checklist = DeserializeStringList(playbook.ChecklistJson);

        return new PlaybookValidationResult
        {
            IsValid = missingFields.Count == 0 && policyViolation is null,
            TeamName = targetTeam,
            MissingRequiredFields = missingFields,
            Checklist = checklist,
            ContactChannel = playbook.ContactChannel,
            RequiresApproval = playbook.RequiresApproval,
            PolicyViolation = policyViolation,
        };
    }

    internal static bool IsFieldPopulated(CreateEscalationDraftRequest draft, string fieldName)
    {
        if (string.Equals(fieldName, "Title", StringComparison.OrdinalIgnoreCase))
            return !string.IsNullOrWhiteSpace(draft.Title);
        if (string.Equals(fieldName, "CustomerSummary", StringComparison.OrdinalIgnoreCase))
            return !string.IsNullOrWhiteSpace(draft.CustomerSummary);
        if (string.Equals(fieldName, "StepsToReproduce", StringComparison.OrdinalIgnoreCase))
            return !string.IsNullOrWhiteSpace(draft.StepsToReproduce);
        if (string.Equals(fieldName, "LogsIdsRequested", StringComparison.OrdinalIgnoreCase))
            return !string.IsNullOrWhiteSpace(draft.LogsIdsRequested);
        if (string.Equals(fieldName, "SuspectedComponent", StringComparison.OrdinalIgnoreCase))
            return !string.IsNullOrWhiteSpace(draft.SuspectedComponent);
        if (string.Equals(fieldName, "Severity", StringComparison.OrdinalIgnoreCase))
            return !string.IsNullOrWhiteSpace(draft.Severity);
        if (string.Equals(fieldName, "Reason", StringComparison.OrdinalIgnoreCase))
            return !string.IsNullOrWhiteSpace(draft.Reason);
        if (string.Equals(fieldName, "EvidenceLinks", StringComparison.OrdinalIgnoreCase))
            return draft.EvidenceLinks.Count > 0;
        return false;
    }

    private static void ValidateRequiredFields(IReadOnlyList<string> fields)
    {
        foreach (var field in fields)
        {
            if (!KnownHandoffFields.Contains(field))
                throw new ArgumentException($"Unknown required field: '{field}'. Known fields: {string.Join(", ", KnownHandoffFields)}");
        }
    }

    private static List<string> DeserializeStringList(string json, ILogger? logger = null) =>
        JsonDeserializeHelper.Deserialize<List<string>>(json, SharedJsonOptions.CamelCaseWrite, logger!, []);

    private static TeamPlaybookDto MapPlaybook(TeamPlaybookEntity entity) => new()
    {
        Id = entity.Id,
        TeamName = entity.TeamName,
        Description = entity.Description,
        RequiredFields = DeserializeStringList(entity.RequiredFieldsJson),
        Checklist = DeserializeStringList(entity.ChecklistJson),
        ContactChannel = entity.ContactChannel,
        RequiresApproval = entity.RequiresApproval,
        MinSeverity = entity.MinSeverity,
        AutoRouteSeverity = entity.AutoRouteSeverity,
        MaxConcurrentEscalations = entity.MaxConcurrentEscalations,
        FallbackTeam = entity.FallbackTeam,
        IsActive = entity.IsActive,
        CreatedAt = entity.CreatedAt,
        UpdatedAt = entity.UpdatedAt,
    };
}
