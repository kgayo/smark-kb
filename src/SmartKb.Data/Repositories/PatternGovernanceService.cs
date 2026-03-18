using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SmartKb.Contracts;
using SmartKb.Contracts.Enums;
using SmartKb.Contracts.Models;
using SmartKb.Contracts.Services;
using SmartKb.Data.Entities;

namespace SmartKb.Data.Repositories;

/// <summary>
/// P1-006: Pattern governance workflows — trust-level transitions, governance queue,
/// pattern detail retrieval. Enforces valid state transitions and writes audit events.
/// </summary>
public sealed class PatternGovernanceService : IPatternGovernanceService
{
    private readonly SmartKbDbContext _db;
    private readonly IAuditEventWriter _auditWriter;
    private readonly IPatternIndexingService? _patternIndexing;
    private readonly ILogger<PatternGovernanceService> _logger;

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    // Valid trust-level transitions.
    private static readonly Dictionary<TrustLevel, HashSet<TrustLevel>> ValidTransitions = new()
    {
        [TrustLevel.Draft] = [TrustLevel.Reviewed, TrustLevel.Approved, TrustLevel.Deprecated],
        [TrustLevel.Reviewed] = [TrustLevel.Approved, TrustLevel.Deprecated],
        [TrustLevel.Approved] = [TrustLevel.Deprecated],
        [TrustLevel.Deprecated] = [],
    };

    public PatternGovernanceService(
        SmartKbDbContext db,
        IAuditEventWriter auditWriter,
        ILogger<PatternGovernanceService> logger,
        IPatternIndexingService? patternIndexing = null)
    {
        _db = db;
        _auditWriter = auditWriter;
        _logger = logger;
        _patternIndexing = patternIndexing;
    }

