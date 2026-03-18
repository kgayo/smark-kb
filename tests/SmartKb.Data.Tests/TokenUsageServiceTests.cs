using Microsoft.Extensions.Logging.Abstractions;
using SmartKb.Contracts.Configuration;
using SmartKb.Contracts.Models;
using SmartKb.Data.Entities;
using SmartKb.Data.Repositories;

namespace SmartKb.Data.Tests;

public class TokenUsageServiceTests : IDisposable
{
    private readonly SmartKbDbContext _db;
    private readonly CostOptimizationSettings _costSettings;
    private readonly TokenUsageService _service;

    public TokenUsageServiceTests()
    {
        _db = TestDbContextFactory.Create();
        _costSettings = new CostOptimizationSettings();

        _db.Tenants.Add(new TenantEntity
        {
            TenantId = "tenant-1",
            DisplayName = "Test Tenant",
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow,
        });
        _db.SaveChanges();

        _service = new TokenUsageService(
            _db, _costSettings, NullLogger<TokenUsageService>.Instance);
    }

    public void Dispose() => _db.Dispose();

    private static TokenUsageRecord MakeUsage(
        int prompt = 500,
        int completion = 200,
        int embedding = 100,
        bool cacheHit = false,
        int chunks = 5,
        decimal cost = 0.01m) => new()
    {
        PromptTokens = prompt,
        CompletionTokens = completion,
        TotalTokens = prompt + completion,
        EmbeddingTokens = embedding,
        EmbeddingCacheHit = cacheHit,
        EvidenceChunksUsed = chunks,
        EstimatedCostUsd = cost,
    };

    [Fact]
    public async Task RecordUsage_PersistsRow()
    {
        var usage = MakeUsage();

        await _service.RecordUsageAsync("tenant-1", "user-1", "corr-1", usage);

        var entity = Assert.Single(_db.TokenUsages);
        Assert.Equal("tenant-1", entity.TenantId);
        Assert.Equal("user-1", entity.UserId);
        Assert.Equal("corr-1", entity.CorrelationId);
        Assert.Equal(500, entity.PromptTokens);
        Assert.Equal(200, entity.CompletionTokens);
        Assert.Equal(700, entity.TotalTokens);
        Assert.Equal(100, entity.EmbeddingTokens);
        Assert.False(entity.EmbeddingCacheHit);
        Assert.Equal(5, entity.EvidenceChunksUsed);
        Assert.Equal(0.01m, entity.EstimatedCostUsd);
    }

    [Fact]
    public async Task GetSummary_AggregatesCorrectly()
    {
        var now = DateTimeOffset.UtcNow;

        await _service.RecordUsageAsync("tenant-1", "user-1", "c1",
            MakeUsage(prompt: 1000, completion: 500, embedding: 200, cacheHit: false, cost: 0.05m));
        await _service.RecordUsageAsync("tenant-1", "user-2", "c2",
            MakeUsage(prompt: 800, completion: 300, embedding: 150, cacheHit: true, cost: 0.03m));

        var summary = await _service.GetSummaryAsync(
            "tenant-1",
            now.AddHours(-1),
            now.AddHours(1));

        Assert.Equal("tenant-1", summary.TenantId);
        Assert.Equal(1800, summary.TotalPromptTokens);
        Assert.Equal(800, summary.TotalCompletionTokens);
        Assert.Equal(2600, summary.TotalTokens);
        Assert.Equal(350, summary.TotalEmbeddingTokens);
        Assert.Equal(2, summary.TotalRequests);
        Assert.Equal(1, summary.EmbeddingCacheHits);
        Assert.Equal(1, summary.EmbeddingCacheMisses);
        Assert.Equal(0.08m, summary.TotalEstimatedCostUsd);
    }

    [Fact]
    public async Task GetSummary_EmptyPeriod_ReturnsZeroes()
    {
        var far = DateTimeOffset.UtcNow.AddYears(1);

        var summary = await _service.GetSummaryAsync(
            "tenant-1", far, far.AddDays(1));

        Assert.Equal(0, summary.TotalPromptTokens);
        Assert.Equal(0, summary.TotalCompletionTokens);
        Assert.Equal(0, summary.TotalTokens);
        Assert.Equal(0, summary.TotalEmbeddingTokens);
        Assert.Equal(0, summary.TotalRequests);
        Assert.Equal(0, summary.EmbeddingCacheHits);
        Assert.Equal(0, summary.EmbeddingCacheMisses);
        Assert.Equal(0m, summary.TotalEstimatedCostUsd);
    }

