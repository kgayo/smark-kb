using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SmartKb.Contracts.Configuration;
using SmartKb.Contracts.Models;
using SmartKb.Contracts.Services;
using SmartKb.Data.Entities;
using SmartKb.Data.Exceptions;

namespace SmartKb.Data.Repositories;

public sealed class TenantCostSettingsService : ITenantCostSettingsService
{
    private readonly SmartKbDbContext _db;
    private readonly CostOptimizationSettings _defaults;
    private readonly ChatOrchestrationSettings _chatSettings;
    private readonly ILogger<TenantCostSettingsService> _logger;

    public TenantCostSettingsService(
        SmartKbDbContext db,
        CostOptimizationSettings defaults,
        ChatOrchestrationSettings chatSettings,
        ILogger<TenantCostSettingsService> logger)
    {
        _db = db;
        _defaults = defaults;
        _chatSettings = chatSettings;
        _logger = logger;
    }

    public async Task<CostSettingsResponse> GetSettingsAsync(string tenantId, CancellationToken ct = default)
    {
        var entity = await _db.TenantCostSettings
            .FirstOrDefaultAsync(s => s.TenantId == tenantId, ct);

        return BuildResponse(tenantId, entity);
    }

    public async Task<CostSettingsResponse> UpdateSettingsAsync(
        string tenantId, UpdateCostSettingsRequest request, CancellationToken ct = default)
    {
        const int maxRetries = 1;
        for (var attempt = 0; ; attempt++)
        {
            var entity = await _db.TenantCostSettings
                .FirstOrDefaultAsync(s => s.TenantId == tenantId, ct);

            var now = DateTimeOffset.UtcNow;

            if (entity is null)
            {
                entity = new TenantCostSettingsEntity
                {
                    Id = Guid.NewGuid(),
                    TenantId = tenantId,
                    CreatedAt = now,
                    UpdatedAt = now,
                };
                _db.TenantCostSettings.Add(entity);
            }
            else
            {
                entity.UpdatedAt = now;
            }

            if (request.DailyTokenBudget.HasValue) entity.DailyTokenBudget = request.DailyTokenBudget;
            if (request.MonthlyTokenBudget.HasValue) entity.MonthlyTokenBudget = request.MonthlyTokenBudget;
            if (request.MaxPromptTokensPerQuery.HasValue) entity.MaxPromptTokensPerQuery = request.MaxPromptTokensPerQuery;
            if (request.MaxEvidenceChunksInPrompt.HasValue) entity.MaxEvidenceChunksInPrompt = request.MaxEvidenceChunksInPrompt;
            if (request.EnableEmbeddingCache.HasValue) entity.EnableEmbeddingCache = request.EnableEmbeddingCache;
            if (request.EmbeddingCacheTtlHours.HasValue) entity.EmbeddingCacheTtlHours = request.EmbeddingCacheTtlHours;
            if (request.EnableRetrievalCompression.HasValue) entity.EnableRetrievalCompression = request.EnableRetrievalCompression;
            if (request.MaxChunkCharsCompressed.HasValue) entity.MaxChunkCharsCompressed = request.MaxChunkCharsCompressed;
            if (request.BudgetAlertThresholdPercent.HasValue) entity.BudgetAlertThresholdPercent = request.BudgetAlertThresholdPercent;

            try
            {
                await _db.SaveChangesAsync(ct);
            }
            catch (DbUpdateConcurrencyException) when (attempt < maxRetries)
            {
                _logger.LogWarning("Concurrency conflict updating cost settings for tenant {TenantId}, retrying.", tenantId);
                ReloadTrackedEntities();
                continue;
            }

            _logger.LogInformation("Tenant cost settings updated. TenantId={TenantId}", tenantId);
            return BuildResponse(tenantId, entity);
        }
    }

    public async Task<bool> ResetSettingsAsync(string tenantId, CancellationToken ct = default)
    {
        var entity = await _db.TenantCostSettings
            .FirstOrDefaultAsync(s => s.TenantId == tenantId, ct);

        if (entity is null) return false;

        _db.TenantCostSettings.Remove(entity);
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation("Tenant cost settings reset to defaults. TenantId={TenantId}", tenantId);

        return true;
    }

    private CostSettingsResponse BuildResponse(string tenantId, TenantCostSettingsEntity? entity)
    {
        return new CostSettingsResponse
        {
            TenantId = tenantId,
            DailyTokenBudget = entity?.DailyTokenBudget,
            MonthlyTokenBudget = entity?.MonthlyTokenBudget,
            MaxPromptTokensPerQuery = entity?.MaxPromptTokensPerQuery,
            MaxEvidenceChunksInPrompt = entity?.MaxEvidenceChunksInPrompt ?? _chatSettings.MaxEvidenceChunksInPrompt,
            EnableEmbeddingCache = entity?.EnableEmbeddingCache ?? _defaults.EnableEmbeddingCache,
            EmbeddingCacheTtlHours = entity?.EmbeddingCacheTtlHours ?? _defaults.EmbeddingCacheTtlHours,
            EnableRetrievalCompression = entity?.EnableRetrievalCompression ?? _defaults.EnableRetrievalCompression,
            MaxChunkCharsCompressed = entity?.MaxChunkCharsCompressed ?? _defaults.MaxChunkCharsCompressed,
            BudgetAlertThresholdPercent = entity?.BudgetAlertThresholdPercent ?? _defaults.BudgetAlertThresholdPercent,
            HasOverrides = entity is not null,
        };
    }

    private void ReloadTrackedEntities()
    {
        foreach (var entry in _db.ChangeTracker.Entries())
            entry.Reload();
    }
}
