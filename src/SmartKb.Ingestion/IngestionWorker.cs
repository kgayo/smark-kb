namespace SmartKb.Ingestion;

public sealed class IngestionWorker : BackgroundService
{
    private readonly ILogger<IngestionWorker> _logger;

    public IngestionWorker(ILogger<IngestionWorker> logger)
    {
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Ingestion worker started at {Time}", DateTimeOffset.UtcNow);

        while (!stoppingToken.IsCancellationRequested)
        {
            await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
        }
    }
}
