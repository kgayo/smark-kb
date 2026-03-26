using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SmartKb.Contracts;
using SmartKb.Contracts.Configuration;
using SmartKb.Contracts.Models;
using SmartKb.Contracts.Services;
using SmartKb.Data.Entities;

namespace SmartKb.Data.Repositories;

public sealed class EscalationDraftService : IEscalationDraftService
{
    private readonly SmartKbDbContext _db;
    private readonly IAuditEventWriter _auditWriter;
    private readonly EscalationSettings _escalationSettings;
    private readonly ILogger<EscalationDraftService> _logger;
    private readonly IEnumerable<IEscalationTargetConnector> _targetConnectors;
    private readonly ISecretProvider _secretProvider;
    private readonly ITeamPlaybookService _playbookService;

    public EscalationDraftService(
        SmartKbDbContext db,
        IAuditEventWriter auditWriter,
        EscalationSettings escalationSettings,
        ILogger<EscalationDraftService> logger,
        IEnumerable<IEscalationTargetConnector> targetConnectors,
        ISecretProvider secretProvider,
        ITeamPlaybookService playbookService)
    {
        _db = db;
        _auditWriter = auditWriter;
        _escalationSettings = escalationSettings;
        _logger = logger;
        _targetConnectors = targetConnectors;
        _secretProvider = secretProvider;
        _playbookService = playbookService;
    }

    public async Task<EscalationDraftResponse> CreateDraftAsync(
        string tenantId, string userId, string correlationId,
        CreateEscalationDraftRequest request, CancellationToken ct = default)
    {
        // Validate session ownership.
        var sessionExists = await _db.Sessions
            .AnyAsync(s => s.Id == request.SessionId && s.TenantId == tenantId && s.UserId == userId, ct);

        if (!sessionExists)
            throw new InvalidOperationException("Session not found or not owned by user.");

        // Validate message belongs to session.
        var messageExists = await _db.Messages
            .IgnoreQueryFilters()
            .AnyAsync(m => m.Id == request.MessageId && m.SessionId == request.SessionId && m.TenantId == tenantId, ct);

        if (!messageExists)
            throw new InvalidOperationException("Message not found in session.");

        // Apply routing rule if target team not specified.
        var targetTeam = !string.IsNullOrEmpty(request.TargetTeam)
            ? request.TargetTeam
            : await ResolveTargetTeamAsync(tenantId, request.SuspectedComponent, ct);

        var now = DateTimeOffset.UtcNow;
        var entity = new EscalationDraftEntity
        {
            Id = Guid.NewGuid(),
            SessionId = request.SessionId,
            MessageId = request.MessageId,
            TenantId = tenantId,
            UserId = userId,
            Title = !string.IsNullOrEmpty(request.Title) ? request.Title : "Escalation Draft",
            CustomerSummary = request.CustomerSummary,
            StepsToReproduce = request.StepsToReproduce,
            LogsIdsRequested = request.LogsIdsRequested,
            SuspectedComponent = request.SuspectedComponent,
            Severity = EscalationSettings.NormalizeSeverity(request.Severity),
            EvidenceLinksJson = JsonSerializer.Serialize(request.EvidenceLinks, SharedJsonOptions.CamelCaseWrite),
            TargetTeam = targetTeam,
            Reason = request.Reason,
            CreatedAt = now,
        };

        _db.EscalationDrafts.Add(entity);
        await _db.SaveChangesAsync(ct);

        // Validate against team playbook (P2-002).
        var playbookValidation = await _playbookService.ValidateDraftAsync(tenantId, targetTeam, request, ct);

        if (!playbookValidation.IsValid)
        {
            _logger.LogWarning(
                "Escalation draft has playbook violations. DraftId={DraftId}, TeamName={TeamName}, MissingFields={MissingFields}, PolicyViolation={PolicyViolation}",
                entity.Id, targetTeam,
                string.Join(", ", playbookValidation.MissingRequiredFields),
                playbookValidation.PolicyViolation);

            await _auditWriter.WriteAsync(new AuditEvent(
                EventId: entity.Id.ToString(),
                EventType: AuditEventTypes.PlaybookValidationFailed,
                TenantId: tenantId,
                ActorId: userId,
                CorrelationId: correlationId,
                Timestamp: now,
                Detail: $"Playbook validation failed for team {targetTeam}. Missing: [{string.Join(", ", playbookValidation.MissingRequiredFields)}]. Policy: {playbookValidation.PolicyViolation ?? "none"}"), ct);
        }

        _logger.LogInformation(
            "Escalation draft created. DraftId={DraftId}, SessionId={SessionId}, TargetTeam={TargetTeam}, Severity={Severity}, TenantId={TenantId}",
            entity.Id, entity.SessionId, entity.TargetTeam, entity.Severity, tenantId);

        await _auditWriter.WriteAsync(new AuditEvent(
            EventId: Guid.NewGuid().ToString(),
            EventType: AuditEventTypes.EscalationDraftCreated,
            TenantId: tenantId,
            ActorId: userId,
            CorrelationId: correlationId,
            Timestamp: now,
            Detail: $"Escalation draft created. DraftId={entity.Id}, SessionId={entity.SessionId}, TargetTeam={entity.TargetTeam}, Severity={entity.Severity}"), ct);

        return MapDraft(entity, playbookValidation);
    }

