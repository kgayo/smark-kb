using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SmartKb.Contracts.Configuration;
using SmartKb.Contracts.Models;
using SmartKb.Contracts.Services;
using SmartKb.Data.Entities;

namespace SmartKb.Data.Repositories;

public sealed class TokenUsageService : ITokenUsageService
{
    private readonly SmartKbDbContext _db;
    private readonly CostOptimizationSettings _costSettings;
    private readonly ILogger<TokenUsageService> _logger;

    public TokenUsageService(
        SmartKbDbContext db,
        CostOptimizationSettings costSettings,
        ILogger<TokenUsageService> logger)
    {
        _db = db;
        _costSettings = costSettings;
        _logger = logger;
    }

    public async Task RecordUsageAsync(
        string tenantId,
        string userId,
        string correlationId,
        TokenUsageRecord usage,
        CancellationToken ct = default)
    {
        var now = DateTimeOffset.UtcNow;
        var entity = new TokenUsageEntity
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            UserId = userId,
            CorrelationId = correlationId,
            PromptTokens = usage.PromptTokens,
            CompletionTokens = usage.CompletionTokens,
            TotalTokens = usage.TotalTokens,
            EmbeddingTokens = usage.EmbeddingTokens,
            EmbeddingCacheHit = usage.EmbeddingCacheHit,
            EvidenceChunksUsed = usage.EvidenceChunksUsed,
            EstimatedCostUsd = usage.EstimatedCostUsd,
            CreatedAt = now,
            CreatedAtEpoch = now.ToUnixTimeSeconds(),
        };

        _db.TokenUsages.Add(entity);
        await _db.SaveChangesAsync(ct);

