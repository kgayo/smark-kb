using Microsoft.Extensions.Logging.Abstractions;
using SmartKb.Contracts;
using SmartKb.Contracts.Configuration;
using SmartKb.Contracts.Services;
using SmartKb.Data.Entities;
using SmartKb.Data.Repositories;

namespace SmartKb.Data.Tests;

public class EmbeddingCacheServiceTests : IDisposable
{
    private readonly SmartKbDbContext _db;
    private readonly FakeEmbeddingService _fakeEmbedding;
    private readonly EmbeddingCacheService _service;

    public EmbeddingCacheServiceTests()
    {
        _db = TestDbContextFactory.Create();
        _fakeEmbedding = new FakeEmbeddingService();

        var embeddingSettings = new EmbeddingSettings();
        var costSettings = new CostOptimizationSettings { EmbeddingCacheTtlHours = 24 };

        _service = new EmbeddingCacheService(
            _db,
            _fakeEmbedding,
            embeddingSettings,
            costSettings,
            NullLogger<EmbeddingCacheService>.Instance);
    }

    public void Dispose() => _db.Dispose();

    [Fact]
    public async Task CacheMiss_CallsInnerServiceAndCachesResult()
    {
        var (embedding, cacheHit) = await _service.GetOrGenerateAsync("hello world");

        Assert.False(cacheHit);
        Assert.NotNull(embedding);
        Assert.Equal(new float[] { 0.1f, 0.2f, 0.3f }, embedding);
        Assert.Equal(1, _fakeEmbedding.CallCount);

        // Verify cached in DB.
        var cached = Assert.Single(_db.EmbeddingCache);
        Assert.Equal(ConnectorHttpHelper.ComputeHash("hello world"), cached.ContentHash);
        Assert.Equal("hello world", cached.InputText);
    }

    [Fact]
    public async Task CacheHit_ReturnsCachedResultWithoutCallingInnerService()
    {
        // First call — cache miss.
        await _service.GetOrGenerateAsync("hello world");
        Assert.Equal(1, _fakeEmbedding.CallCount);

        // Second call — cache hit.
        var (embedding, cacheHit) = await _service.GetOrGenerateAsync("hello world");

        Assert.True(cacheHit);
        Assert.NotNull(embedding);
        Assert.Equal(new float[] { 0.1f, 0.2f, 0.3f }, embedding);
        Assert.Equal(1, _fakeEmbedding.CallCount); // Not called again.
    }

    [Fact]
    public async Task ExpiredEntries_AreNotReturned()
    {
        // Manually insert an expired cache entry.
        var hash = ConnectorHttpHelper.ComputeHash("expired query");
        _db.EmbeddingCache.Add(new EmbeddingCacheEntity
        {
            Id = Guid.NewGuid(),
            ContentHash = hash,
            InputText = "expired query",
            EmbeddingJson = "[0.1, 0.2, 0.3]",
            ModelId = "text-embedding-3-large",
            Dimensions = 3,
            CreatedAt = DateTimeOffset.UtcNow.AddHours(-48),
            LastAccessedAt = DateTimeOffset.UtcNow.AddHours(-48),
            ExpiresAt = DateTimeOffset.UtcNow.AddHours(-1), // Expired.
        });
        await _db.SaveChangesAsync();

        var (embedding, cacheHit) = await _service.GetOrGenerateAsync("expired query");

        Assert.False(cacheHit);
        Assert.Equal(1, _fakeEmbedding.CallCount); // Had to call inner service.
    }

    [Fact]
    public async Task EvictExpired_RemovesExpiredEntries()
    {
        var now = DateTimeOffset.UtcNow;

        // Add one expired and one valid entry.
        _db.EmbeddingCache.Add(new EmbeddingCacheEntity
        {
            Id = Guid.NewGuid(),
            ContentHash = "hash-expired",
            InputText = "expired",
            EmbeddingJson = "[0.1]",
            ModelId = "model",
            Dimensions = 1,
            CreatedAt = now.AddHours(-48),
            LastAccessedAt = now.AddHours(-48),
            ExpiresAt = now.AddHours(-1),
        });
        _db.EmbeddingCache.Add(new EmbeddingCacheEntity
        {
            Id = Guid.NewGuid(),
            ContentHash = "hash-valid",
            InputText = "valid",
            EmbeddingJson = "[0.2]",
            ModelId = "model",
            Dimensions = 1,
            CreatedAt = now.AddHours(-1),
            LastAccessedAt = now.AddHours(-1),
            ExpiresAt = now.AddHours(23),
        });
        await _db.SaveChangesAsync();

        var evicted = await _service.EvictExpiredAsync();

        Assert.Equal(1, evicted);
        var remaining = Assert.Single(_db.EmbeddingCache);
        Assert.Equal("hash-valid", remaining.ContentHash);
    }

    [Fact]
    public void ComputeHash_ConsistentForSameInput()
    {
        var hash1 = ConnectorHttpHelper.ComputeHash("test input");
        var hash2 = ConnectorHttpHelper.ComputeHash("test input");

        Assert.Equal(hash1, hash2);
        Assert.NotEmpty(hash1);
    }

    [Fact]
    public void ComputeHash_DifferentForDifferentInput()
    {
        var hash1 = ConnectorHttpHelper.ComputeHash("input A");
        var hash2 = ConnectorHttpHelper.ComputeHash("input B");

        Assert.NotEqual(hash1, hash2);
    }