    public async Task<EscalationDraftResponse?> GetDraftAsync(
        string tenantId, string userId, Guid draftId, CancellationToken ct = default)
    {
        var entity = await _db.EscalationDrafts
            .FirstOrDefaultAsync(d => d.Id == draftId && d.TenantId == tenantId && d.UserId == userId, ct);

        return entity is null ? null : MapDraft(entity);
    }

    public async Task<EscalationDraftListResponse?> ListDraftsAsync(
        string tenantId, string userId, Guid sessionId, CancellationToken ct = default)
    {
        var sessionExists = await _db.Sessions
            .AnyAsync(s => s.Id == sessionId && s.TenantId == tenantId && s.UserId == userId, ct);

        if (!sessionExists) return null;

        var drafts = await _db.EscalationDrafts
            .Where(d => d.SessionId == sessionId && d.TenantId == tenantId && d.UserId == userId)
            .ToListAsync(ct);

        drafts = drafts.OrderByDescending(d => d.CreatedAt).ToList();

        return new EscalationDraftListResponse
        {
            SessionId = sessionId,
            Drafts = drafts.Select(d => MapDraft(d)).ToList(),
            TotalCount = drafts.Count,
        };
    }

    public async Task<(EscalationDraftResponse? Response, bool NotFound)> UpdateDraftAsync(
        string tenantId, string userId, Guid draftId,
        UpdateEscalationDraftRequest request, CancellationToken ct = default)
    {
        var entity = await _db.EscalationDrafts
            .FirstOrDefaultAsync(d => d.Id == draftId && d.TenantId == tenantId && d.UserId == userId, ct);

        if (entity is null) return (null, true);

        if (request.Title is not null) entity.Title = request.Title;
        if (request.CustomerSummary is not null) entity.CustomerSummary = request.CustomerSummary;
        if (request.StepsToReproduce is not null) entity.StepsToReproduce = request.StepsToReproduce;
        if (request.LogsIdsRequested is not null) entity.LogsIdsRequested = request.LogsIdsRequested;
        if (request.SuspectedComponent is not null) entity.SuspectedComponent = request.SuspectedComponent;
        if (request.Severity is not null) entity.Severity = EscalationSettings.NormalizeSeverity(request.Severity);
        if (request.EvidenceLinks is not null) entity.EvidenceLinksJson = JsonSerializer.Serialize(request.EvidenceLinks, SharedJsonOptions.CamelCaseWrite);
        if (request.TargetTeam is not null) entity.TargetTeam = request.TargetTeam;
        if (request.Reason is not null) entity.Reason = request.Reason;

        await _db.SaveChangesAsync(ct);

        _logger.LogInformation(
            "Escalation draft updated. DraftId={DraftId}, TenantId={TenantId}",
            draftId, tenantId);

        return (MapDraft(entity), false);
    }