    [Fact]
    public async Task GetDailyBreakdown_GroupsByDate()
    {
        // Seed usage entities with specific dates directly for reliable grouping.
        var today = DateTimeOffset.UtcNow.Date;
        var day1 = new DateTimeOffset(today, TimeSpan.Zero);
        var day2 = day1.AddDays(-1);

        _db.TokenUsages.Add(new TokenUsageEntity
        {
            Id = Guid.NewGuid(),
            TenantId = "tenant-1",
            UserId = "u1",
            CorrelationId = "c1",
            PromptTokens = 100,
            CompletionTokens = 50,
            TotalTokens = 150,
            EmbeddingTokens = 30,
            EmbeddingCacheHit = false,
            EvidenceChunksUsed = 3,
            EstimatedCostUsd = 0.01m,
            CreatedAt = day1.AddHours(2),
        });
        _db.TokenUsages.Add(new TokenUsageEntity
        {
            Id = Guid.NewGuid(),
            TenantId = "tenant-1",
            UserId = "u1",
            CorrelationId = "c2",
            PromptTokens = 200,
            CompletionTokens = 100,
            TotalTokens = 300,
            EmbeddingTokens = 60,
            EmbeddingCacheHit = true,
            EvidenceChunksUsed = 5,
            EstimatedCostUsd = 0.02m,
            CreatedAt = day1.AddHours(5),
        });
        _db.TokenUsages.Add(new TokenUsageEntity
        {
            Id = Guid.NewGuid(),
            TenantId = "tenant-1",
            UserId = "u2",
            CorrelationId = "c3",
            PromptTokens = 400,
            CompletionTokens = 200,
            TotalTokens = 600,
            EmbeddingTokens = 80,
            EmbeddingCacheHit = false,
            EvidenceChunksUsed = 7,
            EstimatedCostUsd = 0.04m,
            CreatedAt = day2.AddHours(10),
        });
        await _db.SaveChangesAsync();

        var breakdown = await _service.GetDailyBreakdownAsync(
            "tenant-1", day2, day1.AddDays(1));

        Assert.Equal(2, breakdown.Count);

        var earlier = breakdown[0]; // day2
        Assert.Equal(DateOnly.FromDateTime(day2.DateTime), earlier.Date);
        Assert.Equal(400, earlier.PromptTokens);
        Assert.Equal(600, earlier.TotalTokens);
        Assert.Equal(1, earlier.RequestCount);

        var later = breakdown[1]; // day1
        Assert.Equal(DateOnly.FromDateTime(day1.DateTime), later.Date);
        Assert.Equal(300, later.PromptTokens);
        Assert.Equal(450, later.TotalTokens);
        Assert.Equal(2, later.RequestCount);
        Assert.Equal(1, later.CacheHits);
    }

    [Fact]
    public async Task CheckBudget_NoBudgetsConfigured_Allows()
    {
        // No TenantCostSettings row → no budgets.
        var result = await _service.CheckBudgetAsync("tenant-1");

        Assert.True(result.Allowed);
        Assert.Null(result.DenialReason);
        Assert.False(result.BudgetWarning);
        Assert.Null(result.WarningMessage);
    }

    [Fact]
    public async Task CheckBudget_DailyBudgetExceeded_Denies()
    {
        // Set a daily budget of 1000.
        _db.TenantCostSettings.Add(new TenantCostSettingsEntity
        {
            Id = Guid.NewGuid(),
            TenantId = "tenant-1",
            DailyTokenBudget = 1000,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        });
        await _db.SaveChangesAsync();

        // Record usage exceeding the budget (today).
        _db.TokenUsages.Add(new TokenUsageEntity
        {
            Id = Guid.NewGuid(),
            TenantId = "tenant-1",
            UserId = "u1",
            CorrelationId = "c1",
            PromptTokens = 800,
            CompletionTokens = 400,
            TotalTokens = 1200,
            EmbeddingTokens = 50,
            EmbeddingCacheHit = false,
            EvidenceChunksUsed = 5,
            EstimatedCostUsd = 0.05m,
            CreatedAt = DateTimeOffset.UtcNow,
        });
        await _db.SaveChangesAsync();

        var result = await _service.CheckBudgetAsync("tenant-1");

        Assert.False(result.Allowed);
        Assert.NotNull(result.DenialReason);
        Assert.Contains("Daily", result.DenialReason);
    }

