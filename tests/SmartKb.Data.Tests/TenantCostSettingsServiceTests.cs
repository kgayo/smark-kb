using Microsoft.Extensions.Logging.Abstractions;
using SmartKb.Contracts.Configuration;
using SmartKb.Contracts.Models;
using SmartKb.Data.Entities;
using SmartKb.Data.Repositories;

namespace SmartKb.Data.Tests;

public class TenantCostSettingsServiceTests : IDisposable
{
    private readonly SmartKbDbContext _db;
    private readonly CostOptimizationSettings _defaults;
    private readonly ChatOrchestrationSettings _chatSettings;
    private readonly TenantCostSettingsService _service;

    public TenantCostSettingsServiceTests()
    {
        _db = TestDbContextFactory.Create();
        _defaults = new CostOptimizationSettings();
        _chatSettings = new ChatOrchestrationSettings();

        _db.Tenants.Add(new TenantEntity
        {
            TenantId = "tenant-1",
            DisplayName = "Test Tenant",
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow,
        });
        _db.SaveChanges();

        _service = new TenantCostSettingsService(
            _db, _defaults, _chatSettings, NullLogger<TenantCostSettingsService>.Instance);
    }

    public void Dispose() => _db.Dispose();

    [Fact]
    public async Task GetSettings_NoOverrides_ReturnsDefaults()
    {
        var result = await _service.GetSettingsAsync("tenant-1");

        Assert.Equal("tenant-1", result.TenantId);
        Assert.Null(result.DailyTokenBudget);
        Assert.Null(result.MonthlyTokenBudget);
        Assert.Null(result.MaxPromptTokensPerQuery);
        Assert.Equal(_chatSettings.MaxEvidenceChunksInPrompt, result.MaxEvidenceChunksInPrompt);
        Assert.Equal(_defaults.EnableEmbeddingCache, result.EnableEmbeddingCache);
        Assert.Equal(_defaults.EmbeddingCacheTtlHours, result.EmbeddingCacheTtlHours);
        Assert.Equal(_defaults.EnableRetrievalCompression, result.EnableRetrievalCompression);
        Assert.Equal(_defaults.MaxChunkCharsCompressed, result.MaxChunkCharsCompressed);
        Assert.Equal(_defaults.BudgetAlertThresholdPercent, result.BudgetAlertThresholdPercent);
        Assert.False(result.HasOverrides);
    }

    [Fact]
    public async Task UpdateSettings_CreatesNewRow_OnFirstUpdate()
    {
        var request = new UpdateCostSettingsRequest
        {
            DailyTokenBudget = 100_000,
            EnableEmbeddingCache = false,
        };

        var result = await _service.UpdateSettingsAsync("tenant-1", request);

        Assert.Equal("tenant-1", result.TenantId);
        Assert.Equal(100_000, result.DailyTokenBudget);
        Assert.False(result.EnableEmbeddingCache);
        Assert.True(result.HasOverrides);
    }

    [Fact]
    public async Task UpdateSettings_UpdatesExistingRow()
    {
        await _service.UpdateSettingsAsync("tenant-1",
            new UpdateCostSettingsRequest { DailyTokenBudget = 100_000 });

        var result = await _service.UpdateSettingsAsync("tenant-1",
            new UpdateCostSettingsRequest { MonthlyTokenBudget = 3_000_000 });

        Assert.Equal(100_000, result.DailyTokenBudget); // Preserved from first update.
        Assert.Equal(3_000_000, result.MonthlyTokenBudget);
        Assert.True(result.HasOverrides);
    }

    [Fact]
    public async Task UpdateSettings_OnlyNonNullFieldsOverride()
    {
        // First update sets DailyTokenBudget and EmbeddingCacheTtlHours.
        await _service.UpdateSettingsAsync("tenant-1", new UpdateCostSettingsRequest
        {
            DailyTokenBudget = 50_000,
            EmbeddingCacheTtlHours = 48,
        });

        // Second update only sets MaxChunkCharsCompressed; other fields remain.
        var result = await _service.UpdateSettingsAsync("tenant-1", new UpdateCostSettingsRequest
        {
            MaxChunkCharsCompressed = 500,
        });

        Assert.Equal(50_000, result.DailyTokenBudget);
        Assert.Equal(48, result.EmbeddingCacheTtlHours);
        Assert.Equal(500, result.MaxChunkCharsCompressed);
    }

