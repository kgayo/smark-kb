using SmartKb.Contracts.Services;
using DiagnosticsHelper = SmartKb.Contracts.Diagnostics;

namespace SmartKb.Api;

/// <summary>
/// Background service that periodically evicts expired embedding cache entries
/// to prevent unbounded table growth (P3-035). Read-time filtering already prevents
/// stale data from being used; this worker handles storage hygiene.
/// </summary>
public sealed class EmbeddingCacheEvictionService : BackgroundService
{
    private static readonly TimeSpan DefaultInterval = TimeSpan.FromHours(6);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<EmbeddingCacheEvictionService> _logger;
    private readonly TimeProvider _timeProvider;

    public EmbeddingCacheEvictionService(
        IServiceScopeFactory scopeFactory,
        ILogger<EmbeddingCacheEvictionService> logger,
        TimeProvider? timeProvider = null)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "Embedding cache eviction service started. Interval: {IntervalHours}h",
            DefaultInterval.TotalHours);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await EvictAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Unhandled error during embedding cache eviction. Will retry next cycle.");
            }

            try
            {
                await Task.Delay(DefaultInterval, _timeProvider, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        _logger.LogInformation("Embedding cache eviction service stopping.");
    }

    internal async Task EvictAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var cacheService = scope.ServiceProvider.GetRequiredService<IEmbeddingCacheService>();

        var evicted = await cacheService.EvictExpiredAsync(ct);

        if (evicted > 0)
        {
            DiagnosticsHelper.EmbeddingCacheEvictionsTotal.Add(evicted);
            _logger.LogInformation("Evicted {Count} expired embedding cache entries.", evicted);
        }
        else
        {
            _logger.LogDebug("No expired embedding cache entries to evict.");
        }
    }
}
