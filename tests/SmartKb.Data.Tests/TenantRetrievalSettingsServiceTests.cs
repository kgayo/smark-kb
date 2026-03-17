using Microsoft.Extensions.Logging.Abstractions;
using SmartKb.Contracts.Configuration;
using SmartKb.Contracts.Models;
using SmartKb.Data.Entities;
using SmartKb.Data.Repositories;

namespace SmartKb.Data.Tests;

public class TenantRetrievalSettingsServiceTests : IDisposable
{
    private readonly SmartKbDbContext _db;
    private readonly RetrievalSettings _defaults;
    private readonly TenantRetrievalSettingsService _service;

    public TenantRetrievalSettingsServiceTests()
    {
        _db = TestDbContextFactory.Create();
        _defaults = new RetrievalSettings();

        // Seed a tenant.
        _db.Tenants.Add(new TenantEntity
        {
            TenantId = "tenant-1",
            DisplayName = "Test Tenant",
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow,
        });
        _db.SaveChanges();

        _service = new TenantRetrievalSettingsService(
            _db, _defaults, NullLogger<TenantRetrievalSettingsService>.Instance);
    }

    public void Dispose() => _db.Dispose();

    [Fact]
    public async Task GetSettings_NoOverrides_ReturnsDefaults()
    {
        var result = await _service.GetSettingsAsync("tenant-1");

        Assert.Equal("tenant-1", result.TenantId);
        Assert.Equal(_defaults.TopK, result.TopK);
        Assert.Equal(_defaults.EnableSemanticReranking, result.EnableSemanticReranking);
        Assert.Equal(_defaults.EnablePatternFusion, result.EnablePatternFusion);
        Assert.Equal(_defaults.PatternTopK, result.PatternTopK);
        Assert.Equal(_defaults.TrustBoostApproved, result.TrustBoostApproved);
        Assert.Equal(_defaults.DiversityMaxPerSource, result.DiversityMaxPerSource);
        Assert.False(result.HasOverrides);
    }

    [Fact]
    public async Task UpdateSettings_CreatesOverride()
    {
        var request = new UpdateRetrievalSettingsRequest
        {
            TopK = 30,
            EnablePatternFusion = false,
        };

        var result = await _service.UpdateSettingsAsync("tenant-1", request);

        Assert.Equal("tenant-1", result.TenantId);
        Assert.Equal(30, result.TopK);
        Assert.False(result.EnablePatternFusion);
        Assert.True(result.HasOverrides);
        // Non-overridden fields should be defaults.
        Assert.Equal(_defaults.PatternTopK, result.PatternTopK);
        Assert.Equal(_defaults.EnableSemanticReranking, result.EnableSemanticReranking);
    }

    [Fact]
    public async Task UpdateSettings_UpdatesExisting()
    {
        await _service.UpdateSettingsAsync("tenant-1", new UpdateRetrievalSettingsRequest { TopK = 30 });
        var result = await _service.UpdateSettingsAsync("tenant-1", new UpdateRetrievalSettingsRequest { PatternTopK = 10 });

        Assert.Equal(30, result.TopK); // Preserved from first update.
        Assert.Equal(10, result.PatternTopK);
        Assert.True(result.HasOverrides);
    }

    [Fact]
    public async Task ResetSettings_DeletesOverride()
    {
        await _service.UpdateSettingsAsync("tenant-1", new UpdateRetrievalSettingsRequest { TopK = 30 });

        var deleted = await _service.ResetSettingsAsync("tenant-1");
        Assert.True(deleted);

        var result = await _service.GetSettingsAsync("tenant-1");
        Assert.False(result.HasOverrides);
        Assert.Equal(_defaults.TopK, result.TopK);
    }

    [Fact]
    public async Task ResetSettings_NoOverride_ReturnsFalse()
    {
        var deleted = await _service.ResetSettingsAsync("tenant-1");
        Assert.False(deleted);
    }

    [Fact]
    public async Task UpdateSettings_AllBoostFields()
    {
        var request = new UpdateRetrievalSettingsRequest
        {
            TrustBoostApproved = 2.0f,
            TrustBoostReviewed = 1.5f,
            TrustBoostDraft = 0.5f,
            RecencyBoostRecent = 1.5f,
            RecencyBoostOld = 0.6f,
            PatternAuthorityBoost = 1.8f,
            NoEvidenceScoreThreshold = 0.5f,
            NoEvidenceMinResults = 5,
        };

        var result = await _service.UpdateSettingsAsync("tenant-1", request);

        Assert.Equal(2.0f, result.TrustBoostApproved);
        Assert.Equal(1.5f, result.TrustBoostReviewed);
        Assert.Equal(0.5f, result.TrustBoostDraft);
        Assert.Equal(1.5f, result.RecencyBoostRecent);
        Assert.Equal(0.6f, result.RecencyBoostOld);
        Assert.Equal(1.8f, result.PatternAuthorityBoost);
        Assert.Equal(0.5f, result.NoEvidenceScoreThreshold);
        Assert.Equal(5, result.NoEvidenceMinResults);
    }

    [Fact]
    public async Task GetSettings_DifferentTenants_Isolated()
    {
        _db.Tenants.Add(new TenantEntity
        {
            TenantId = "tenant-2",
            DisplayName = "Tenant 2",
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow,
        });
        await _db.SaveChangesAsync();

        await _service.UpdateSettingsAsync("tenant-1", new UpdateRetrievalSettingsRequest { TopK = 50 });

        var t1 = await _service.GetSettingsAsync("tenant-1");
        var t2 = await _service.GetSettingsAsync("tenant-2");

        Assert.Equal(50, t1.TopK);
        Assert.Equal(_defaults.TopK, t2.TopK);
        Assert.True(t1.HasOverrides);
        Assert.False(t2.HasOverrides);
    }

    [Fact]
    public async Task UpdateSettings_DiversityMaxPerSource()
    {
        var result = await _service.UpdateSettingsAsync("tenant-1",
            new UpdateRetrievalSettingsRequest { DiversityMaxPerSource = 5 });

        Assert.Equal(5, result.DiversityMaxPerSource);
    }

    [Fact]
    public async Task UpdateSettings_EnableSemanticReranking()
    {
        var result = await _service.UpdateSettingsAsync("tenant-1",
            new UpdateRetrievalSettingsRequest { EnableSemanticReranking = false });

        Assert.False(result.EnableSemanticReranking);
    }
}