    public async Task<EscalationDraftExportResponse?> ExportDraftAsMarkdownAsync(
        string tenantId, string userId, Guid draftId, CancellationToken ct = default)
    {
        var entity = await _db.EscalationDrafts
            .FirstOrDefaultAsync(d => d.Id == draftId && d.TenantId == tenantId && d.UserId == userId, ct);

        if (entity is null) return null;

        var markdown = BuildMarkdown(entity);

        // Mark as exported.
        var now = DateTimeOffset.UtcNow;
        entity.ExportedAt = now;
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation(
            "Escalation draft exported. DraftId={DraftId}, TenantId={TenantId}",
            draftId, tenantId);

        return new EscalationDraftExportResponse
        {
            DraftId = draftId,
            Markdown = markdown,
            ExportedAt = now,
        };
    }

    public async Task<bool> DeleteDraftAsync(
        string tenantId, string userId, Guid draftId, CancellationToken ct = default)
    {
        var entity = await _db.EscalationDrafts
            .FirstOrDefaultAsync(d => d.Id == draftId && d.TenantId == tenantId && d.UserId == userId, ct);

        if (entity is null) return false;

        entity.DeletedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation(
            "Escalation draft soft-deleted. DraftId={DraftId}, TenantId={TenantId}",
            draftId, tenantId);

        return true;
    }