        _logger.LogDebug(
            "Token usage recorded. TenantId={TenantId}, CorrelationId={CorrelationId}, TotalTokens={TotalTokens}, CostUsd={CostUsd}",
            tenantId, correlationId, usage.TotalTokens, usage.EstimatedCostUsd);
    }

    public async Task<TokenUsageSummary> GetSummaryAsync(
        string tenantId,
        DateTimeOffset periodStart,
        DateTimeOffset periodEnd,
        CancellationToken ct = default)
    {
        var periodStartEpoch = periodStart.ToUnixTimeSeconds();
        var periodEndEpoch = periodEnd.ToUnixTimeSeconds();

        var usages = await _db.TokenUsages
            .Where(u => u.TenantId == tenantId
                        && u.CreatedAtEpoch >= periodStartEpoch
                        && u.CreatedAtEpoch < periodEndEpoch)
            .ToListAsync(ct);

        var costSettings = await _db.TenantCostSettings
            .FirstOrDefaultAsync(s => s.TenantId == tenantId, ct);

        var totalPrompt = usages.Sum(u => (long)u.PromptTokens);
        var totalCompletion = usages.Sum(u => (long)u.CompletionTokens);
        var totalTokens = usages.Sum(u => (long)u.TotalTokens);
        var totalEmbedding = usages.Sum(u => (long)u.EmbeddingTokens);
        var cacheHits = usages.Count(u => u.EmbeddingCacheHit);
        var cacheMisses = usages.Count(u => !u.EmbeddingCacheHit);
        var totalCost = usages.Sum(u => u.EstimatedCostUsd);

        var dailyBudget = costSettings?.DailyTokenBudget;
        var monthlyBudget = costSettings?.MonthlyTokenBudget;

        var todayStartEpoch = new DateTimeOffset(DateTimeOffset.UtcNow.Date, TimeSpan.Zero).ToUnixTimeSeconds();
        var monthStartEpoch = new DateTimeOffset(DateTimeOffset.UtcNow.Year, DateTimeOffset.UtcNow.Month, 1, 0, 0, 0, TimeSpan.Zero).ToUnixTimeSeconds();

        var dailyTokens = await _db.TokenUsages
            .Where(u => u.TenantId == tenantId && u.CreatedAtEpoch >= todayStartEpoch)
            .SumAsync(u => (long)u.TotalTokens, ct);

        var monthlyTokens = await _db.TokenUsages
            .Where(u => u.TenantId == tenantId && u.CreatedAtEpoch >= monthStartEpoch)
            .SumAsync(u => (long)u.TotalTokens, ct);

        return new TokenUsageSummary
        {
            TenantId = tenantId,
            PeriodStart = periodStart,
            PeriodEnd = periodEnd,
            TotalPromptTokens = totalPrompt,
            TotalCompletionTokens = totalCompletion,
            TotalTokens = totalTokens,
            TotalEmbeddingTokens = totalEmbedding,
            TotalRequests = usages.Count,
            EmbeddingCacheHits = cacheHits,
            EmbeddingCacheMisses = cacheMisses,
            TotalEstimatedCostUsd = totalCost,
            DailyTokenBudget = dailyBudget,
            MonthlyTokenBudget = monthlyBudget,
            DailyBudgetUtilizationPercent = dailyBudget.HasValue && dailyBudget.Value > 0
                ? (float)dailyTokens / dailyBudget.Value * 100f
                : 0f,
            MonthlyBudgetUtilizationPercent = monthlyBudget.HasValue && monthlyBudget.Value > 0
                ? (float)monthlyTokens / monthlyBudget.Value * 100f
                : 0f,
        };
    }

    public async Task<IReadOnlyList<DailyUsageBreakdown>> GetDailyBreakdownAsync(
        string tenantId,
        DateTimeOffset periodStart,
        DateTimeOffset periodEnd,
        CancellationToken ct = default)
    {
        var periodStartEpoch = periodStart.ToUnixTimeSeconds();
        var periodEndEpoch = periodEnd.ToUnixTimeSeconds();

        var usages = await _db.TokenUsages
            .Where(u => u.TenantId == tenantId
                        && u.CreatedAtEpoch >= periodStartEpoch
                        && u.CreatedAtEpoch < periodEndEpoch)
            .ToListAsync(ct);

        return usages
            .GroupBy(u => DateOnly.FromDateTime(u.CreatedAt.UtcDateTime.Date))
            .Select(g => new DailyUsageBreakdown
            {
                Date = g.Key,
                PromptTokens = g.Sum(u => (long)u.PromptTokens),
                CompletionTokens = g.Sum(u => (long)u.CompletionTokens),
                TotalTokens = g.Sum(u => (long)u.TotalTokens),
                EmbeddingTokens = g.Sum(u => (long)u.EmbeddingTokens),
                RequestCount = g.Count(),
                CacheHits = g.Count(u => u.EmbeddingCacheHit),
                EstimatedCostUsd = g.Sum(u => u.EstimatedCostUsd),
            })
            .OrderBy(d => d.Date)
            .ToList();
    }

    public async Task<BudgetCheckResult> CheckBudgetAsync(
        string tenantId,
        CancellationToken ct = default)
    {
        var costSettings = await _db.TenantCostSettings
            .FirstOrDefaultAsync(s => s.TenantId == tenantId, ct);

        var dailyBudget = costSettings?.DailyTokenBudget;
        var monthlyBudget = costSettings?.MonthlyTokenBudget;
        var alertThreshold = costSettings?.BudgetAlertThresholdPercent ?? _costSettings.BudgetAlertThresholdPercent;

        if (!dailyBudget.HasValue && !monthlyBudget.HasValue)
        {
            return new BudgetCheckResult
            {
                Allowed = true,
                DenialReason = null,
                DailyUtilizationPercent = 0f,
                MonthlyUtilizationPercent = 0f,
                BudgetWarning = false,
                WarningMessage = null,
            };
        }

        var todayStartEpoch = new DateTimeOffset(DateTimeOffset.UtcNow.Date, TimeSpan.Zero).ToUnixTimeSeconds();
        var monthStartEpoch = new DateTimeOffset(DateTimeOffset.UtcNow.Year, DateTimeOffset.UtcNow.Month, 1, 0, 0, 0, TimeSpan.Zero).ToUnixTimeSeconds();

        float dailyUtil = 0f;
        float monthlyUtil = 0f;

        if (dailyBudget.HasValue && dailyBudget.Value > 0)
        {
            var dailyTokens = await _db.TokenUsages
                .Where(u => u.TenantId == tenantId && u.CreatedAtEpoch >= todayStartEpoch)
                .SumAsync(u => (long)u.TotalTokens, ct);
            dailyUtil = (float)dailyTokens / dailyBudget.Value * 100f;

            if (dailyTokens >= dailyBudget.Value)
            {
                _logger.LogWarning(
                    "Daily token budget exceeded. TenantId={TenantId}, Used={Used}, Budget={Budget}",
                    tenantId, dailyTokens, dailyBudget.Value);

                return new BudgetCheckResult
                {
                    Allowed = false,
                    DenialReason = $"Daily token budget exceeded ({dailyTokens:N0}/{dailyBudget.Value:N0} tokens).",
                    DailyUtilizationPercent = dailyUtil,
                    MonthlyUtilizationPercent = monthlyUtil,
                    BudgetWarning = true,
                    WarningMessage = null,
                };
            }
        }

        if (monthlyBudget.HasValue && monthlyBudget.Value > 0)
        {
            var monthlyTokens = await _db.TokenUsages
                .Where(u => u.TenantId == tenantId && u.CreatedAtEpoch >= monthStartEpoch)
                .SumAsync(u => (long)u.TotalTokens, ct);
            monthlyUtil = (float)monthlyTokens / monthlyBudget.Value * 100f;

            if (monthlyTokens >= monthlyBudget.Value)
            {
                _logger.LogWarning(
                    "Monthly token budget exceeded. TenantId={TenantId}, Used={Used}, Budget={Budget}",
                    tenantId, monthlyTokens, monthlyBudget.Value);

                return new BudgetCheckResult
                {
                    Allowed = false,
                    DenialReason = $"Monthly token budget exceeded ({monthlyTokens:N0}/{monthlyBudget.Value:N0} tokens).",
                    DailyUtilizationPercent = dailyUtil,
                    MonthlyUtilizationPercent = monthlyUtil,
                    BudgetWarning = true,
                    WarningMessage = null,
                };
            }
        }

        var warning = dailyUtil >= alertThreshold || monthlyUtil >= alertThreshold;
        string? warningMessage = null;
        if (warning)
        {
            warningMessage = dailyUtil >= alertThreshold
                ? $"Daily token budget at {dailyUtil:F1}% utilization."
                : $"Monthly token budget at {monthlyUtil:F1}% utilization.";
        }

        return new BudgetCheckResult
        {
            Allowed = true,
            DenialReason = null,
            DailyUtilizationPercent = dailyUtil,
            MonthlyUtilizationPercent = monthlyUtil,
            BudgetWarning = warning,
            WarningMessage = warningMessage,
        };
    }
}