    [Fact]
    public async Task CheckBudget_MonthlyBudgetExceeded_Denies()
    {
        _db.TenantCostSettings.Add(new TenantCostSettingsEntity
        {
            Id = Guid.NewGuid(),
            TenantId = "tenant-1",
            MonthlyTokenBudget = 500,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        });
        await _db.SaveChangesAsync();

        // Record usage exceeding the monthly budget.
        _db.TokenUsages.Add(new TokenUsageEntity
        {
            Id = Guid.NewGuid(),
            TenantId = "tenant-1",
            UserId = "u1",
            CorrelationId = "c1",
            PromptTokens = 400,
            CompletionTokens = 200,
            TotalTokens = 600,
            EmbeddingTokens = 50,
            EmbeddingCacheHit = false,
            EvidenceChunksUsed = 5,
            EstimatedCostUsd = 0.03m,
            CreatedAt = DateTimeOffset.UtcNow,
        });
        await _db.SaveChangesAsync();

        var result = await _service.CheckBudgetAsync("tenant-1");

        Assert.False(result.Allowed);
        Assert.NotNull(result.DenialReason);
        Assert.Contains("Monthly", result.DenialReason);
    }

    [Fact]
    public async Task CheckBudget_ApproachingThreshold_WarnsButAllows()
    {
        _db.TenantCostSettings.Add(new TenantCostSettingsEntity
        {
            Id = Guid.NewGuid(),
            TenantId = "tenant-1",
            DailyTokenBudget = 1000,
            BudgetAlertThresholdPercent = 80,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        });
        await _db.SaveChangesAsync();

        // Record usage at 85% of daily budget (850/1000).
        _db.TokenUsages.Add(new TokenUsageEntity
        {
            Id = Guid.NewGuid(),
            TenantId = "tenant-1",
            UserId = "u1",
            CorrelationId = "c1",
            PromptTokens = 500,
            CompletionTokens = 350,
            TotalTokens = 850,
            EmbeddingTokens = 30,
            EmbeddingCacheHit = false,
            EvidenceChunksUsed = 3,
            EstimatedCostUsd = 0.02m,
            CreatedAt = DateTimeOffset.UtcNow,
        });
        await _db.SaveChangesAsync();

        var result = await _service.CheckBudgetAsync("tenant-1");

        Assert.True(result.Allowed);
        Assert.True(result.BudgetWarning);
        Assert.NotNull(result.WarningMessage);
        Assert.Contains("Daily", result.WarningMessage);
    }

    [Fact]
    public async Task CheckBudget_WellUnderBudget_NoWarning()
    {
        _db.TenantCostSettings.Add(new TenantCostSettingsEntity
        {
            Id = Guid.NewGuid(),
            TenantId = "tenant-1",
            DailyTokenBudget = 100_000,
            BudgetAlertThresholdPercent = 80,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        });
        await _db.SaveChangesAsync();

        // Record small usage (100/100_000 = 0.1%).
        _db.TokenUsages.Add(new TokenUsageEntity
        {
            Id = Guid.NewGuid(),
            TenantId = "tenant-1",
            UserId = "u1",
            CorrelationId = "c1",
            PromptTokens = 70,
            CompletionTokens = 30,
            TotalTokens = 100,
            EmbeddingTokens = 10,
            EmbeddingCacheHit = false,
            EvidenceChunksUsed = 2,
            EstimatedCostUsd = 0.001m,
            CreatedAt = DateTimeOffset.UtcNow,
        });
        await _db.SaveChangesAsync();

        var result = await _service.CheckBudgetAsync("tenant-1");

        Assert.True(result.Allowed);
        Assert.False(result.BudgetWarning);
        Assert.Null(result.WarningMessage);
    }
}