    public async Task<ExternalEscalationResult?> ApproveAndCreateExternalAsync(
        string tenantId, string userId, string correlationId,
        Guid draftId, ApproveEscalationDraftRequest request, CancellationToken ct = default)
    {
        var entity = await _db.EscalationDrafts
            .FirstOrDefaultAsync(d => d.Id == draftId && d.TenantId == tenantId && d.UserId == userId, ct);

        if (entity is null) return null;

        // Idempotency: if already approved and succeeded, return cached result.
        if (entity.ExternalStatus == EscalationExternalStatus.Created && entity.ApprovedAt.HasValue)
        {
            return new ExternalEscalationResult
            {
                DraftId = draftId,
                ExternalStatus = EscalationExternalStatus.Created,
                ExternalId = entity.ExternalId,
                ExternalUrl = entity.ExternalUrl,
                ApprovedAt = entity.ApprovedAt,
                ConnectorType = entity.TargetConnectorType?.ToString(),
            };
        }

        // Look up the target connector.
        var connector = await _db.Connectors
            .FirstOrDefaultAsync(c => c.Id == request.ConnectorId && c.TenantId == tenantId, ct);

        if (connector is null)
            throw new InvalidOperationException("Connector not found or does not belong to this tenant.");

        if (connector.Status != Contracts.Enums.ConnectorStatus.Enabled)
            throw new InvalidOperationException("Connector is not enabled.");

        if (connector.ConnectorType != Contracts.Enums.ConnectorType.AzureDevOps &&
            connector.ConnectorType != Contracts.Enums.ConnectorType.ClickUp)
            throw new InvalidOperationException($"Connector type '{connector.ConnectorType}' does not support external escalation creation. Only AzureDevOps and ClickUp are supported.");

        // Find the matching IEscalationTargetConnector implementation.
        var targetConnector = _targetConnectors.FirstOrDefault(c => c.Type == connector.ConnectorType);
        if (targetConnector is null)
            throw new InvalidOperationException($"No escalation target connector registered for '{connector.ConnectorType}'.");

        // Fetch the secret from Key Vault.
        string? secretValue = null;
        if (!string.IsNullOrEmpty(connector.KeyVaultSecretName))
        {
            try
            {
                secretValue = await _secretProvider.GetSecretAsync(connector.KeyVaultSecretName, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "Failed to retrieve secret for connector {ConnectorId}", connector.Id);
                throw new InvalidOperationException("Failed to retrieve connector credentials from Key Vault.");
            }
        }

        if (string.IsNullOrEmpty(secretValue))
            throw new InvalidOperationException("Connector has no credentials configured.");

        // Build the description from escalation draft fields.
        var description = BuildExternalDescription(entity);

        // Record approval.
        var now = DateTimeOffset.UtcNow;
        entity.ApprovedAt = now;
        entity.ApprovedBy = userId;
        entity.TargetConnectorId = connector.Id;
        entity.TargetConnectorType = connector.ConnectorType;
        entity.ExternalStatus = EscalationExternalStatus.Pending;
        await _db.SaveChangesAsync(ct);

        await _auditWriter.WriteAsync(new AuditEvent(
            EventId: Guid.NewGuid().ToString(),
            EventType: AuditEventTypes.EscalationDraftApproved,
            TenantId: tenantId,
            ActorId: userId,
            CorrelationId: correlationId,
            Timestamp: now,
            Detail: $"Escalation draft approved for external creation. DraftId={draftId}, ConnectorId={connector.Id}, ConnectorType={connector.ConnectorType}"), ct);

        // Create the external work item/task.
        var workItemRequest = new ExternalWorkItemRequest
        {
            Title = entity.Title,
            Description = description,
            Severity = entity.Severity,
            TargetProject = request.TargetProject,
            TargetListId = request.TargetListId,
            AreaPath = request.AreaPath,
            WorkItemType = request.WorkItemType,
        };

        var result = await targetConnector.CreateExternalWorkItemAsync(
            connector.SourceConfig ?? "{}", secretValue, workItemRequest, ct);

        // Update entity with result.
        entity.ExternalId = result.ExternalId;
        entity.ExternalUrl = result.ExternalUrl;
        entity.ExternalStatus = result.Success ? EscalationExternalStatus.Created : "Failed";
        entity.ExternalErrorDetail = result.ErrorDetail;
        await _db.SaveChangesAsync(ct);

        var auditEventType = result.Success
            ? AuditEventTypes.EscalationExternalCreated
            : AuditEventTypes.EscalationExternalFailed;

        await _auditWriter.WriteAsync(new AuditEvent(
            EventId: Guid.NewGuid().ToString(),
            EventType: auditEventType,
            TenantId: tenantId,
            ActorId: userId,
            CorrelationId: correlationId,
            Timestamp: DateTimeOffset.UtcNow,
            Detail: result.Success
                ? $"External work item created. DraftId={draftId}, ExternalId={result.ExternalId}, Url={result.ExternalUrl}"
                : $"External work item creation failed. DraftId={draftId}, Error={result.ErrorDetail}"), ct);

        _logger.LogInformation(
            "External escalation {Status}. DraftId={DraftId}, ConnectorType={ConnectorType}, ExternalId={ExternalId}",
            entity.ExternalStatus, draftId, connector.ConnectorType, result.ExternalId);

        return new ExternalEscalationResult
        {
            DraftId = draftId,
            ExternalStatus = entity.ExternalStatus,
            ExternalId = result.ExternalId,
            ExternalUrl = result.ExternalUrl,
            ErrorDetail = result.ErrorDetail,
            ApprovedAt = now,
            ConnectorType = connector.ConnectorType.ToString(),
        };
    }

