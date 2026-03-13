using Microsoft.Extensions.Logging;
using SmartKb.Ingestion;

namespace SmartKb.Ingestion.Tests;

public class IngestionWorkerTests
{
    [Fact]
    public async Task Worker_StartsAndStops_Gracefully()
    {
        var logger = new LoggerFactory().CreateLogger<IngestionWorker>();
        var worker = new IngestionWorker(logger);

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));

        await worker.StartAsync(cts.Token);
        await Task.Delay(150);
        await worker.StopAsync(CancellationToken.None);
    }
}