    [Fact]
    public async Task EvictExpired_NoExpired_ReturnsZero()
    {
        var evicted = await _service.EvictExpiredAsync();
        Assert.Equal(0, evicted);
    }

    [Fact]
    public async Task DifferentTexts_CachedSeparately()
    {
        await _service.GetOrGenerateAsync("text one");
        await _service.GetOrGenerateAsync("text two");

        Assert.Equal(2, _fakeEmbedding.CallCount);
        Assert.Equal(2, _db.EmbeddingCache.Count());
    }

    [Fact]
    public async Task CacheHit_MalformedJson_TreatsAsMissAndRegenerates()
    {
        // Insert a cache entry with corrupted JSON.
        var hash = ConnectorHttpHelper.ComputeHash("corrupt query");
        _db.EmbeddingCache.Add(new EmbeddingCacheEntity
        {
            Id = Guid.NewGuid(),
            ContentHash = hash,
            InputText = "corrupt query",
            EmbeddingJson = "NOT VALID JSON",
            ModelId = "text-embedding-3-large",
            Dimensions = 3,
            CreatedAt = DateTimeOffset.UtcNow.AddHours(-1),
            LastAccessedAt = DateTimeOffset.UtcNow.AddHours(-1),
            ExpiresAt = DateTimeOffset.UtcNow.AddHours(23),
        });
        await _db.SaveChangesAsync();

        var (embedding, cacheHit) = await _service.GetOrGenerateAsync("corrupt query");

        // Should treat corrupted cache as miss and regenerate.
        Assert.False(cacheHit);
        Assert.NotNull(embedding);
        Assert.Equal(new float[] { 0.1f, 0.2f, 0.3f }, embedding);
        Assert.Equal(1, _fakeEmbedding.CallCount);
    }

    [Fact]
    public async Task EvictExpired_ExcludesValidEntriesAtDatabaseLevel()
    {
        var now = DateTimeOffset.UtcNow;

        // Add 3 expired and 2 valid entries.
        for (var i = 0; i < 3; i++)
        {
            _db.EmbeddingCache.Add(new EmbeddingCacheEntity
            {
                Id = Guid.NewGuid(),
                ContentHash = $"hash-expired-{i}",
                InputText = $"expired-{i}",
                EmbeddingJson = "[0.1]",
                ModelId = "model",
                Dimensions = 1,
                CreatedAt = now.AddDays(-30),
                LastAccessedAt = now.AddDays(-30),
                ExpiresAt = now.AddHours(-1),
            });
        }

        for (var i = 0; i < 2; i++)
        {
            _db.EmbeddingCache.Add(new EmbeddingCacheEntity
            {
                Id = Guid.NewGuid(),
                ContentHash = $"hash-valid-{i}",
                InputText = $"valid-{i}",
                EmbeddingJson = "[0.2]",
                ModelId = "model",
                Dimensions = 1,
                CreatedAt = now.AddHours(-1),
                LastAccessedAt = now.AddHours(-1),
                ExpiresAt = now.AddHours(23),
            });
        }

        await _db.SaveChangesAsync();

        var evicted = await _service.EvictExpiredAsync();

        Assert.Equal(3, evicted);
        Assert.Equal(2, _db.EmbeddingCache.Count());
        Assert.All(_db.EmbeddingCache, e => Assert.StartsWith("hash-valid", e.ContentHash));
    }

    [Fact]
    public async Task GetOrGenerateAsync_NullEmbeddingFromInnerService_ReturnsNull()
    {
        var nullEmbeddingService = new NullEmbeddingService();
        var costSettings = new CostOptimizationSettings { EmbeddingCacheTtlHours = 24 };
        var service = new EmbeddingCacheService(
            _db,
            nullEmbeddingService,
            new EmbeddingSettings(),
            costSettings,
            NullLogger<EmbeddingCacheService>.Instance);

        var (embedding, cacheHit) = await service.GetOrGenerateAsync("test query");

        Assert.Null(embedding);
        Assert.False(cacheHit);
        Assert.Empty(_db.EmbeddingCache); // Nothing cached.
    }

    [Fact]
    public async Task GetOrGenerateAsync_EmptyEmbeddingFromInnerService_ReturnsNull()
    {
        var emptyEmbeddingService = new EmptyEmbeddingService();
        var costSettings = new CostOptimizationSettings { EmbeddingCacheTtlHours = 24 };
        var service = new EmbeddingCacheService(
            _db,
            emptyEmbeddingService,
            new EmbeddingSettings(),
            costSettings,
            NullLogger<EmbeddingCacheService>.Instance);

        var (embedding, cacheHit) = await service.GetOrGenerateAsync("test query");

        Assert.Null(embedding);
        Assert.False(cacheHit);
        Assert.Empty(_db.EmbeddingCache); // Nothing cached.
    }

    private class FakeEmbeddingService : IEmbeddingService
    {
        public int CallCount { get; private set; }

        public Task<float[]> GenerateEmbeddingAsync(string text, CancellationToken cancellationToken = default)
        {
            CallCount++;
            return Task.FromResult(new float[] { 0.1f, 0.2f, 0.3f });
        }
    }

    private class NullEmbeddingService : IEmbeddingService
    {
        public Task<float[]> GenerateEmbeddingAsync(string text, CancellationToken cancellationToken = default)
        {
            return Task.FromResult<float[]>(null!);
        }
    }

    private class EmptyEmbeddingService : IEmbeddingService
    {
        public Task<float[]> GenerateEmbeddingAsync(string text, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Array.Empty<float>());
        }
    }
}