    internal static string BuildExternalDescription(EscalationDraftEntity entity)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"## Escalation: {entity.Title}");
        sb.AppendLine();
        sb.AppendLine($"**Severity:** {entity.Severity}");
        sb.AppendLine($"**Target Team:** {entity.TargetTeam}");
        sb.AppendLine($"**Suspected Component:** {entity.SuspectedComponent}");
        sb.AppendLine();
        sb.AppendLine("### Reason");
        sb.AppendLine(entity.Reason);
        sb.AppendLine();
        sb.AppendLine("### Customer Summary");
        sb.AppendLine(entity.CustomerSummary);
        sb.AppendLine();
        if (!string.IsNullOrEmpty(entity.StepsToReproduce))
        {
            sb.AppendLine("### Steps to Reproduce");
            sb.AppendLine(entity.StepsToReproduce);
            sb.AppendLine();
        }
        if (!string.IsNullOrEmpty(entity.LogsIdsRequested))
        {
            sb.AppendLine("### Logs / IDs Requested");
            sb.AppendLine(entity.LogsIdsRequested);
            sb.AppendLine();
        }
        var citations = DeserializeCitations(entity.EvidenceLinksJson);
        if (citations.Count > 0)
        {
            sb.AppendLine("### Evidence Links");
            foreach (var c in citations)
                sb.AppendLine($"- [{c.Title}]({c.SourceUrl})");
            sb.AppendLine();
        }
        sb.AppendLine("---");
        sb.AppendLine("_Created by Smart KB escalation workflow._");
        return sb.ToString();
    }

    private async Task<string> ResolveTargetTeamAsync(
        string tenantId, string suspectedComponent, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(suspectedComponent))
            return _escalationSettings.FallbackTargetTeam;

        var rule = await _db.EscalationRoutingRules
            .FirstOrDefaultAsync(r =>
                r.TenantId == tenantId &&
                r.ProductArea == suspectedComponent &&
                r.IsActive, ct);

        return rule?.TargetTeam ?? _escalationSettings.FallbackTargetTeam;
    }

    internal static string BuildMarkdown(EscalationDraftEntity entity)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"# {entity.Title}");
        sb.AppendLine();
        sb.AppendLine($"**Severity:** {entity.Severity}");
        sb.AppendLine($"**Target Team:** {entity.TargetTeam}");
        sb.AppendLine($"**Suspected Component:** {entity.SuspectedComponent}");
        sb.AppendLine($"**Created:** {entity.CreatedAt:u}");
        sb.AppendLine();

        sb.AppendLine("## Reason for Escalation");
        sb.AppendLine();
        sb.AppendLine(entity.Reason);
        sb.AppendLine();

        sb.AppendLine("## Customer Summary");
        sb.AppendLine();
        sb.AppendLine(entity.CustomerSummary);
        sb.AppendLine();

        sb.AppendLine("## Steps to Reproduce");
        sb.AppendLine();
        sb.AppendLine(entity.StepsToReproduce);
        sb.AppendLine();

        sb.AppendLine("## Logs / IDs Requested");
        sb.AppendLine();
        sb.AppendLine(entity.LogsIdsRequested);
        sb.AppendLine();

        sb.AppendLine("## Evidence Links");
        sb.AppendLine();
        var citations = DeserializeCitations(entity.EvidenceLinksJson);
        if (citations.Count > 0)
        {
            foreach (var c in citations)
            {
                sb.AppendLine($"- [{c.Title}]({c.SourceUrl}) — {c.Snippet} (Source: {c.SourceSystem}, Updated: {c.UpdatedAt:u})");
            }
        }
        else
        {
            sb.AppendLine("_No evidence links attached._");
        }
        sb.AppendLine();

        return sb.ToString();
    }

    private EscalationDraftResponse MapDraft(EscalationDraftEntity entity, PlaybookValidationResult? playbookValidation = null) => new()
    {
        DraftId = entity.Id,
        SessionId = entity.SessionId,
        MessageId = entity.MessageId,
        Title = entity.Title,
        CustomerSummary = entity.CustomerSummary,
        StepsToReproduce = entity.StepsToReproduce,
        LogsIdsRequested = entity.LogsIdsRequested,
        SuspectedComponent = entity.SuspectedComponent,
        Severity = entity.Severity,
        EvidenceLinks = DeserializeCitations(entity.EvidenceLinksJson),
        TargetTeam = entity.TargetTeam,
        Reason = entity.Reason,
        CreatedAt = entity.CreatedAt,
        ExportedAt = entity.ExportedAt,
        ApprovedAt = entity.ApprovedAt,
        ExternalId = entity.ExternalId,
        ExternalUrl = entity.ExternalUrl,
        ExternalStatus = entity.ExternalStatus,
        ExternalErrorDetail = entity.ExternalErrorDetail,
        TargetConnectorType = entity.TargetConnectorType?.ToString(),
        PlaybookValidation = playbookValidation,
    };

    private static IReadOnlyList<CitationDto> DeserializeCitations(string json, ILogger? logger = null) =>
        JsonDeserializeHelper.Deserialize<List<CitationDto>>(json, SharedJsonOptions.CamelCaseWrite, logger, []);
}
