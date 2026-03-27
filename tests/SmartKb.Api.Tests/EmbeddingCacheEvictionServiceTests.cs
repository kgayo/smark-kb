using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SmartKb.Contracts.Configuration;
using SmartKb.Contracts.Services;
using SmartKb.Data;
using SmartKb.Data.Entities;
using SmartKb.Data.Repositories;

namespace SmartKb.Api.Tests;

public sealed class EmbeddingCacheEvictionServiceTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly ServiceProvider _sp;
    private readonly SmartKbDbContext _db;

    public EmbeddingCacheEvictionServiceTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<SmartKbDbContext>(o => o.UseSqlite(_connection));
        services.AddSingleton(new EmbeddingSettings());
        services.AddSingleton(new CostOptimizationSettings { EmbeddingCacheTtlHours = 24 });
        services.AddSingleton<IEmbeddingService, FakeEmbeddingService>();
        services.AddScoped<IEmbeddingCacheService, EmbeddingCacheService>();

        _sp = services.BuildServiceProvider();
        _db = _sp.GetRequiredService<SmartKbDbContext>();
        _db.Database.EnsureCreated();
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
        _sp.Dispose();
    }

    [Fact]
    public async Task EvictAsync_RemovesExpiredEntries()
    {
        var now = DateTimeOffset.UtcNow;

        _db.EmbeddingCache.Add(MakeEntry("hash-expired", now.AddHours(-2)));
        _db.EmbeddingCache.Add(MakeEntry("hash-valid", now.AddHours(12)));
        await _db.SaveChangesAsync();

        var service = CreateService();
        await service.EvictAsync(CancellationToken.None);

        var remaining = _db.EmbeddingCache.ToList();
        Assert.Single(remaining);
        Assert.Equal("hash-valid", remaining[0].ContentHash);
    }

    [Fact]
    public async Task EvictAsync_NoExpired_DoesNothing()
    {
        var now = DateTimeOffset.UtcNow;
        _db.EmbeddingCache.Add(MakeEntry("hash-valid", now.AddHours(12)));
        await _db.SaveChangesAsync();

        var service = CreateService();
        await service.EvictAsync(CancellationToken.None);

        Assert.Single(_db.EmbeddingCache);
    }

    [Fact]
    public async Task EvictAsync_EmptyTable_DoesNothing()
    {
        var service = CreateService();
        await service.EvictAsync(CancellationToken.None);

        Assert.Empty(_db.EmbeddingCache);
    }

    [Fact]
    public async Task EvictAsync_AllExpired_RemovesAll()
    {
        var now = DateTimeOffset.UtcNow;
        _db.EmbeddingCache.Add(MakeEntry("hash-1", now.AddHours(-2)));
        _db.EmbeddingCache.Add(MakeEntry("hash-2", now.AddHours(-1)));
        _db.EmbeddingCache.Add(MakeEntry("hash-3", now.AddMinutes(-5)));
        await _db.SaveChangesAsync();

        var service = CreateService();
        await service.EvictAsync(CancellationToken.None);

        Assert.Empty(_db.EmbeddingCache);
    }

    [Fact]
    public async Task ExecuteAsync_StopsOnCancellation()
    {
        using var cts = new CancellationTokenSource();
        var service = CreateService();

        cts.Cancel();
        await service.StartAsync(cts.Token);
        await service.StopAsync(CancellationToken.None);

        // If we get here without hanging, the service respects cancellation.
    }

    [Fact]
    public async Task ExecuteAsync_RunsEvictionCycle()
    {
        var now = DateTimeOffset.UtcNow;
        _db.EmbeddingCache.Add(MakeEntry("hash-expired", now.AddHours(-1)));
        await _db.SaveChangesAsync();

        using var cts = new CancellationTokenSource();
        var service = CreateService();

        // Start the service, let it run one cycle, then stop.
        await service.StartAsync(CancellationToken.None);

        // Give it time to run the first eviction cycle.
        await Task.Delay(200);
        await service.StopAsync(CancellationToken.None);

        Assert.Empty(_db.EmbeddingCache);
    }

    [Fact]
    public async Task EvictAsync_PropagatesCancellation()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var service = CreateService();
        await Assert.ThrowsAsync<OperationCanceledException>(
            () => service.EvictAsync(cts.Token));
    }

    private EmbeddingCacheEvictionService CreateService()
    {
        return new EmbeddingCacheEvictionService(
            _sp.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<EmbeddingCacheEvictionService>.Instance);
    }

    private static EmbeddingCacheEntity MakeEntry(string hash, DateTimeOffset expiresAt)
    {
        var now = DateTimeOffset.UtcNow;
        return new EmbeddingCacheEntity
        {
            Id = Guid.NewGuid(),
            ContentHash = hash,
            InputText = $"text-{hash}",
            EmbeddingJson = "[0.1, 0.2, 0.3]",
            ModelId = "text-embedding-3-large",
            Dimensions = 3,
            CreatedAt = now.AddHours(-24),
            LastAccessedAt = now.AddHours(-24),
            ExpiresAt = expiresAt,
            ExpiresAtEpoch = expiresAt.ToUnixTimeSeconds(),
        };
    }

    private class FakeEmbeddingService : IEmbeddingService
    {
        public Task<float[]> GenerateEmbeddingAsync(string text, CancellationToken ct = default)
            => Task.FromResult(new float[] { 0.1f, 0.2f, 0.3f });
    }
}
