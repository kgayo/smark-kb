using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SmartKb.Contracts.Configuration;
using SmartKb.Contracts.Models;
using SmartKb.Contracts.Services;
using SmartKb.Data.Entities;

namespace SmartKb.Data.Repositories;

public sealed class TenantRetrievalSettingsService : ITenantRetrievalSettingsService
{
    private readonly SmartKbDbContext _db;
    private readonly RetrievalSettings _defaults;
    private readonly ILogger<TenantRetrievalSettingsService> _logger;

    public TenantRetrievalSettingsService(
        SmartKbDbContext db,
        RetrievalSettings defaults,
        ILogger<TenantRetrievalSettingsService> logger)
    {
        _db = db;
        _defaults = defaults;
        _logger = logger;
    }

    public async Task<RetrievalSettingsResponse> GetSettingsAsync(string tenantId, CancellationToken ct = default)
    {
        var entity = await _db.TenantRetrievalSettings
            .FirstOrDefaultAsync(s => s.TenantId == tenantId, ct);

        return BuildResponse(tenantId, entity);
    }

    public async Task<RetrievalSettingsResponse> UpdateSettingsAsync(
        string tenantId, UpdateRetrievalSettingsRequest request, CancellationToken ct = default)
    {
        const int maxRetries = 1;
        for (var attempt = 0; ; attempt++)
        {
            var entity = await _db.TenantRetrievalSettings
                .FirstOrDefaultAsync(s => s.TenantId == tenantId, ct);

            var now = DateTimeOffset.UtcNow;

            if (entity is null)
            {
                entity = new TenantRetrievalSettingsEntity
                {
                    Id = Guid.NewGuid(),
                    TenantId = tenantId,
                    CreatedAt = now,
                    UpdatedAt = now,
                };
                _db.TenantRetrievalSettings.Add(entity);
            }
            else
            {
                entity.UpdatedAt = now;
            }

            // Apply non-null overrides from request.
            if (request.TopK.HasValue) entity.TopK = request.TopK;
            if (request.EnableSemanticReranking.HasValue) entity.EnableSemanticReranking = request.EnableSemanticReranking;
            if (request.EnablePatternFusion.HasValue) entity.EnablePatternFusion = request.EnablePatternFusion;
            if (request.PatternTopK.HasValue) entity.PatternTopK = request.PatternTopK;
            if (request.TrustBoostApproved.HasValue) entity.TrustBoostApproved = request.TrustBoostApproved;
            if (request.TrustBoostReviewed.HasValue) entity.TrustBoostReviewed = request.TrustBoostReviewed;
            if (request.TrustBoostDraft.HasValue) entity.TrustBoostDraft = request.TrustBoostDraft;
            if (request.RecencyBoostRecent.HasValue) entity.RecencyBoostRecent = request.RecencyBoostRecent;
            if (request.RecencyBoostOld.HasValue) entity.RecencyBoostOld = request.RecencyBoostOld;
            if (request.PatternAuthorityBoost.HasValue) entity.PatternAuthorityBoost = request.PatternAuthorityBoost;
            if (request.DiversityMaxPerSource.HasValue) entity.DiversityMaxPerSource = request.DiversityMaxPerSource;
            if (request.NoEvidenceScoreThreshold.HasValue) entity.NoEvidenceScoreThreshold = request.NoEvidenceScoreThreshold;
            if (request.NoEvidenceMinResults.HasValue) entity.NoEvidenceMinResults = request.NoEvidenceMinResults;

            try
            {
                await _db.SaveChangesAsync(ct);
            }
            catch (DbUpdateConcurrencyException) when (attempt < maxRetries)
            {
                _logger.LogWarning("Concurrency conflict updating retrieval settings for tenant {TenantId}, retrying.", tenantId);
                ReloadTrackedEntities();
                continue;
            }

            _logger.LogInformation(
                "Tenant retrieval settings updated. TenantId={TenantId}",
                tenantId);

            return BuildResponse(tenantId, entity);
        }
    }

    public async Task<bool> ResetSettingsAsync(string tenantId, CancellationToken ct = default)
    {
        var entity = await _db.TenantRetrievalSettings
            .FirstOrDefaultAsync(s => s.TenantId == tenantId, ct);

        if (entity is null) return false;

        _db.TenantRetrievalSettings.Remove(entity);
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation(
            "Tenant retrieval settings reset to defaults. TenantId={TenantId}",
            tenantId);

        return true;
    }

    private RetrievalSettingsResponse BuildResponse(string tenantId, TenantRetrievalSettingsEntity? entity)
    {
        return new RetrievalSettingsResponse
        {
            TenantId = tenantId,
            TopK = entity?.TopK ?? _defaults.TopK,
            EnableSemanticReranking = entity?.EnableSemanticReranking ?? _defaults.EnableSemanticReranking,
            EnablePatternFusion = entity?.EnablePatternFusion ?? _defaults.EnablePatternFusion,
            PatternTopK = entity?.PatternTopK ?? _defaults.PatternTopK,
            TrustBoostApproved = entity?.TrustBoostApproved ?? _defaults.TrustBoostApproved,
            TrustBoostReviewed = entity?.TrustBoostReviewed ?? _defaults.TrustBoostReviewed,
            TrustBoostDraft = entity?.TrustBoostDraft ?? _defaults.TrustBoostDraft,
            RecencyBoostRecent = entity?.RecencyBoostRecent ?? _defaults.RecencyBoostRecent,
            RecencyBoostOld = entity?.RecencyBoostOld ?? _defaults.RecencyBoostOld,
            PatternAuthorityBoost = entity?.PatternAuthorityBoost ?? _defaults.PatternAuthorityBoost,
            DiversityMaxPerSource = entity?.DiversityMaxPerSource ?? _defaults.DiversityMaxPerSource,
            NoEvidenceScoreThreshold = entity?.NoEvidenceScoreThreshold ?? _defaults.NoEvidenceScoreThreshold,
            NoEvidenceMinResults = entity?.NoEvidenceMinResults ?? _defaults.NoEvidenceMinResults,
            HasOverrides = entity is not null,
        };
    }

    private void ReloadTrackedEntities()
    {
        foreach (var entry in _db.ChangeTracker.Entries())
            entry.Reload();
    }
}
