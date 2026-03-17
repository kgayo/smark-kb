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

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public EscalationDraftService(
        SmartKbDbContext db,
        IAuditEventWriter auditWriter,
        EscalationSettings escalationSettings,
        ILogger<EscalationDraftService> logger)
    {
        _db = db;
        _auditWriter = auditWriter;
        _escalationSettings = escalationSettings;
        _logger = logger;
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
            Severity = ValidateSeverity(request.Severity),
            EvidenceLinksJson = JsonSerializer.Serialize(request.EvidenceLinks, JsonOpts),
            TargetTeam = targetTeam,
            Reason = request.Reason,
            CreatedAt = now,
        };

        _db.EscalationDrafts.Add(entity);
        await _db.SaveChangesAsync(ct);

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

        return MapDraft(entity);
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
            Drafts = drafts.Select(MapDraft).ToList(),
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
        if (request.Severity is not null) entity.Severity = ValidateSeverity(request.Severity);
        if (request.EvidenceLinks is not null) entity.EvidenceLinksJson = JsonSerializer.Serialize(request.EvidenceLinks, JsonOpts);
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

    private static string ValidateSeverity(string severity)
    {
        var normalized = severity.ToUpperInvariant();
        return EscalationSettings.SeverityOrder.Contains(normalized) ? normalized : "P3";
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

    private EscalationDraftResponse MapDraft(EscalationDraftEntity entity) => new()
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
    };

    private static IReadOnlyList<CitationDto> DeserializeCitations(string json)
    {
        if (string.IsNullOrEmpty(json)) return [];
        try
        {
            return JsonSerializer.Deserialize<List<CitationDto>>(json, JsonOpts) ?? [];
        }
        catch
        {
            return [];
        }
    }
}
