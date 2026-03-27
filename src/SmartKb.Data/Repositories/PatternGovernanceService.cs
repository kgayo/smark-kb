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
        int page = 1, int pageSize = PaginationDefaults.DefaultPageSize, CancellationToken ct = default)
    {
        page = Math.Max(1, page);
        pageSize = PaginationDefaults.ClampPageSize(pageSize);

        var query = _db.CasePatterns
            .Where(p => p.TenantId == tenantId);

        if (!string.IsNullOrEmpty(trustLevel))
            query = query.Where(p => p.TrustLevel == trustLevel);

        if (!string.IsNullOrEmpty(productArea))
            query = query.Where(p => p.ProductArea == productArea);

        var totalCount = await query.CountAsync(ct);

        var entities = await query
            .OrderByDescending(p => p.CreatedAtEpoch)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        var patterns = entities.Select(p => new PatternSummary
        {
            Id = p.Id,
            PatternId = p.PatternId,
            Title = p.Title,
            ProblemStatement = p.ProblemStatement.Truncate(TruncationLimits.SnippetPreview, "..."),
            TrustLevel = p.TrustLevel,
            Confidence = p.Confidence,
            Version = p.Version,
            ProductArea = p.ProductArea,
            Tags = JsonDeserializeHelper.DeserializeStringList(p.TagsJson),
            SupersedesPatternId = p.SupersedesPatternId,
            SourceUrl = p.SourceUrl,
            RelatedEvidenceCount = JsonDeserializeHelper.DeserializeStringList(p.RelatedEvidenceIdsJson).Count,
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

        // Capture previous values before mutation for version history (P3-013).
        var previousValues = CaptureGovernanceSnapshot(entity, targetLevel);

        entity.TrustLevel = targetLevel.ToString();
        entity.UpdatedAt = now;
        applyGovernanceFields(entity, now);

        // Record version history entry (P3-013).
        var changedFields = previousValues.Keys.ToList();
        var historyEntry = new PatternVersionHistoryEntity
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            PatternId = patternId,
            Version = entity.Version,
            ChangedBy = actorId,
            ChangedAt = now,
            ChangedFieldsJson = JsonSerializer.Serialize(changedFields, SharedJsonOptions.CamelCaseWrite),
            PreviousValuesJson = JsonSerializer.Serialize(previousValues, SharedJsonOptions.CamelCaseWrite),
            ChangeType = "trust_transition",
            Summary = $"{previousLevel} → {targetLevel}",
        };
        _db.PatternVersionHistory.Add(historyEntry);

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
                        RootCause = entity.RootCause,
                        Symptoms = JsonDeserializeHelper.DeserializeStringList(entity.SymptomsJson),
                        DiagnosisSteps = JsonDeserializeHelper.DeserializeStringList(entity.DiagnosisStepsJson),
                        ResolutionSteps = JsonDeserializeHelper.DeserializeStringList(entity.ResolutionStepsJson),
                        VerificationSteps = JsonDeserializeHelper.DeserializeStringList(entity.VerificationStepsJson),
                        EscalationCriteria = JsonDeserializeHelper.DeserializeStringList(entity.EscalationCriteriaJson),
                        RelatedEvidenceIds = JsonDeserializeHelper.DeserializeStringList(entity.RelatedEvidenceIdsJson),
                        Confidence = entity.Confidence,
                        TrustLevel = targetLevel,
                        Version = entity.Version,
                        ProductArea = entity.ProductArea,
                        Tags = JsonDeserializeHelper.DeserializeStringList(entity.TagsJson),
                        Visibility = Enum.TryParse<AccessVisibility>(entity.Visibility, out var v) ? v : AccessVisibility.Internal,
                        AllowedGroups = JsonDeserializeHelper.DeserializeStringList(entity.AllowedGroupsJson),
                        AccessLabel = entity.AccessLabel,
                        SourceUrl = entity.SourceUrl,
                        CreatedAt = entity.CreatedAt,
                        UpdatedAt = entity.UpdatedAt,
                    };
                    await _patternIndexing.IndexPatternsAsync([model], ct);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
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
        RootCause = entity.RootCause,
        Symptoms = JsonDeserializeHelper.DeserializeStringList(entity.SymptomsJson),
        DiagnosisSteps = JsonDeserializeHelper.DeserializeStringList(entity.DiagnosisStepsJson),
        ResolutionSteps = JsonDeserializeHelper.DeserializeStringList(entity.ResolutionStepsJson),
        VerificationSteps = JsonDeserializeHelper.DeserializeStringList(entity.VerificationStepsJson),
        Workaround = entity.Workaround,
        EscalationCriteria = JsonDeserializeHelper.DeserializeStringList(entity.EscalationCriteriaJson),
        EscalationTargetTeam = entity.EscalationTargetTeam,
        RelatedEvidenceIds = JsonDeserializeHelper.DeserializeStringList(entity.RelatedEvidenceIdsJson),
        Confidence = entity.Confidence,
        TrustLevel = entity.TrustLevel,
        Version = entity.Version,
        SupersedesPatternId = entity.SupersedesPatternId,
        ApplicabilityConstraints = JsonDeserializeHelper.DeserializeStringList(entity.ApplicabilityConstraintsJson),
        Exclusions = JsonDeserializeHelper.DeserializeStringList(entity.ExclusionsJson),
        ProductArea = entity.ProductArea,
        Tags = JsonDeserializeHelper.DeserializeStringList(entity.TagsJson),
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

    public async Task<PatternVersionHistoryResponse?> GetPatternHistoryAsync(
        string tenantId, string patternId, CancellationToken ct = default)
    {
        // Verify pattern exists in this tenant.
        var exists = await _db.CasePatterns
            .AnyAsync(p => p.TenantId == tenantId && p.PatternId == patternId, ct);

        if (!exists)
            return null;

        var entries = await _db.PatternVersionHistory
            .Where(h => h.TenantId == tenantId && h.PatternId == patternId)
            .ToListAsync(ct);

        // Order client-side for SQLite compatibility.
        var ordered = entries
            .OrderByDescending(h => h.ChangedAt)
            .ToList();

        var mapped = ordered.Select(h => new PatternVersionHistoryEntry
        {
            Id = h.Id,
            PatternId = h.PatternId,
            Version = h.Version,
            ChangedBy = h.ChangedBy,
            ChangedAt = h.ChangedAt,
            ChangedFields = JsonDeserializeHelper.DeserializeStringList(h.ChangedFieldsJson),
            PreviousValues = JsonDeserializeHelper.DeserializeStringDictionary(h.PreviousValuesJson),
            ChangeType = h.ChangeType,
            Summary = h.Summary,
        }).ToList();

        return new PatternVersionHistoryResponse
        {
            PatternId = patternId,
            Entries = mapped,
            TotalCount = mapped.Count,
        };
    }

    /// <summary>
    /// Captures the governance-relevant fields that will change during a trust transition.
    /// Returns a dictionary of field name → previous value (as string).
    /// </summary>
    internal static Dictionary<string, string?> CaptureGovernanceSnapshot(
        CasePatternEntity entity, TrustLevel targetLevel)
    {
        var previous = new Dictionary<string, string?>
        {
            ["TrustLevel"] = entity.TrustLevel,
        };

        switch (targetLevel)
        {
            case TrustLevel.Reviewed:
                previous["ReviewedAt"] = entity.ReviewedAt?.ToString("O");
                previous["ReviewedBy"] = entity.ReviewedBy;
                previous["ReviewNotes"] = entity.ReviewNotes;
                break;
            case TrustLevel.Approved:
                previous["ApprovedAt"] = entity.ApprovedAt?.ToString("O");
                previous["ApprovedBy"] = entity.ApprovedBy;
                previous["ApprovalNotes"] = entity.ApprovalNotes;
                break;
            case TrustLevel.Deprecated:
                previous["DeprecatedAt"] = entity.DeprecatedAt?.ToString("O");
                previous["DeprecatedBy"] = entity.DeprecatedBy;
                previous["DeprecationReason"] = entity.DeprecationReason;
                break;
        }

        return previous;
    }

}