    [Fact]
    public async Task ResetSettings_RemovesOverrides()
    {
        await _service.UpdateSettingsAsync("tenant-1",
            new UpdateCostSettingsRequest { DailyTokenBudget = 100_000 });

        var deleted = await _service.ResetSettingsAsync("tenant-1");

        Assert.True(deleted);

        var result = await _service.GetSettingsAsync("tenant-1");
        Assert.False(result.HasOverrides);
        Assert.Null(result.DailyTokenBudget);
        Assert.Equal(_defaults.EnableEmbeddingCache, result.EnableEmbeddingCache);
    }

    [Fact]
    public async Task ResetSettings_NoOverrides_ReturnsFalse()
    {
        var deleted = await _service.ResetSettingsAsync("tenant-1");
        Assert.False(deleted);
    }

    [Fact]
    public async Task Settings_MergeCorrectlyWithDefaults()
    {
        // Override only a few fields; rest should come from defaults.
        var request = new UpdateCostSettingsRequest
        {
            EnableRetrievalCompression = true,
            MaxChunkCharsCompressed = 800,
            BudgetAlertThresholdPercent = 90,
        };

        var result = await _service.UpdateSettingsAsync("tenant-1", request);

        // Overridden fields.
        Assert.True(result.EnableRetrievalCompression);
        Assert.Equal(800, result.MaxChunkCharsCompressed);
        Assert.Equal(90, result.BudgetAlertThresholdPercent);

        // Default fields.
        Assert.Null(result.DailyTokenBudget);
        Assert.Null(result.MonthlyTokenBudget);
        Assert.Null(result.MaxPromptTokensPerQuery);
        Assert.Equal(_chatSettings.MaxEvidenceChunksInPrompt, result.MaxEvidenceChunksInPrompt);
        Assert.Equal(_defaults.EnableEmbeddingCache, result.EnableEmbeddingCache);
        Assert.Equal(_defaults.EmbeddingCacheTtlHours, result.EmbeddingCacheTtlHours);
        Assert.True(result.HasOverrides);
    }

    [Fact]
    public async Task UpdateSettings_AllFields()
    {
        var request = new UpdateCostSettingsRequest
        {
            DailyTokenBudget = 200_000,
            MonthlyTokenBudget = 5_000_000,
            MaxPromptTokensPerQuery = 4096,
            MaxEvidenceChunksInPrompt = 10,
            EnableEmbeddingCache = false,
            EmbeddingCacheTtlHours = 12,
            EnableRetrievalCompression = true,
            MaxChunkCharsCompressed = 1000,
            BudgetAlertThresholdPercent = 70,
        };

        var result = await _service.UpdateSettingsAsync("tenant-1", request);

        Assert.Equal(200_000, result.DailyTokenBudget);
        Assert.Equal(5_000_000, result.MonthlyTokenBudget);
        Assert.Equal(4096, result.MaxPromptTokensPerQuery);
        Assert.Equal(10, result.MaxEvidenceChunksInPrompt);
        Assert.False(result.EnableEmbeddingCache);
        Assert.Equal(12, result.EmbeddingCacheTtlHours);
        Assert.True(result.EnableRetrievalCompression);
        Assert.Equal(1000, result.MaxChunkCharsCompressed);
        Assert.Equal(70, result.BudgetAlertThresholdPercent);
    }

    [Fact]
    public async Task DifferentTenants_AreIsolated()
    {
        _db.Tenants.Add(new TenantEntity
        {
            TenantId = "tenant-2",
            DisplayName = "Tenant 2",
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow,
        });
        await _db.SaveChangesAsync();

        await _service.UpdateSettingsAsync("tenant-1",
            new UpdateCostSettingsRequest { DailyTokenBudget = 100_000 });

        var t1 = await _service.GetSettingsAsync("tenant-1");
        var t2 = await _service.GetSettingsAsync("tenant-2");

        Assert.Equal(100_000, t1.DailyTokenBudget);
        Assert.True(t1.HasOverrides);
        Assert.Null(t2.DailyTokenBudget);
        Assert.False(t2.HasOverrides);
    }
}