    public async Task<PatternGovernanceQueueResponse> GetGovernanceQueueAsync(
        string tenantId, string? trustLevel = null, string? productArea = null,
        int page = 1, int pageSize = 20, CancellationToken ct = default)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);

        var query = _db.CasePatterns
            .Where(p => p.TenantId == tenantId);

        if (!string.IsNullOrEmpty(trustLevel))
            query = query.Where(p => p.TrustLevel == trustLevel);

        if (!string.IsNullOrEmpty(productArea))
            query = query.Where(p => p.ProductArea == productArea);

        var totalCount = await query.CountAsync(ct);

        // Materialize then order/page client-side (SQLite doesn't support DateTimeOffset in ORDER BY).
        var allEntities = await query.ToListAsync(ct);
        var entities = allEntities
            .OrderByDescending(p => p.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToList();

        var patterns = entities.Select(p => new PatternSummary
        {
            Id = p.Id,
            PatternId = p.PatternId,
            Title = p.Title,
            ProblemStatement = p.ProblemStatement.Length > 200
                ? p.ProblemStatement[..200] + "..."
                : p.ProblemStatement,
            TrustLevel = p.TrustLevel,
            Confidence = p.Confidence,
            Version = p.Version,
            ProductArea = p.ProductArea,
            Tags = DeserializeStringList(p.TagsJson),
            SupersedesPatternId = p.SupersedesPatternId,
            SourceUrl = p.SourceUrl,
            RelatedEvidenceCount = DeserializeStringList(p.RelatedEvidenceIdsJson).Count,
            QualityScore = p.QualityScore,
            CreatedAt = p.CreatedAt,
            UpdatedAt = p.UpdatedAt,
            ReviewedBy = p.ReviewedBy,
            ReviewedAt = p.ReviewedAt,
            ApprovedBy = p.ApprovedBy,
            ApprovedAt = p.ApprovedAt,
            DeprecatedBy = p.DeprecatedBy,
            DeprecatedAt = p.DeprecatedAt,
            DeprecationReason = p.DeprecationReason,
        }).ToList();

        return new PatternGovernanceQueueResponse
        {
            Patterns = patterns,
            TotalCount = totalCount,
            Page = page,
            PageSize = pageSize,
            HasMore = (page * pageSize) < totalCount,
        };
    }

    public async Task<PatternDetail?> GetPatternDetailAsync(
        string tenantId, string patternId, CancellationToken ct = default)
    {
        var entity = await _db.CasePatterns
            .Where(p => p.TenantId == tenantId && p.PatternId == patternId)
            .FirstOrDefaultAsync(ct);

        if (entity is null)
            return null;

        return MapToDetail(entity);
    }

    public async Task<PatternGovernanceResult?> ReviewPatternAsync(
        string tenantId, string patternId, string actorId, string correlationId,
        ReviewPatternRequest request, CancellationToken ct = default)
    {
        return await TransitionAsync(
            tenantId, patternId, actorId, correlationId,
            TrustLevel.Reviewed,
            AuditEventTypes.PatternReviewed,
            (entity, now) =>
            {
                entity.ReviewedAt = now;
                entity.ReviewedBy = actorId;
                entity.ReviewNotes = request.Notes;
            },
            request.Notes,
            ct);
    }

    public async Task<PatternGovernanceResult?> ApprovePatternAsync(
        string tenantId, string patternId, string actorId, string correlationId,
        ApprovePatternRequest request, CancellationToken ct = default)
    {
        return await TransitionAsync(
            tenantId, patternId, actorId, correlationId,
            TrustLevel.Approved,
            AuditEventTypes.PatternApproved,
            (entity, now) =>
            {
                entity.ApprovedAt = now;
                entity.ApprovedBy = actorId;
                entity.ApprovalNotes = request.Notes;
            },
            request.Notes,
            ct);
    }

    public async Task<PatternGovernanceResult?> DeprecatePatternAsync(
        string tenantId, string patternId, string actorId, string correlationId,
        DeprecatePatternRequest request, CancellationToken ct = default)
    {
        return await TransitionAsync(
            tenantId, patternId, actorId, correlationId,
            TrustLevel.Deprecated,
            AuditEventTypes.PatternDeprecated,
            (entity, now) =>
            {
                entity.DeprecatedAt = now;
                entity.DeprecatedBy = actorId;
                entity.DeprecationReason = request.Reason;
                if (!string.IsNullOrEmpty(request.SupersedingPatternId))
                    entity.SupersedesPatternId = request.SupersedingPatternId;
            },
            request.Reason,
            ct);
    }

    private async Task<PatternGovernanceResult?> TransitionAsync(
        string tenantId, string patternId, string actorId, string correlationId,
        TrustLevel targetLevel, string auditEventType,
        Action<CasePatternEntity, DateTimeOffset> applyGovernanceFields,
        string? detail,
        CancellationToken ct = default)
    {
        var entity = await _db.CasePatterns
            .Where(p => p.TenantId == tenantId && p.PatternId == patternId)
            .FirstOrDefaultAsync(ct);

        if (entity is null)
            return null;

        var currentLevel = Enum.TryParse<TrustLevel>(entity.TrustLevel, out var cl)
            ? cl : TrustLevel.Draft;

        if (!ValidTransitions.TryGetValue(currentLevel, out var allowed) || !allowed.Contains(targetLevel))
        {
            _logger.LogWarning(
                "Invalid trust-level transition {Current} → {Target} for pattern {PatternId} in tenant {TenantId}",
                currentLevel, targetLevel, patternId, tenantId);
            return null;
        }

        var now = DateTimeOffset.UtcNow;
        var previousLevel = entity.TrustLevel;

        entity.TrustLevel = targetLevel.ToString();
        entity.UpdatedAt = now;
        applyGovernanceFields(entity, now);

        await _db.SaveChangesAsync(ct);

        await _auditWriter.WriteAsync(new AuditEvent(
            EventId: patternId,
            EventType: auditEventType,
            TenantId: tenantId,
            ActorId: actorId,
            CorrelationId: correlationId,
            Timestamp: now,
            Detail: $"Pattern {patternId} transitioned {previousLevel} → {targetLevel}" +
                    (string.IsNullOrEmpty(detail) ? "" : $": {detail}")), ct);

        _logger.LogInformation(
            "Pattern {PatternId} transitioned {Previous} → {Target} by {ActorId} in tenant {TenantId}",
            patternId, previousLevel, targetLevel, actorId, tenantId);

        // Update the search index after governance transition.
        if (_patternIndexing is not null)
        {
            try
            {
                if (targetLevel == TrustLevel.Deprecated)
                {
                    // Remove deprecated patterns from the search index entirely.
                    await _patternIndexing.DeletePatternsAsync([entity.PatternId], ct);
                }
                else
                {
                    // Re-index the pattern with updated trust level.
                    var model = new CasePattern
                    {
                        PatternId = entity.PatternId,
                        TenantId = entity.TenantId,
                        Title = entity.Title,
                        ProblemStatement = entity.ProblemStatement,
                        Symptoms = DeserializeStringList(entity.SymptomsJson),
                        DiagnosisSteps = DeserializeStringList(entity.DiagnosisStepsJson),
                        ResolutionSteps = DeserializeStringList(entity.ResolutionStepsJson),
                        VerificationSteps = DeserializeStringList(entity.VerificationStepsJson),
                        EscalationCriteria = DeserializeStringList(entity.EscalationCriteriaJson),
                        RelatedEvidenceIds = DeserializeStringList(entity.RelatedEvidenceIdsJson),
                        Confidence = entity.Confidence,
                        TrustLevel = targetLevel,
                        Version = entity.Version,
                        ProductArea = entity.ProductArea,
                        Tags = DeserializeStringList(entity.TagsJson),
                        Visibility = Enum.TryParse<AccessVisibility>(entity.Visibility, out var v) ? v : AccessVisibility.Internal,
                        AllowedGroups = DeserializeStringList(entity.AllowedGroupsJson),
                        AccessLabel = entity.AccessLabel,
                        SourceUrl = entity.SourceUrl,
                        CreatedAt = entity.CreatedAt,
                        UpdatedAt = entity.UpdatedAt,
                    };
                    await _patternIndexing.IndexPatternsAsync([model], ct);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to update search index for pattern {PatternId} after governance transition", patternId);
            }
        }

        return new PatternGovernanceResult
        {
            PatternId = patternId,
            PreviousTrustLevel = previousLevel,
            NewTrustLevel = targetLevel.ToString(),
            TransitionedBy = actorId,
            TransitionedAt = now,
        };
    }

    private static PatternDetail MapToDetail(CasePatternEntity entity) => new()
    {
        Id = entity.Id,
        PatternId = entity.PatternId,
        TenantId = entity.TenantId,
        Title = entity.Title,
        ProblemStatement = entity.ProblemStatement,
        Symptoms = DeserializeStringList(entity.SymptomsJson),
        DiagnosisSteps = DeserializeStringList(entity.DiagnosisStepsJson),
        ResolutionSteps = DeserializeStringList(entity.ResolutionStepsJson),
        VerificationSteps = DeserializeStringList(entity.VerificationStepsJson),
        Workaround = entity.Workaround,
        EscalationCriteria = DeserializeStringList(entity.EscalationCriteriaJson),
        EscalationTargetTeam = entity.EscalationTargetTeam,
        RelatedEvidenceIds = DeserializeStringList(entity.RelatedEvidenceIdsJson),
        Confidence = entity.Confidence,
        TrustLevel = entity.TrustLevel,
        Version = entity.Version,
        SupersedesPatternId = entity.SupersedesPatternId,
        ApplicabilityConstraints = DeserializeStringList(entity.ApplicabilityConstraintsJson),
        Exclusions = DeserializeStringList(entity.ExclusionsJson),
        ProductArea = entity.ProductArea,
        Tags = DeserializeStringList(entity.TagsJson),
        Visibility = entity.Visibility,
        AccessLabel = entity.AccessLabel,
        SourceUrl = entity.SourceUrl,
        QualityScore = entity.QualityScore,
        CreatedAt = entity.CreatedAt,
        UpdatedAt = entity.UpdatedAt,
        ReviewedBy = entity.ReviewedBy,
        ReviewedAt = entity.ReviewedAt,
        ReviewNotes = entity.ReviewNotes,
        ApprovedBy = entity.ApprovedBy,
        ApprovedAt = entity.ApprovedAt,
        ApprovalNotes = entity.ApprovalNotes,
        DeprecatedBy = entity.DeprecatedBy,
        DeprecatedAt = entity.DeprecatedAt,
        DeprecationReason = entity.DeprecationReason,
    };

    private static IReadOnlyList<string> DeserializeStringList(string? json)
    {
        if (string.IsNullOrEmpty(json)) return [];
        try
        {
            return JsonSerializer.Deserialize<List<string>>(json, JsonOpts) ?? [];
        }
        catch
        {
            return [];
        }
    }
}
