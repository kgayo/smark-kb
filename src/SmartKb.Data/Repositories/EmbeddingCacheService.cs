using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SmartKb.Contracts.Configuration;
using SmartKb.Contracts.Services;
using SmartKb.Data.Entities;

namespace SmartKb.Data.Repositories;

/// <summary>
/// SQL-backed embedding cache that wraps the underlying IEmbeddingService (P2-003).
/// Caches embeddings by SHA-256 content hash with configurable TTL.
/// </summary>
public sealed class EmbeddingCacheService : IEmbeddingCacheService
{
    private readonly SmartKbDbContext _db;
    private readonly IEmbeddingService _innerEmbeddingService;
    private readonly EmbeddingSettings _embeddingSettings;
    private readonly CostOptimizationSettings _costSettings;
    private readonly ILogger<EmbeddingCacheService> _logger;

    public EmbeddingCacheService(
        SmartKbDbContext db,
        IEmbeddingService innerEmbeddingService,
        EmbeddingSettings embeddingSettings,
        CostOptimizationSettings costSettings,
        ILogger<EmbeddingCacheService> logger)
    {
        _db = db;
        _innerEmbeddingService = innerEmbeddingService;
        _embeddingSettings = embeddingSettings;
        _costSettings = costSettings;
        _logger = logger;
    }

    public async Task<(float[]? Embedding, bool CacheHit)> GetOrGenerateAsync(
        string text,
        CancellationToken ct = default)
    {
        var contentHash = ComputeHash(text);
        var now = DateTimeOffset.UtcNow;

        // Try cache lookup (load by hash, then filter expiry in memory for SQLite compat).
        var cached = (await _db.EmbeddingCache
            .Where(c => c.ContentHash == contentHash)
            .ToListAsync(ct))
            .FirstOrDefault(c => c.ExpiresAt > now);

        if (cached is not null)
        {
            // Update last accessed time.
            cached.LastAccessedAt = now;
            await _db.SaveChangesAsync(ct);

            var embedding = JsonSerializer.Deserialize<float[]>(cached.EmbeddingJson);
            _logger.LogDebug("Embedding cache hit. ContentHash={Hash}", contentHash);
            return (embedding, true);
        }

        // Cache miss: generate fresh embedding.
        var freshEmbedding = await _innerEmbeddingService.GenerateEmbeddingAsync(text, ct);

        // Store in cache.
        var ttlHours = _costSettings.EmbeddingCacheTtlHours;
        var cacheEntry = new EmbeddingCacheEntity
        {
            Id = Guid.NewGuid(),
            ContentHash = contentHash,
            InputText = text,
            EmbeddingJson = JsonSerializer.Serialize(freshEmbedding),
            ModelId = _embeddingSettings.ModelId,
            Dimensions = freshEmbedding.Length,
            CreatedAt = now,
            LastAccessedAt = now,
            ExpiresAt = now.AddHours(ttlHours),
        };

        _db.EmbeddingCache.Add(cacheEntry);

        try
        {
            await _db.SaveChangesAsync(ct);
            _logger.LogDebug("Embedding cached. ContentHash={Hash}, TTL={TtlHours}h", contentHash, ttlHours);
        }
        catch (DbUpdateException ex)
        {
            // Concurrent insert for same hash is benign — log and continue.
            _logger.LogDebug(ex, "Embedding cache insert conflict (concurrent). ContentHash={Hash}", contentHash);
        }

        return (freshEmbedding, false);
    }

    public async Task<int> EvictExpiredAsync(CancellationToken ct = default)
    {
        var now = DateTimeOffset.UtcNow;
        var expired = (await _db.EmbeddingCache.ToListAsync(ct))
            .Where(c => c.ExpiresAt <= now)
            .ToList();

        if (expired.Count == 0) return 0;

        _db.EmbeddingCache.RemoveRange(expired);
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation("Evicted {Count} expired embedding cache entries.", expired.Count);
        return expired.Count;
    }

    internal static string ComputeHash(string text)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(text));
        return Convert.ToHexStringLower(bytes);
    }
}
