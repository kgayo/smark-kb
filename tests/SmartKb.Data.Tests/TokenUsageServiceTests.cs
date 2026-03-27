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
            CreatedAtEpoch = day1.AddHours(2).ToUnixTimeSeconds(),
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
            CreatedAtEpoch = day1.AddHours(5).ToUnixTimeSeconds(),
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
            CreatedAtEpoch = day2.AddHours(10).ToUnixTimeSeconds(),
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
        var now = DateTimeOffset.UtcNow;
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
            CreatedAt = now,
            CreatedAtEpoch = now.ToUnixTimeSeconds(),
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
            CreatedAtEpoch = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
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
            CreatedAtEpoch = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
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
            CreatedAtEpoch = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
        });
        await _db.SaveChangesAsync();

        var result = await _service.CheckBudgetAsync("tenant-1");

        Assert.True(result.Allowed);
        Assert.False(result.BudgetWarning);
        Assert.Null(result.WarningMessage);
    }

    // --- Tenant isolation tests ---

    [Fact]
    public async Task GetSummary_ExcludesOtherTenantData()
    {
        _db.Tenants.Add(new TenantEntity
        {
            TenantId = "tenant-2",
            DisplayName = "Other Tenant",
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow,
        });
        await _db.SaveChangesAsync();

        var now = DateTimeOffset.UtcNow;
        await _service.RecordUsageAsync("tenant-1", "u1", "c1",
            MakeUsage(prompt: 100, completion: 50, cost: 0.01m));
        await _service.RecordUsageAsync("tenant-2", "u2", "c2",
            MakeUsage(prompt: 9000, completion: 9000, cost: 9.00m));

        var summary = await _service.GetSummaryAsync(
            "tenant-1", now.AddHours(-1), now.AddHours(1));

        Assert.Equal(150, summary.TotalTokens);
        Assert.Equal(1, summary.TotalRequests);
        Assert.Equal(0.01m, summary.TotalEstimatedCostUsd);
    }

    [Fact]
    public async Task GetDailyBreakdown_ExcludesOtherTenantData()
    {
        _db.Tenants.Add(new TenantEntity
        {
            TenantId = "tenant-2",
            DisplayName = "Other Tenant",
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow,
        });
        await _db.SaveChangesAsync();

        var now = DateTimeOffset.UtcNow;
        await _service.RecordUsageAsync("tenant-1", "u1", "c1",
            MakeUsage(prompt: 100, completion: 50));
        await _service.RecordUsageAsync("tenant-2", "u2", "c2",
            MakeUsage(prompt: 5000, completion: 5000));

        var breakdown = await _service.GetDailyBreakdownAsync(
            "tenant-1", now.AddHours(-1), now.AddHours(1));

        Assert.Single(breakdown);
        Assert.Equal(150, breakdown[0].TotalTokens);
        Assert.Equal(1, breakdown[0].RequestCount);
    }

    [Fact]
    public async Task CheckBudget_ExcludesOtherTenantUsage()
    {
        _db.Tenants.Add(new TenantEntity
        {
            TenantId = "tenant-2",
            DisplayName = "Other Tenant",
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow,
        });
        await _db.SaveChangesAsync();

        _db.TenantCostSettings.Add(new TenantCostSettingsEntity
        {
            Id = Guid.NewGuid(),
            TenantId = "tenant-1",
            DailyTokenBudget = 1000,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        });
        await _db.SaveChangesAsync();

        // tenant-2 has huge usage but it shouldn't affect tenant-1
        _db.TokenUsages.Add(new TokenUsageEntity
        {
            Id = Guid.NewGuid(),
            TenantId = "tenant-2",
            UserId = "u2",
            CorrelationId = "c2",
            PromptTokens = 50000,
            CompletionTokens = 50000,
            TotalTokens = 100000,
            EmbeddingTokens = 0,
            EmbeddingCacheHit = false,
            EvidenceChunksUsed = 0,
            EstimatedCostUsd = 5.00m,
            CreatedAt = DateTimeOffset.UtcNow,
            CreatedAtEpoch = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
        });
        await _db.SaveChangesAsync();

        var result = await _service.CheckBudgetAsync("tenant-1");

        Assert.True(result.Allowed);
        Assert.Equal(0f, result.DailyUtilizationPercent);
    }

    // --- Budget utilization percentage tests ---

    [Fact]
    public async Task GetSummary_IncludesBudgetUtilizationPercents()
    {
        _db.TenantCostSettings.Add(new TenantCostSettingsEntity
        {
            Id = Guid.NewGuid(),
            TenantId = "tenant-1",
            DailyTokenBudget = 10000,
            MonthlyTokenBudget = 100000,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        });
        await _db.SaveChangesAsync();

        // Record 5000 tokens today.
        _db.TokenUsages.Add(new TokenUsageEntity
        {
            Id = Guid.NewGuid(),
            TenantId = "tenant-1",
            UserId = "u1",
            CorrelationId = "c1",
            PromptTokens = 3000,
            CompletionTokens = 2000,
            TotalTokens = 5000,
            EmbeddingTokens = 100,
            EmbeddingCacheHit = false,
            EvidenceChunksUsed = 5,
            EstimatedCostUsd = 0.05m,
            CreatedAt = DateTimeOffset.UtcNow,
            CreatedAtEpoch = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
        });
        await _db.SaveChangesAsync();

        var now = DateTimeOffset.UtcNow;
        var summary = await _service.GetSummaryAsync(
            "tenant-1", now.AddHours(-1), now.AddHours(1));

        Assert.Equal(10000, summary.DailyTokenBudget);
        Assert.Equal(100000, summary.MonthlyTokenBudget);
        Assert.True(summary.DailyBudgetUtilizationPercent > 0f);
        Assert.True(summary.MonthlyBudgetUtilizationPercent > 0f);
        // 5000/10000 = 50%
        Assert.InRange(summary.DailyBudgetUtilizationPercent, 49f, 51f);
        // 5000/100000 = 5%
        Assert.InRange(summary.MonthlyBudgetUtilizationPercent, 4f, 6f);
    }

    [Fact]
    public async Task GetSummary_NoBudgetConfigured_UtilizationZero()
    {
        await _service.RecordUsageAsync("tenant-1", "u1", "c1",
            MakeUsage(prompt: 1000, completion: 500));

        var now = DateTimeOffset.UtcNow;
        var summary = await _service.GetSummaryAsync(
            "tenant-1", now.AddHours(-1), now.AddHours(1));

        Assert.Null(summary.DailyTokenBudget);
        Assert.Null(summary.MonthlyTokenBudget);
        Assert.Equal(0f, summary.DailyBudgetUtilizationPercent);
        Assert.Equal(0f, summary.MonthlyBudgetUtilizationPercent);
    }

    // --- Monthly warning and both-budgets tests ---

    [Fact]
    public async Task CheckBudget_MonthlyApproachingThreshold_WarnsWithMonthlyMessage()
    {
        _db.TenantCostSettings.Add(new TenantCostSettingsEntity
        {
            Id = Guid.NewGuid(),
            TenantId = "tenant-1",
            MonthlyTokenBudget = 1000,
            BudgetAlertThresholdPercent = 80,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        });
        await _db.SaveChangesAsync();

        // Record 850 tokens this month (85% of 1000).
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
            CreatedAtEpoch = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
        });
        await _db.SaveChangesAsync();

        var result = await _service.CheckBudgetAsync("tenant-1");

        Assert.True(result.Allowed);
        Assert.True(result.BudgetWarning);
        Assert.NotNull(result.WarningMessage);
        Assert.Contains("Monthly", result.WarningMessage);
    }

    [Fact]
    public async Task CheckBudget_DailyOkButMonthlyExceeded_DeniesWithMonthlyReason()
    {
        _db.TenantCostSettings.Add(new TenantCostSettingsEntity
        {
            Id = Guid.NewGuid(),
            TenantId = "tenant-1",
            DailyTokenBudget = 100000,  // generous daily
            MonthlyTokenBudget = 500,   // tight monthly
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        });
        await _db.SaveChangesAsync();

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
            CreatedAtEpoch = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
        });
        await _db.SaveChangesAsync();

        var result = await _service.CheckBudgetAsync("tenant-1");

        Assert.False(result.Allowed);
        Assert.NotNull(result.DenialReason);
        Assert.Contains("Monthly", result.DenialReason);
    }

    [Fact]
    public async Task CheckBudget_UsesGlobalDefaultAlertThreshold_WhenTenantSettingNull()
    {
        var customSettings = new CostOptimizationSettings { BudgetAlertThresholdPercent = 50 };
        var customService = new TokenUsageService(
            _db, customSettings, NullLogger<TokenUsageService>.Instance);

        _db.TenantCostSettings.Add(new TenantCostSettingsEntity
        {
            Id = Guid.NewGuid(),
            TenantId = "tenant-1",
            DailyTokenBudget = 1000,
            BudgetAlertThresholdPercent = null, // falls back to global
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        });
        await _db.SaveChangesAsync();

        // 550/1000 = 55% — above global threshold of 50%, below default 80%.
        _db.TokenUsages.Add(new TokenUsageEntity
        {
            Id = Guid.NewGuid(),
            TenantId = "tenant-1",
            UserId = "u1",
            CorrelationId = "c1",
            PromptTokens = 350,
            CompletionTokens = 200,
            TotalTokens = 550,
            EmbeddingTokens = 30,
            EmbeddingCacheHit = false,
            EvidenceChunksUsed = 3,
            EstimatedCostUsd = 0.02m,
            CreatedAt = DateTimeOffset.UtcNow,
            CreatedAtEpoch = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
        });
        await _db.SaveChangesAsync();

        var result = await customService.CheckBudgetAsync("tenant-1");

        Assert.True(result.Allowed);
        Assert.True(result.BudgetWarning);
        Assert.NotNull(result.WarningMessage);
    }

    // --- GetDailyBreakdown edge cases ---

    [Fact]
    public async Task GetDailyBreakdown_EmptyPeriod_ReturnsEmptyList()
    {
        var far = DateTimeOffset.UtcNow.AddYears(1);

        var breakdown = await _service.GetDailyBreakdownAsync(
            "tenant-1", far, far.AddDays(1));

        Assert.Empty(breakdown);
    }

    [Fact]
    public async Task RecordUsage_MultipleRecords_AllPersisted()
    {
        await _service.RecordUsageAsync("tenant-1", "u1", "c1",
            MakeUsage(prompt: 100, completion: 50, cacheHit: true));
        await _service.RecordUsageAsync("tenant-1", "u1", "c2",
            MakeUsage(prompt: 200, completion: 100, cacheHit: false));
        await _service.RecordUsageAsync("tenant-1", "u2", "c3",
            MakeUsage(prompt: 300, completion: 150, cacheHit: true));

        Assert.Equal(3, _db.TokenUsages.Count());

        var now = DateTimeOffset.UtcNow;
        var summary = await _service.GetSummaryAsync(
            "tenant-1", now.AddHours(-1), now.AddHours(1));

        Assert.Equal(3, summary.TotalRequests);
        Assert.Equal(2, summary.EmbeddingCacheHits);
        Assert.Equal(1, summary.EmbeddingCacheMisses);
    }

    // --- Epoch column tests ---

    [Fact]
    public async Task RecordUsage_SetsCreatedAtEpochConsistentWithCreatedAt()
    {
        await _service.RecordUsageAsync("tenant-1", "u1", "c1", MakeUsage());

        var entity = Assert.Single(_db.TokenUsages);
        var expectedEpoch = entity.CreatedAt.ToUnixTimeSeconds();
        Assert.Equal(expectedEpoch, entity.CreatedAtEpoch);
    }

    [Fact]
    public async Task GetSummary_FiltersServerSideViaEpochColumn()
    {
        var now = DateTimeOffset.UtcNow;
        var inRange = now.AddMinutes(-10);
        var outOfRange = now.AddDays(-30);

        _db.TokenUsages.Add(new TokenUsageEntity
        {
            Id = Guid.NewGuid(),
            TenantId = "tenant-1",
            UserId = "u1",
            CorrelationId = "c1",
            PromptTokens = 100,
            CompletionTokens = 50,
            TotalTokens = 150,
            EmbeddingTokens = 20,
            EmbeddingCacheHit = false,
            EvidenceChunksUsed = 2,
            EstimatedCostUsd = 0.01m,
            CreatedAt = inRange,
            CreatedAtEpoch = inRange.ToUnixTimeSeconds(),
        });
        _db.TokenUsages.Add(new TokenUsageEntity
        {
            Id = Guid.NewGuid(),
            TenantId = "tenant-1",
            UserId = "u1",
            CorrelationId = "c2",
            PromptTokens = 9000,
            CompletionTokens = 9000,
            TotalTokens = 18000,
            EmbeddingTokens = 500,
            EmbeddingCacheHit = false,
            EvidenceChunksUsed = 10,
            EstimatedCostUsd = 1.00m,
            CreatedAt = outOfRange,
            CreatedAtEpoch = outOfRange.ToUnixTimeSeconds(),
        });
        await _db.SaveChangesAsync();

        var summary = await _service.GetSummaryAsync(
            "tenant-1", now.AddHours(-1), now.AddHours(1));

        Assert.Equal(1, summary.TotalRequests);
        Assert.Equal(150, summary.TotalTokens);
    }

    [Fact]
    public async Task GetDailyBreakdown_FiltersServerSideViaEpochColumn()
    {
        var now = DateTimeOffset.UtcNow;
        var inRange = now.AddMinutes(-10);
        var outOfRange = now.AddDays(-30);

        _db.TokenUsages.Add(new TokenUsageEntity
        {
            Id = Guid.NewGuid(),
            TenantId = "tenant-1",
            UserId = "u1",
            CorrelationId = "c1",
            PromptTokens = 200,
            CompletionTokens = 100,
            TotalTokens = 300,
            EmbeddingTokens = 40,
            EmbeddingCacheHit = false,
            EvidenceChunksUsed = 3,
            EstimatedCostUsd = 0.02m,
            CreatedAt = inRange,
            CreatedAtEpoch = inRange.ToUnixTimeSeconds(),
        });
        _db.TokenUsages.Add(new TokenUsageEntity
        {
            Id = Guid.NewGuid(),
            TenantId = "tenant-1",
            UserId = "u1",
            CorrelationId = "c2",
            PromptTokens = 5000,
            CompletionTokens = 5000,
            TotalTokens = 10000,
            EmbeddingTokens = 200,
            EmbeddingCacheHit = false,
            EvidenceChunksUsed = 8,
            EstimatedCostUsd = 0.50m,
            CreatedAt = outOfRange,
            CreatedAtEpoch = outOfRange.ToUnixTimeSeconds(),
        });
        await _db.SaveChangesAsync();

        var breakdown = await _service.GetDailyBreakdownAsync(
            "tenant-1", now.AddHours(-1), now.AddHours(1));

        Assert.Single(breakdown);
        Assert.Equal(300, breakdown[0].TotalTokens);
    }

    [Fact]
    public async Task CheckBudget_FiltersServerSideViaEpochColumn()
    {
        _db.TenantCostSettings.Add(new TenantCostSettingsEntity
        {
            Id = Guid.NewGuid(),
            TenantId = "tenant-1",
            DailyTokenBudget = 1000,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        });
        await _db.SaveChangesAsync();

        // Old usage (30 days ago) — should NOT count toward daily budget.
        var old = DateTimeOffset.UtcNow.AddDays(-30);
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
            CreatedAt = old,
            CreatedAtEpoch = old.ToUnixTimeSeconds(),
        });
        await _db.SaveChangesAsync();

        var result = await _service.CheckBudgetAsync("tenant-1");

        // Old usage should not trigger daily budget denial.
        Assert.True(result.Allowed);
        Assert.Equal(0f, result.DailyUtilizationPercent);
    }
}
